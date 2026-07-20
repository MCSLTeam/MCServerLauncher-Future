using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Pty;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Communicate;

public class InstanceProcess : DisposableObject
{
    private readonly ProcessStartInfo _startInfo;
    private readonly bool _isMcServer;
    private readonly ConsoleMode _consoleMode;
    private readonly ConcurrentQueue<string> _logHistory = new();
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly SemaphoreSlim _statusGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ConsoleSubscriber> _consoleSubscribers = new();
    private readonly object _lineBufferGate = new();
    private Process? _process;
    private IInstanceConsoleHost? _consoleHost;
    private Task? _stdoutPumpTask;
    private Task? _stderrPumpTask;
    private Task? _ptyPumpTask;
    private Task? _completionTask;
    private int _processStarted;
    private int _runningPublished;
    private int _finalized;
    private long _consoleOutputOffset;
    private string _lineCarry = string.Empty;
    private Encoding _outputEncoding = Encoding.UTF8;
    private Encoding _inputEncoding = Encoding.UTF8;

    public InstanceProcess(
        ProcessStartInfo info,
        bool isMcServer,
        ConsoleMode consoleMode = ConsoleMode.Pipe,
        int monitorFrequency = 2000)
    {
        _startInfo = info;
        _isMcServer = isMcServer;
        _consoleMode = consoleMode;
        Monitor = new ProcessMonitor(this, monitorFrequency);
    }

    public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
    public int ServerProcessId { get; private set; } = -1;
    /// <summary>
    /// True when the managed process has exited or lifecycle was force-finalized.
    /// Unix PTY uses <see cref="Process.GetProcessById"/>; after Kill, <c>HasExited</c>
    /// can stay false on some platforms, so honor <c>_finalized</c> from KillProcess.
    /// </summary>
    public bool HasExit =>
        Volatile.Read(ref _finalized) != 0 || (_process?.HasExited ?? true);
    public bool IsPty => _consoleHost?.IsPty == true;
    public ProcessMonitor Monitor { get; }

    public event Func<InstanceStatus, CancellationToken, Task>? OnStatusChanged;
    public event Func<string, CancellationToken, Task>? OnLog;
    internal Task Completion => _completionTask ?? Task.CompletedTask;

    public async Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
    {
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

            await Task.Delay(delayToCheck, ct);
            if (process.HasExited)
            {
                var exitCode = -1;
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                }

                Log.Error(
                    "[InstanceProcess] Child exited within {DelayMs}ms (exit={ExitCode}, file={FileName}, pty={IsPty})",
                    delayToCheck,
                    exitCode,
                    fileName,
                    host.IsPty);
                await _completionTask.WaitAsync(CancellationToken.None);
                return false;
            }

            ServerProcessId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ProcessTreeHelper.FindSubProcessPid(process.Id, fileName)
                : process.Id;
            if (!_isMcServer)
                await PublishRunningAsync(ct);
            return true;
        }
        catch
        {
            await TerminateAndDrainAsync();
            throw;
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        var completion = _completionTask;
        if (completion is null)
        {
            if (_process is not null)
                await _process.WaitForExitAsync(ct);
            return;
        }

        await completion.WaitAsync(ct);
    }

    public void Close()
    {
        _process?.Close();
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return _logHistory.ToArray();
    }

    public void KillProcess()
    {
        // Unblock PumpPtyAsync and release master FDs before/while killing so halt
        // does not hang the RPC thread. Always force-publish Stopped so UI does not
        // stick on running/starting if FinalizeProcessAsync is delayed.
        try
        {
            _pumpCancellation.Cancel();
        }
        catch
        {
        }

        try
        {
            _consoleHost?.Dispose();
        }
        catch
        {
        }

        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            // Never block RPC (halt/stop) on an unbounded WaitForExit.
            try
            {
                if (!process.WaitForExit(milliseconds: 2_000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    _ = process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch
            {
            }
        }

// Clear status immediately so reports / UI do not stick on running while
        // FinalizeProcessAsync may still be waiting on WaitForExitAsync.
        // Also mark lifecycle finished so HasExit becomes true even when
        // Process.HasExited stays false for GetProcessById-backed PTY children.
        try
        {
            ForcePublishStopped();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Marks the process stopped and clears PID without waiting for pump completion.
    /// Idempotent with <see cref="PublishStoppedAsync"/> via <c>_finalized</c>.
    /// </summary>
    private void ForcePublishStopped()
    {
        var alreadyFinal =
            Volatile.Read(ref _finalized) != 0 &&
            Status == InstanceStatus.Stopped &&
            ServerProcessId < 0;
        Volatile.Write(ref _finalized, 1);
        ServerProcessId = -1;
        Status = InstanceStatus.Stopped;
        if (alreadyFinal)
            return;

        // Best-effort event; FinalizeProcessAsync may also call PublishStoppedAsync.
        _ = InvokeAsync(OnStatusChanged, InstanceStatus.Stopped, CancellationToken.None);
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
        var id = Guid.CreateVersion7();
        _consoleSubscribers[id] = new ConsoleSubscriber(handler);
        return id;
    }

    public void DetachConsoleSubscriber(Guid subscriberId)
    {
        _consoleSubscribers.TryRemove(subscriberId, out _);
    }

    protected override void ProtectedDispose()
    {
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

        _process?.Dispose();
        _pumpCancellation.Dispose();
        _statusGate.Dispose();
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
                await FanOutConsoleOutputAsync(chunk, offset).ConfigureAwait(false);
                FeedLineSplitter(chunk.Span);
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

    private void FeedLineSplitter(ReadOnlySpan<byte> chunk)
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
                _ = PublishLogLineAsync(line, isStandardError: false);
            }
        }
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
        await InvokeAsync(OnLog, message, CancellationToken.None).ConfigureAwait(false);

        if (!isStandardError && _isMcServer)
        {
            if (ProcessMonitor.DonePattern.IsMatch(message.TrimEnd()))
                await PublishRunningAsync(CancellationToken.None).ConfigureAwait(false);
            else if (message.Contains("Minecraft has crashed", StringComparison.Ordinal))
                await PublishStatusAsync(InstanceStatus.Crashed, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FanOutConsoleOutputAsync(ReadOnlyMemory<byte> chunk, long offset)
    {
        if (chunk.Length == 0)
            return;

        // Snapshot subscribers so a failing handler cannot mutate the collection mid-fan-out.
        var subscribers = _consoleSubscribers.ToArray();
        if (subscribers.Length == 0)
            return;

        // Copy once: handlers may run concurrently and the pump reuses its read buffer.
        var owned = chunk.ToArray();
        var memory = owned.AsMemory();
        foreach (var pair in subscribers)
        {
            try
            {
                await pair.Value.Handler(memory, offset, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "[InstanceProcess] Console subscriber {SubscriberId} failed; detaching.",
                    pair.Key);
                _consoleSubscribers.TryRemove(pair.Key, out _);
            }
        }
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

        try
        {
            // Pumps should end after exit / host dispose; bound only pump drain.
            await Task.WhenAll(stdoutPumpTask, stderrPumpTask)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            pumpFailure = exception;
        }

        lock (_lineBufferGate)
        {
            if (_lineCarry.Length > 0)
            {
                var remaining = _lineCarry;
                _lineCarry = string.Empty;
                _ = PublishLogLineAsync(remaining, isStandardError: false);
            }
        }

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
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
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
    }

    private void AddLogHistory(string log)
    {
        _logHistory.Enqueue(log);
        const int maxLogHistory = 500;
        while (_logHistory.Count > maxLogHistory)
            _logHistory.TryDequeue(out _);
    }

    private async Task PublishRunningAsync(CancellationToken cancellationToken)
    {
        await _statusGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _finalized) != 0 || HasExit || Interlocked.Exchange(ref _runningPublished, 1) != 0)
                return;

            await ChangeStatusAsync(InstanceStatus.Running, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private async Task PublishStoppedAsync()
    {
        await _statusGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var alreadyFinal = Volatile.Read(ref _finalized) != 0 &&
                               Status == InstanceStatus.Stopped &&
                               ServerProcessId < 0;
            Volatile.Write(ref _finalized, 1);
            // Clear PID so clients do not treat a stopped instance as still alive.
            ServerProcessId = -1;
            if (alreadyFinal)
                return;

            await ChangeStatusAsync(InstanceStatus.Stopped, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private async Task PublishStatusAsync(InstanceStatus newStatus, CancellationToken cancellationToken)
    {
        await _statusGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _finalized) != 0)
                return;

            await ChangeStatusAsync(newStatus, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _statusGate.Release();
        }
    }

    private Task ChangeStatusAsync(InstanceStatus newStatus, CancellationToken cancellationToken)
    {
        Status = newStatus;
        return InvokeAsync(OnStatusChanged, newStatus, cancellationToken);
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

    private sealed record ConsoleSubscriber(Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> Handler);

    public class ProcessMonitor
    {
        // Vanilla / Paper / most forks. Allow trailing noise after help hint (PTY/loggers).
        public static readonly Regex DonePattern = new(
            @"Done \(\d+\.\d{1,3}s\)! For help, type [""']help[""'](?:\s+or\s+[""']\?[""'])?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
