using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Pty;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Communicate;

public class InstanceProcess : DisposableObject
{
    private const int LogSubscriberCapacity = 256;
    private const int ConsoleSubscriberCapacity = 64;
    private readonly ProcessStartInfo _startInfo;
    private readonly IInstanceLifecycleObserver _lifecycleObserver;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _readyTimeout;
    private readonly ConsoleMode _consoleMode;
    private readonly ConcurrentQueue<string> _logHistory = new();
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly object _lifecycleStateGate = new();
    private readonly object _logSubscriberGate = new();
    private readonly List<LogSubscriber> _logSubscribers = [];
    private readonly ConcurrentDictionary<Guid, ConsoleSubscriber> _consoleSubscribers = new();
    private readonly object _lineBufferGate = new();
    private Process? _process;
    private IInstanceConsoleHost? _consoleHost;
    private Task? _stdoutPumpTask;
    private Task? _stderrPumpTask;
    private Task? _ptyPumpTask;
    private Task? _completionTask;
    private Task? _processReadyTask;
    private Task _publicationTail = Task.CompletedTask;
    private ITimer? _readyTimeoutTimer;
    private int _processStarted;
    private int _readyTimedOut;
    private int _terminalCommitted;
    private int _finalized;
    private int _status = (int)InstanceStatus.Stopped;
    private long _consoleOutputOffset;
    private string _lineCarry = string.Empty;
    private Encoding _outputEncoding = Encoding.UTF8;
    private Encoding _inputEncoding = Encoding.UTF8;

    public InstanceProcess(
        ProcessStartInfo info,
        InstanceType instanceType,
        ConsoleMode consoleMode = ConsoleMode.Pipe,
        int monitorFrequency = 2000)
        : this(
            info,
            InstanceLifecycleObserverFactory.Create(instanceType),
            consoleMode,
            monitorFrequency,
            TimeProvider.System,
            readyTimeout: null)
    {
    }

    internal InstanceProcess(
        ProcessStartInfo info,
        IInstanceLifecycleObserver lifecycleObserver,
        ConsoleMode consoleMode = ConsoleMode.Pipe,
        int monitorFrequency = 2000,
        TimeProvider? timeProvider = null,
        TimeSpan? readyTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(lifecycleObserver);
        var effectiveReadyTimeout = readyTimeout ?? lifecycleObserver.DefaultReadyTimeout;
        if (effectiveReadyTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(readyTimeout), effectiveReadyTimeout, "Ready timeout must be positive.");

        _startInfo = info;
        _lifecycleObserver = lifecycleObserver;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _readyTimeout = effectiveReadyTimeout;
        _consoleMode = consoleMode;
        Monitor = new ProcessMonitor(this, monitorFrequency);
    }

    public InstanceStatus Status => (InstanceStatus)Volatile.Read(ref _status);
    public bool ReadyTimedOut => Volatile.Read(ref _readyTimedOut) != 0;
    public int ServerProcessId { get; private set; } = -1;
    /// <summary>
    /// True when the managed process has exited or lifecycle was force-finalized.
    /// Unix PTY uses <see cref="Process.GetProcessById"/>; after Kill, <c>HasExited</c>
    /// can stay false on some platforms, so honor <c>_finalized</c> from KillProcess.
    /// </summary>
    public bool HasExit =>
        Volatile.Read(ref _finalized) != 0 || _process is null || HasActuallyExited(_process);
    public bool IsPty => _consoleHost?.IsPty == true;
    public ProcessMonitor Monitor { get; }

    public event Func<InstanceStatus, CancellationToken, Task>? OnStatusChanged;
    internal event Func<InstanceReportFact, CancellationToken, Task>? OnReportFactChanged;
    public event Func<string, CancellationToken, Task>? OnLog
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            var subscriber = new LogSubscriber(value, RemoveLogSubscriber);
            lock (_logSubscriberGate)
                _logSubscribers.Add(subscriber);
        }
        remove
        {
            if (value is null)
                return;

            LogSubscriber? subscriber = null;
            lock (_logSubscriberGate)
            {
                for (var index = _logSubscribers.Count - 1; index >= 0; index--)
                {
                    if (!_logSubscribers[index].Matches(value))
                        continue;

                    subscriber = _logSubscribers[index];
                    _logSubscribers.RemoveAt(index);
                    break;
                }
            }

            subscriber?.Dispose();
        }
    }
    internal Task Completion => _completionTask ?? Task.CompletedTask;

    internal Task WaitForLifecyclePublicationsAsync(CancellationToken cancellationToken = default) =>
        AwaitTrackedPublicationsAsync(cancellationToken);

    public async Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
    {
        // Retained for daemon-internal source compatibility. Process acceptance no longer waits
        // for a quick-exit window: a successful spawn commits Starting and returns success.
        _ = delayToCheck;
        ct.ThrowIfCancellationRequested();
        var fileName = _startInfo.FileName;
        try
        {
            var (process, host, externalLifecycle) = ConsoleHostFactory.Create(_startInfo, _consoleMode);
            _process = process;
            _consoleHost = host;

            if (_startInfo.StandardOutputEncoding is not null)
                _outputEncoding = _startInfo.StandardOutputEncoding;
            if (_startInfo.StandardInputEncoding is not null)
                _inputEncoding = _startInfo.StandardInputEncoding;

            if (!externalLifecycle)
            {
                if (!process.Start())
                    return false;
            }

            Volatile.Write(ref _processStarted, 1);

            // Commit Starting before any output pump can race with lifecycle signals.
            // Once committed, caller cancellation cannot roll back a successfully spawned process.
            await CommitStarting().ConfigureAwait(false);
            StartReadyTimeout();

            var serverProcessId = ResolveServerProcessId(process, fileName);
            lock (_lifecycleStateGate)
            {
                if (Volatile.Read(ref _finalized) == 0)
                    ServerProcessId = serverProcessId;
            }

            if (host.IsPty && host.OutputStream is not null)
            {
                _ptyPumpTask = PumpPtyAsync(host.OutputStream);
                _completionTask = FinalizeProcessAsync(_ptyPumpTask, Task.CompletedTask);
            }
            else
            {
                _stdoutPumpTask = PumpAsync(process.StandardOutput, isStandardError: false);
                _stderrPumpTask = PumpAsync(process.StandardError, isStandardError: true);
                _completionTask = FinalizeProcessAsync(_stdoutPumpTask, _stderrPumpTask);
            }

            var processReadySignal = _lifecycleObserver.ObserveProcessReady();
            if (processReadySignal != InstanceLifecycleSignal.None)
                _processReadyTask = TransitionAfterStartAsync(processReadySignal);

            return true;
        }
        catch
        {
            await TerminateAndDrainAsync();
            throw;
        }
    }

    /// <summary>
    /// Transitions to Stopping when a cooperative stop is requested.
    /// Halt/kill paths may skip this and go straight to a terminal status.
    /// No-ops when already terminal or already Stopping.
    /// </summary>
    public async Task<bool> RequestStoppingAsync(CancellationToken cancellationToken = default)
    {
        // Honor pre-flight cancellation, but once we decide to stop, commit Stopping without a
        // cancellable handler path so kill/stop write always runs after intermediate success.
        cancellationToken.ThrowIfCancellationRequested();

        Task publication;
        lock (_lifecycleStateGate)
        {
            if (Volatile.Read(ref _terminalCommitted) != 0)
                return false;

            if (Status is InstanceStatus.Crashed or InstanceStatus.Stopped or InstanceStatus.Stopping)
                return false;

            if (Status is not (InstanceStatus.Starting or InstanceStatus.Running))
                return false;

            CommitStatusLocked(InstanceStatus.Stopping, terminal: false);
            publication = _publicationTail;
        }

        StopReadyTimeout();
        await publication.ConfigureAwait(false);
        return true;
    }

    private async Task TransitionAfterStartAsync(InstanceLifecycleSignal signal)
    {
        // Yield so StartAsync can return while status is still Starting.
        await Task.Yield();
        try
        {
            await ApplyLifecycleSignalAsync(signal).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceProcess] Process-ready lifecycle transition failed.");
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        var completion = _completionTask;
        if (completion is null)
        {
            if (_process is not null)
                await _process.WaitForExitAsync(ct);
            await AwaitTrackedPublicationsAsync(ct).ConfigureAwait(false);
            return;
        }

        await completion.WaitAsync(ct).ConfigureAwait(false);
        if (_processReadyTask is { } processReady)
            await processReady.WaitAsync(ct).ConfigureAwait(false);
        await AwaitTrackedPublicationsAsync(ct).ConfigureAwait(false);
    }

    public void Close()
    {
        _process?.Close();
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return _logHistory.ToArray();
    }

    public void KillProcess(bool waitForExit = true)
    {
        var process = _process;
        if (process is not null && !HasExit)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (HasActuallyExited(process))
            {
            }
        }

        CancelPumpsAndDisposeConsoleHost();
        if (waitForExit)
            CommitForcedTerminal();
    }

    /// <summary>
    /// Requests process-tree termination and does not complete until the process tree,
    /// redirected output, and tracked lifecycle publications have drained.
    /// </summary>
    internal async Task KillAndDrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var process = _process;
        if (process is null)
        {
            await AwaitTrackedPublicationsAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var descendants = CaptureDescendantProcesses(process);
        try
        {
            // Process.Kill is asynchronous. A failure must propagate so the manager keeps the
            // binding and per-instance gate closed instead of reporting a false halt success.
            KillProcess();

            await WaitForActualExitAsync(process).ConfigureAwait(false);
            await Task.WhenAll(descendants.Select(WaitForActualExitAsync)).ConfigureAwait(false);

            var completion = _completionTask;
            if (completion is not null)
            {
                try
                {
                    await completion.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // The operating-system process tree is already gone. Preserve the stronger
                    // halt boundary while surfacing the failed publication/pump in daemon logs.
                    Log.Warning(exception, "[InstanceProcess] Process finalizer faulted after halt.");
                }
            }

            await AwaitTrackedPublicationsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            foreach (var descendant in descendants)
                descendant.Dispose();
        }
    }

    public void WriteLine(string? message)
    {
        if (message is null)
            return;
        if (_consoleHost is null)
        {
            // Match legacy Process.StandardInput access: fail when process is not started/available.
            if (_process is null)
                throw new InvalidOperationException("StandardIn has not been redirected.");
            _process.StandardInput.WriteLine(message);
            return;
        }

        _consoleHost.WriteLine(message, _inputEncoding);
    }

    public void WriteRaw(ReadOnlyMemory<byte> data)
    {
        _consoleHost?.Write(data);
    }

    public Task WriteRawAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_consoleHost is null)
            return Task.CompletedTask;
        return _consoleHost.WriteAsync(data, cancellationToken);
    }

    public void ResizeConsole(ushort columns, ushort rows)
    {
        _consoleHost?.Resize(columns, rows);
    }

    public Guid AttachConsoleSubscriber(Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.CreateVersion7();
        _consoleSubscribers[id] = new ConsoleSubscriber(id, handler, RemoveConsoleSubscriber);
        return id;
    }

    public void DetachConsoleSubscriber(Guid subscriberId)
    {
        if (_consoleSubscribers.TryRemove(subscriberId, out var subscriber))
            subscriber.Dispose();
    }

    protected override void ProtectedDispose()
    {
        StopReadyTimeout();
        _pumpCancellation.Cancel();
        if (_consoleHost is not null)
        {
            try
            {
                _consoleHost.Dispose();
            }
            catch
            {
            }
        }

        LogSubscriber[] logSubscribers;
        lock (_logSubscriberGate)
        {
            logSubscribers = [.. _logSubscribers];
            _logSubscribers.Clear();
        }
        foreach (var subscriber in logSubscribers)
            subscriber.Dispose();
        foreach (var pair in _consoleSubscribers.ToArray())
        {
            if (_consoleSubscribers.TryRemove(pair.Key, out var subscriber))
                subscriber.Dispose();
        }

        _process?.Dispose();
        _pumpCancellation.Dispose();
    }

    private async Task PumpAsync(StreamReader reader, bool isStandardError)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(CancellationToken.None);
                if (line is null)
                    return;

                var message = isStandardError ? "[STDERR] " + line : line;
                await PublishLogLineAsync(message, isStandardError).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_pumpCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task PumpPtyAsync(Stream output)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await output.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        _pumpCancellation.Token)
                    .ConfigureAwait(false);
                if (read <= 0)
                    return;

                var chunk = buffer.AsMemory(0, read);
                var offset = Interlocked.Add(ref _consoleOutputOffset, read) - read;
                FanOutConsoleOutput(chunk, offset);
                foreach (var line in ExtractCompleteLines(chunk.Span))
                    await PublishLogLineAsync(line, isStandardError: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_pumpCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private IReadOnlyList<string> ExtractCompleteLines(ReadOnlySpan<byte> chunk)
    {
        string text;
        try
        {
            text = _outputEncoding.GetString(chunk);
        }
        catch
        {
            text = Encoding.UTF8.GetString(chunk);
        }

        text = StripAnsi(text);
        List<string>? lines = null;
        lock (_lineBufferGate)
        {
            _lineCarry += text;
            while (true)
            {
                var idx = _lineCarry.IndexOf('\n');
                if (idx < 0)
                    break;
                var line = _lineCarry[..idx].TrimEnd('\r');
                _lineCarry = _lineCarry[(idx + 1)..];
                (lines ??= []).Add(line);
            }
        }

        return lines ?? [];
    }

    private static string StripAnsi(string input)
    {
        if (input.IndexOf('\u001b') < 0)
            return input;
        return AnsiRegex.Replace(input, string.Empty);
    }

    private static readonly Regex AnsiRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);

    private async Task PublishLogLineAsync(string message, bool isStandardError)
    {
        AddLogHistory(message);
        var lifecycleSignal = _lifecycleObserver.ObserveLog(message, isStandardError);
        if (lifecycleSignal != InstanceLifecycleSignal.None)
            await ApplyLifecycleSignalAsync(lifecycleSignal).ConfigureAwait(false);

        PublishLogHandlers(message);
    }

    private void PublishLogHandlers(string message)
    {
        LogSubscriber[] subscribers;
        lock (_logSubscriberGate)
            subscribers = [.. _logSubscribers];
        foreach (var subscriber in subscribers)
            subscriber.TryEnqueue(message);
    }

    private void FanOutConsoleOutput(ReadOnlyMemory<byte> chunk, long offset)
    {
        if (chunk.Length == 0)
            return;

        // Snapshot subscribers so a failing handler cannot mutate the collection mid-fan-out.
        var subscribers = _consoleSubscribers.ToArray();
        if (subscribers.Length == 0)
            return;

        // Copy once: handlers may run concurrently and the pump reuses its read buffer.
        var owned = chunk.ToArray();
        var output = new ConsoleOutput(owned, offset);
        foreach (var pair in subscribers)
            pair.Value.TryEnqueue(output);
    }

    private async Task FinalizeProcessAsync(Task stdoutPumpTask, Task stderrPumpTask)
    {
        Exception? pumpFailure = null;
        // Must wait for the real process exit. A timeout here previously marked
        // live PTY/MC servers as stopped (process_id null) while Java kept running.
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        // On Windows, closing HPCON after the root exits allows the active output pump to
        // drain the pseudoconsole's final output and observe EOF.
        _consoleHost?.NotifyProcessExited();

        try
        {
            // A terminal lifecycle fact follows complete pipe/PTY drain. External consumers
            // are isolated by bounded queues, so they cannot hold this path open.
            await Task.WhenAll(stdoutPumpTask, stderrPumpTask).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            pumpFailure = exception;
        }

        string? remaining = null;
        lock (_lineBufferGate)
        {
            if (_lineCarry.Length > 0)
            {
                remaining = _lineCarry;
                _lineCarry = string.Empty;
            }
        }

        if (remaining is not null)
            await PublishLogLineAsync(remaining, isStandardError: false).ConfigureAwait(false);

        await PublishStoppedAsync().ConfigureAwait(false);
        if (pumpFailure is not null)
            throw pumpFailure;
    }

    private async Task TerminateAndDrainAsync()
    {
        if (Volatile.Read(ref _processStarted) == 0)
            return;

        try
        {
            KillProcess();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[InstanceProcess] Failed to terminate a partially started process.");
        }

        var completion = _completionTask;
        if (completion is not null)
        {
            try
            {
                await completion.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
        else if (_process is not null)
        {
            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        await AwaitTrackedPublicationsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void AddLogHistory(string log)
    {
        _logHistory.Enqueue(log);
        const int maxLogHistory = 500;
        while (_logHistory.Count > maxLogHistory)
            _logHistory.TryDequeue(out _);
    }

    private void StartReadyTimeout()
    {
        var timer = _timeProvider.CreateTimer(
            static state => ((InstanceProcess)state!).MarkReadyTimedOut(),
            this,
            _readyTimeout,
            Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref _readyTimeoutTimer, timer)?.Dispose();

        // A test provider may invoke callbacks synchronously. Do not retain a timer after a
        // concurrent ready/terminal transition has already left Starting.
        if (Status != InstanceStatus.Starting)
            StopReadyTimeout();
    }

    private void StopReadyTimeout()
    {
        Interlocked.Exchange(ref _readyTimeoutTimer, null)?.Dispose();
    }

    private void MarkReadyTimedOut()
    {
        lock (_lifecycleStateGate)
        {
            if ((InstanceStatus)_status != InstanceStatus.Starting ||
                Volatile.Read(ref _terminalCommitted) != 0 ||
                Volatile.Read(ref _readyTimedOut) != 0)
            {
                return;
            }

            Volatile.Write(ref _readyTimedOut, 1);
            QueueReportFactPublicationLocked(
                new InstanceReportFact(InstanceStatus.Starting, ReadyTimedOut: true));
        }

        StopReadyTimeout();
    }

    private Task ApplyLifecycleSignalAsync(InstanceLifecycleSignal signal)
    {
        return signal switch
        {
            InstanceLifecycleSignal.None => Task.CompletedTask,
            InstanceLifecycleSignal.Ready => PublishRunningAsync(CancellationToken.None),
            InstanceLifecycleSignal.Crashed => PublishCrashedAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unknown lifecycle signal."),
        };
    }

    private async Task PublishRunningAsync(CancellationToken cancellationToken)
    {
        Task publication;
        lock (_lifecycleStateGate)
        {
            if (Volatile.Read(ref _terminalCommitted) != 0 || HasExit)
                return;

            if ((InstanceStatus)_status != InstanceStatus.Starting)
                return;

            CommitStatusLocked(InstanceStatus.Running, terminal: false);
            publication = _publicationTail;
        }

        StopReadyTimeout();
        await publication.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishCrashedAsync(CancellationToken cancellationToken)
    {
        Task publication;
        lock (_lifecycleStateGate)
        {
            if (Volatile.Read(ref _terminalCommitted) != 0 ||
                (InstanceStatus)_status is not (InstanceStatus.Starting or InstanceStatus.Running))
            {
                return;
            }

            CommitStatusLocked(InstanceStatus.Crashed, terminal: true);
            publication = _publicationTail;
        }

        StopReadyTimeout();
        await publication.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishStoppedAsync()
    {
        Task publication;
        lock (_lifecycleStateGate)
        {
            Volatile.Write(ref _finalized, 1);
            ServerProcessId = -1;

            if (Volatile.Read(ref _terminalCommitted) != 0)
                return;

            CommitStatusLocked(InstanceStatus.Stopped, terminal: true);
            publication = _publicationTail;
        }

        StopReadyTimeout();
        await publication.ConfigureAwait(false);
    }

    private Task CommitStarting()
    {
        lock (_lifecycleStateGate)
        {
            if (Volatile.Read(ref _terminalCommitted) != 0 || Volatile.Read(ref _finalized) != 0)
                return _publicationTail;

            CommitStatusLocked(InstanceStatus.Starting, terminal: false);
            return _publicationTail;
        }
    }

    private void CommitStatusLocked(InstanceStatus newStatus, bool terminal)
    {
        if ((InstanceStatus)_status == newStatus)
            return;

        if (terminal)
            Volatile.Write(ref _terminalCommitted, 1);

        Volatile.Write(ref _status, (int)newStatus);
        Volatile.Write(ref _readyTimedOut, 0);
        QueueStatusPublicationLocked(newStatus);
    }

    private void CommitForcedTerminal()
    {
        lock (_lifecycleStateGate)
        {
            Volatile.Write(ref _finalized, 1);
            ServerProcessId = -1;
            if (Volatile.Read(ref _terminalCommitted) != 0)
                return;

            CommitStatusLocked(InstanceStatus.Stopped, terminal: true);
        }

        StopReadyTimeout();
    }

    private void QueueStatusPublicationLocked(InstanceStatus status)
    {
        var handlers = OnStatusChanged?.GetInvocationList()
            .Cast<Func<InstanceStatus, CancellationToken, Task>>()
            .ToArray() ?? [];
        QueuePublicationLocked(
            () => InvokeAsync(handlers, status, CancellationToken.None),
            $"status '{status}'");
    }

    private void QueueReportFactPublicationLocked(InstanceReportFact fact)
    {
        var handlers = OnReportFactChanged?.GetInvocationList()
            .Cast<Func<InstanceReportFact, CancellationToken, Task>>()
            .ToArray() ?? [];
        QueuePublicationLocked(
            () => InvokeAsync(handlers, fact, CancellationToken.None),
            "ready-timeout report fact");
    }

    private void QueuePublicationLocked(Func<Task> publish, string description)
    {
        var previous = _publicationTail;
        _publicationTail = PublishAfterAsync(previous, publish, description);
    }

    private static async Task PublishAfterAsync(Task previous, Func<Task> publish, string description)
    {
        // Never invoke callbacks while the lifecycle-state lock is held.
        await Task.Yield();
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceProcess] Earlier lifecycle publication failed before {Description}.", description);
        }

        await publish().ConfigureAwait(false);
    }

    private async Task AwaitTrackedPublicationsAsync(CancellationToken cancellationToken)
    {
        Task publication;
        lock (_lifecycleStateGate)
            publication = _publicationTail;
        await publication.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int ResolveServerProcessId(Process process, string fileName)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return process.Id;

            var expectedProcessName = Path.GetFileNameWithoutExtension(fileName);
            if (string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
                return process.Id;

            return ProcessTreeHelper.FindSubProcessPid(process.Id, fileName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            try
            {
                return process.HasExited ? -1 : process.Id;
            }
            catch
            {
                return -1;
            }
        }
    }

    private void CancelPumpsAndDisposeConsoleHost()
    {
        try
        {
            _pumpCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _consoleHost?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "[InstanceProcess] Console host cleanup failed after process termination request.");
        }
    }

    private IReadOnlyList<Process> CaptureDescendantProcesses(Process process)
    {
        int rootProcessId;
        try
        {
            rootProcessId = process.Id;
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        Dictionary<int, int> processTree;
        try
        {
            processTree = ProcessTreeHelper.BuildProcessTree();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[InstanceProcess] Failed to snapshot the process tree before halt.");
            processTree = [];
        }

        var processIds = new HashSet<int> { rootProcessId };
        while (true)
        {
            var added = false;
            foreach (var (processId, parentProcessId) in processTree)
            {
                if (!processIds.Contains(parentProcessId) || !processIds.Add(processId))
                    continue;

                added = true;
            }

            if (!added)
                break;
        }

        var serverProcessId = ServerProcessId;
        if (serverProcessId > 0)
            processIds.Add(serverProcessId);
        processIds.Remove(rootProcessId);

        var descendants = new List<Process>(processIds.Count);
        foreach (var processId in processIds)
        {
            try
            {
                descendants.Add(Process.GetProcessById(processId));
            }
            catch (ArgumentException)
            {
                // The descendant exited between tree capture and handle acquisition.
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceProcess] Could not capture descendant process {ProcessId} before halt.",
                    processId);
                throw;
            }
        }

        return descendants;
    }

    private static async Task WaitForActualExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (HasActuallyExited(process))
        {
        }
    }

    private static bool HasActuallyExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Process.Close/Dispose removes the association. At that point this lifecycle
            // wrapper no longer has an operating-system process it can wait for or terminate.
            return true;
        }
    }

    private void RemoveLogSubscriber(LogSubscriber subscriber)
    {
        lock (_logSubscriberGate)
            _logSubscribers.Remove(subscriber);
        subscriber.Dispose();
    }

    private void RemoveConsoleSubscriber(Guid subscriberId)
    {
        if (_consoleSubscribers.TryRemove(subscriberId, out var subscriber))
            subscriber.Dispose();
    }

    private static async Task InvokeAsync<T>(
        Func<T, CancellationToken, Task>? handlers,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<T, CancellationToken, Task>>())
            await handler(value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InvokeAsync<T>(
        IReadOnlyList<Func<T, CancellationToken, Task>> handlers,
        T value,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
            await handler(value, cancellationToken).ConfigureAwait(false);
    }

    private sealed class LogSubscriber : IDisposable
    {
        private readonly Func<string, CancellationToken, Task> _handler;
        private readonly Action<LogSubscriber> _remove;
        private readonly Channel<string> _queue;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _disposeCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        internal LogSubscriber(
            Func<string, CancellationToken, Task> handler,
            Action<LogSubscriber> remove)
        {
            _handler = handler;
            _remove = remove;
            _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(LogSubscriberCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
            _ = ConsumeAsync();
        }

        internal bool Matches(Func<string, CancellationToken, Task> handler) =>
            _handler == handler;

        internal bool TryEnqueue(string message) =>
            Volatile.Read(ref _disposed) == 0 && _queue.Writer.TryWrite(message);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _queue.Writer.TryComplete();
                _cancellation.Cancel();
            }
            finally
            {
                _disposeCompleted.TrySetResult();
            }
        }

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (var message in _queue.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await _handler(message, _cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(exception, "[InstanceProcess] Log consumer failed; detaching.");
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                _remove(this);
                await _disposeCompleted.Task.ConfigureAwait(false);
                _cancellation.Dispose();
            }
        }
    }

    private readonly record struct ConsoleOutput(byte[] Buffer, long Offset);

    private sealed class ConsoleSubscriber : IDisposable
    {
        private readonly Guid _id;
        private readonly Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> _handler;
        private readonly Action<Guid> _remove;
        private readonly Channel<ConsoleOutput> _queue;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource _disposeCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        internal ConsoleSubscriber(
            Guid id,
            Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler,
            Action<Guid> remove)
        {
            _id = id;
            _handler = handler;
            _remove = remove;
            _queue = Channel.CreateBounded<ConsoleOutput>(new BoundedChannelOptions(ConsoleSubscriberCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
            _ = ConsumeAsync();
        }

        internal bool TryEnqueue(ConsoleOutput output) =>
            Volatile.Read(ref _disposed) == 0 && _queue.Writer.TryWrite(output);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _queue.Writer.TryComplete();
                _cancellation.Cancel();
            }
            finally
            {
                _disposeCompleted.TrySetResult();
            }
        }

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (var output in _queue.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await _handler(output.Buffer, output.Offset, _cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning(
                            exception,
                            "[InstanceProcess] Console subscriber {SubscriberId} failed; detaching.",
                            _id);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                _remove(_id);
                await _disposeCompleted.Task.ConfigureAwait(false);
                _cancellation.Dispose();
            }
        }
    }

    public class ProcessMonitor
    {
        private readonly IAsyncLazyCell<(long Memory, double Cpu)> _monitor;

        public ProcessMonitor(InstanceProcess process, int freq = 2000)
        {
            _monitor = new AsyncTimedLazyCell<(long Memory, double Cpu)>(() =>
            {
                if (process.Status == InstanceStatus.Running &&
                    process.ServerProcessId != -1 &&
                    !process.HasExit)
                {
                    return ProcessInfo.GetProcessUsageAsync(process.ServerProcessId);
                }

                return Task.FromResult((0L, 0.0));
            }, TimeSpan.FromMilliseconds(freq));
        }

        public async Task<InstancePerformanceCounter> GetMonitorData()
        {
            var (mem, cpu) = await _monitor.Value.ConfigureAwait(false);
            return new InstancePerformanceCounter(cpu, mem);
        }
    }
}
