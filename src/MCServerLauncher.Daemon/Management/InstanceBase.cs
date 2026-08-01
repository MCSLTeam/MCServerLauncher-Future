using System.Diagnostics;
using System.Collections.Concurrent;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Management.Minecraft;
using Serilog;
using DisposableObject = MCServerLauncher.Daemon.Utils.DisposableObject;

namespace MCServerLauncher.Daemon.Management;

public abstract class InstanceBase : DisposableObject, IInstance, IInstanceReportFactSource, IInstanceProcessGenerationSource
{
    private const int MaximumLogHistoryLines = 500;
    private const string LifecycleLogPrefix = "[MCSL] Instance";

    /// <summary>
    /// How long a halt or a restart waits for the previous generation to finish draining before it
    /// detaches the generation and lets the rest finish in the background.
    /// </summary>
    private static readonly TimeSpan DefaultDrainDeadline = TimeSpan.FromSeconds(5);
    private readonly Func<ProcessStartInfo, InstanceType, ConsoleMode, InstanceProcess> _processFactory;
    private readonly object _processBindingGate = new();
    private ProcessBinding? _processBinding;
    private long _nextProcessGeneration;
    private readonly ConcurrentQueue<string> _logHistory = new();
    protected InstanceConfig ProtectedConfig;
    private int _lastStatus = (int)InstanceStatus.Stopped;
    private int _lastReadyTimedOut;
    private event Func<IInstance, InstanceReportFact, CancellationToken, Task>? ReportFactChanged;
    private event Func<IInstance, long, string, CancellationToken, Task>? ProcessLogReceived;
    private event Func<IInstance, long, InstanceStatus, CancellationToken, Task>? ProcessStatusChanged;
    private event Func<IInstance, long, InstanceReportFact, CancellationToken, Task>? ProcessReportFactChanged;

    protected InstanceBase(InstanceConfig config)
        : this(
            config,
            static (startInfo, instanceType, consoleMode) =>
                new InstanceProcess(startInfo, instanceType, consoleMode))
    {
    }

    internal InstanceBase(
        InstanceConfig config,
        Func<ProcessStartInfo, InstanceType, ConsoleMode, InstanceProcess> processFactory)
    {
        ProtectedConfig = config;
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
    }

    public InstanceConfig Config => ProtectedConfig;

    public InstanceProcess? Process => Volatile.Read(ref _processBinding)?.Source;
    public InstanceStatus Status => Process?.Status ?? (InstanceStatus)Volatile.Read(ref _lastStatus);
    public bool ReadyTimedOut => Process?.ReadyTimedOut ?? Volatile.Read(ref _lastReadyTimedOut) != 0;
    public int ServerProcessId => Process?.ServerProcessId ?? -1;

    event Func<IInstance, InstanceReportFact, CancellationToken, Task>? IInstanceReportFactSource.ReportFactChanged
    {
        add => ReportFactChanged += value;
        remove => ReportFactChanged -= value;
    }

    long IInstanceProcessGenerationSource.CurrentProcessGeneration =>
        Volatile.Read(ref _processBinding)?.Generation ?? 0;

    event Func<IInstance, long, string, CancellationToken, Task>?
        IInstanceProcessGenerationSource.ProcessLogReceived
    {
        add => ProcessLogReceived += value;
        remove => ProcessLogReceived -= value;
    }

    event Func<IInstance, long, InstanceStatus, CancellationToken, Task>?
        IInstanceProcessGenerationSource.ProcessStatusChanged
    {
        add => ProcessStatusChanged += value;
        remove => ProcessStatusChanged -= value;
    }

    event Func<IInstance, long, InstanceReportFact, CancellationToken, Task>?
        IInstanceProcessGenerationSource.ProcessReportFactChanged
    {
        add => ProcessReportFactChanged += value;
        remove => ProcessReportFactChanged -= value;
    }

    public event Func<Guid, string, CancellationToken, Task>? OnLog;
    public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

    public virtual async Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var process = Process;
        return new InstanceReport(
            process?.Status ?? (InstanceStatus)Volatile.Read(ref _lastStatus),
            Config,
            new Dictionary<string, string>(),
            [],
            process is null ? default : await process.Monitor.GetMonitorData(),
            process?.ReadyTimedOut ?? Volatile.Read(ref _lastReadyTimedOut) != 0);
    }

    public async Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Always clear any previous InstanceProcess before a new start. Unix PTY children are
        // attached via Process.GetProcessById; after Kill/halt HasExited may stay false, which
        // previously made the next StartAsync return false permanently until daemon restart.
        if (Volatile.Read(ref _processBinding) is { } existingBinding)
            await DisposeManagedProcessAsync(existingBinding, DefaultDrainDeadline, ct).ConfigureAwait(false);

        var startInfoResult = Config.TryGetStartInfo();
        if (startInfoResult.IsErr(out var error))
        {
            Log.Error(
                "[Instance] Failed to build start info for instance '{InstanceId}' ({ErrorCode}): {ErrorMessage}",
                Config.Uuid,
                error!.Code,
                error.Message);
            return false;
        }

        var startInfo = startInfoResult.Unwrap();
        var process = _processFactory(
            startInfo,
            Config.InstanceType,
            Config.ConsoleMode);
        var binding = AttachProcess(process);

        try
        {
            ct.ThrowIfCancellationRequested();
            var started = await process.StartAsync(delayToCheck, ct);
            if (!started)
            {
                Log.Error(
                    "[Instance] Operating system process start was rejected for '{InstanceId}' (console_mode={ConsoleMode})",
                    Config.Uuid,
                    Config.ConsoleMode);
                ResetProcess(binding);
            }

            return started;
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "[Instance] Start threw for '{InstanceId}' (console_mode={ConsoleMode})",
                Config.Uuid,
                Config.ConsoleMode);
            ResetProcess(binding);
            throw;
        }
    }

    public virtual async Task<bool> StopAsync(CancellationToken ct = default)
    {
        // Default stop is RequestStopping + non-blocking kill. MinecraftInstance overrides to send "stop".
        var stopProcess = Process;
        if (stopProcess is null)
            return false;

        // Return after Stopping succeeds; do not wait for OS exit.
        if (!await stopProcess.RequestStoppingAsync(ct).ConfigureAwait(false))
            return false;

        stopProcess.KillProcess(waitForExit: false);
        return true;
    }

    /// <summary>
    /// Immediately kills the managed process and drops <see cref="Process"/> so a later
    /// <see cref="StartAsync"/> cannot be blocked by a stale HasExited=false handle.
    /// </summary>
    public Task ForceKillAndClearAsync(CancellationToken ct = default) =>
        ForceKillAndClearAsync(DefaultDrainDeadline, ct);

    internal async Task ForceKillAndClearAsync(TimeSpan drainDeadline, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _processBinding) is { } binding)
            await DisposeManagedProcessAsync(binding, drainDeadline, ct).ConfigureAwait(false);
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return _logHistory.ToArray();
    }

    protected override void ProtectedDispose()
    {
        if (Volatile.Read(ref _processBinding) is { } binding)
            ResetProcess(binding);
    }

    private async Task OnProcessLogAsync(
        ProcessBinding binding,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Task managerPublication;
        lock (_processBindingGate)
        {
            if (!IsCurrentBinding(binding))
                return;

            AddLogHistory(message);
            managerPublication = InvokeAsync(
                ProcessLogReceived,
                this,
                binding.Generation,
                message,
                cancellationToken);
        }

        await managerPublication.ConfigureAwait(false);
        if (!IsCurrentBinding(binding))
            return;

        await InvokeAsync(OnLog, Config.Uuid, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Terminates the process tree and detaches the generation. Starting a new generation waits for
    /// the previous process tree, output and lifecycle publication tail to drain — but the deadline
    /// bounds that wait, never the work behind it.
    /// </summary>
    /// <remarks>
    /// Nothing in the drain is ours to bound. A redirected pipe reaches EOF only when the last
    /// inherited copy of its write handle closes anywhere on the machine, and the publication tail is
    /// only as fast as its slowest subscriber. Waiting on a party we do not control while holding the
    /// per-instance mutation gate is what wedges every later operation on the instance — and on the
    /// restart path it is worse, because a generation that never finishes draining means the instance
    /// can never start again.
    /// </remarks>
    private async Task DisposeManagedProcessAsync(
        ProcessBinding binding,
        TimeSpan drainDeadline,
        CancellationToken ct)
    {
        var drain = binding.Source.KillAndDrainAsync(ct);
        try
        {
            await drain.WaitAsync(drainDeadline).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Detaching is what fences the straggler, and it is safe here because termination was
            // already requested and the terminal status already committed, synchronously, before the
            // drain's first await. Every publication path re-checks the current binding, so once the
            // binding is gone the abandoned generation can no longer reach the catalog however long
            // it takes to finish.
            Log.Warning(
                "[Instance] Process drain exceeded {DrainDeadline} for '{InstanceId}'; detaching the old generation and letting it finish in the background.",
                drainDeadline,
                Config.Uuid);
            ObserveDetachedDrain(drain);
        }
        catch (Exception exception)
        {
            // The drain failed rather than outran us, so the kill is not known to have taken. Keep
            // the binding and report the failure instead of a false halt success.
            Log.Warning(
                exception,
                "[Instance] Failed to drain the process tree for '{InstanceId}'",
                Config.Uuid);
            throw;
        }

        ResetProcess(binding);
    }

    /// <summary>
    /// Keeps an abandoned drain's failure from surfacing as an unobserved task exception. There is
    /// nothing to do about it beyond recording it: the generation is already fenced.
    /// </summary>
    private void ObserveDetachedDrain(Task drain) =>
        _ = drain.ContinueWith(
            static (task, state) => Log.Warning(
                task.Exception,
                "[Instance] Detached process drain for '{InstanceId}' faulted after its deadline.",
                state),
            Config.Uuid,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private ProcessBinding AttachProcess(InstanceProcess process)
    {
        var binding = new ProcessBinding(
            this,
            process,
            Interlocked.Increment(ref _nextProcessGeneration));
        process.OnStatusChanged += binding.StatusHandler;
        process.OnReportFactChanged += binding.ReportFactHandler;
        process.OnLog += binding.LogHandler;

        lock (_processBindingGate)
        {
            if (Interlocked.CompareExchange(ref _processBinding, binding, null) is null)
                return binding;
        }

        process.OnStatusChanged -= binding.StatusHandler;
        process.OnReportFactChanged -= binding.ReportFactHandler;
        process.OnLog -= binding.LogHandler;
        throw new InvalidOperationException("An instance process is already attached.");
    }

    private void ResetProcess(ProcessBinding binding)
    {
        var process = binding.Source;
        lock (_processBindingGate)
        {
            if (!ReferenceEquals(
                    Interlocked.CompareExchange(ref _processBinding, null, binding),
                    binding))
            {
                return;
            }

            CaptureProcessFact(process);
        }

        process.OnStatusChanged -= binding.StatusHandler;
        process.OnReportFactChanged -= binding.ReportFactHandler;
        process.OnLog -= binding.LogHandler;
        try
        {
            process.Close();
        }
        catch
        {
        }

        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    private async Task OnProcessStatusChangedAsync(
        ProcessBinding binding,
        InstanceStatus status,
        CancellationToken cancellationToken)
    {
        Task managerPublication;
        lock (_processBindingGate)
        {
            if (!IsCurrentBinding(binding))
                return;

            Volatile.Write(ref _lastStatus, (int)status);
            Volatile.Write(ref _lastReadyTimedOut, 0);
            managerPublication = InvokeAsync(
                ProcessStatusChanged,
                this,
                binding.Generation,
                status,
                cancellationToken);
        }

        var lifecycleLog = FormatLifecycleLog(status);
        if (lifecycleLog is not null)
            await PublishLifecycleLogAsync(binding, lifecycleLog, cancellationToken).ConfigureAwait(false);

        if (!IsCurrentBinding(binding))
            return;

        await managerPublication.ConfigureAwait(false);
        if (!IsCurrentBinding(binding))
            return;

        await InvokeAsync(OnStatusChanged, Config.Uuid, status, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishLifecycleLogAsync(
        ProcessBinding binding,
        string message,
        CancellationToken cancellationToken)
    {
        Task managerPublication;
        lock (_processBindingGate)
        {
            if (!IsCurrentBinding(binding))
                return;

            AddLogHistory(message);
            managerPublication = InvokeAsync(
                ProcessLogReceived,
                this,
                binding.Generation,
                message,
                cancellationToken);
        }

        await managerPublication.ConfigureAwait(false);
        if (!IsCurrentBinding(binding))
            return;

        await InvokeAsync(OnLog, Config.Uuid, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnProcessReportFactChangedAsync(
        ProcessBinding binding,
        InstanceReportFact fact,
        CancellationToken cancellationToken)
    {
        Task managerPublication;
        lock (_processBindingGate)
        {
            if (!IsCurrentBinding(binding))
                return;

            Volatile.Write(ref _lastStatus, (int)fact.Status);
            Volatile.Write(ref _lastReadyTimedOut, fact.ReadyTimedOut ? 1 : 0);
            managerPublication = InvokeAsync(
                ProcessReportFactChanged,
                this,
                binding.Generation,
                fact,
                cancellationToken);
        }

        await managerPublication.ConfigureAwait(false);
        if (!IsCurrentBinding(binding))
            return;

        await InvokeAsync(ReportFactChanged, this, fact, cancellationToken).ConfigureAwait(false);
    }

    private void CaptureProcessFact(InstanceProcess? process)
    {
        if (process is null)
            return;

        Volatile.Write(ref _lastStatus, (int)process.Status);
        Volatile.Write(ref _lastReadyTimedOut, process.ReadyTimedOut ? 1 : 0);
    }

    private void AddLogHistory(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _logHistory.Enqueue(message);
        while (_logHistory.Count > MaximumLogHistoryLines)
            _logHistory.TryDequeue(out _);
    }

    private static string? FormatLifecycleLog(InstanceStatus status) => status switch
    {
        InstanceStatus.Starting => LifecycleLogPrefix + " starting.",
        InstanceStatus.Stopped => LifecycleLogPrefix + " stopped.",
        _ => null
    };

    private bool IsCurrentBinding(ProcessBinding binding)
    {
        var current = Volatile.Read(ref _processBinding);
        return current is not null &&
               ReferenceEquals(current.Source, binding.Source) &&
               current.Generation == binding.Generation;
    }

    private static async Task InvokeAsync<T>(
        Func<Guid, T, CancellationToken, Task>? handlers,
        Guid instanceId,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Guid, T, CancellationToken, Task>>())
            await handler(instanceId, value, cancellationToken);
    }

    private static async Task InvokeAsync<T>(
        Func<IInstance, T, CancellationToken, Task>? handlers,
        IInstance instance,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<IInstance, T, CancellationToken, Task>>())
            await handler(instance, value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InvokeAsync<T>(
        Func<IInstance, long, T, CancellationToken, Task>? handlers,
        IInstance instance,
        long generation,
        T value,
        CancellationToken cancellationToken)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Func<IInstance, long, T, CancellationToken, Task>>())
        {
            await handler(instance, generation, value, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ProcessBinding
    {
        internal ProcessBinding(InstanceBase owner, InstanceProcess source, long generation)
        {
            Source = source;
            Generation = generation;
            StatusHandler = (status, cancellationToken) =>
                owner.OnProcessStatusChangedAsync(this, status, cancellationToken);
            ReportFactHandler = (fact, cancellationToken) =>
                owner.OnProcessReportFactChangedAsync(this, fact, cancellationToken);
            LogHandler = (message, cancellationToken) =>
                owner.OnProcessLogAsync(this, message, cancellationToken);
        }

        internal InstanceProcess Source { get; }

        internal long Generation { get; }

        internal Func<InstanceStatus, CancellationToken, Task> StatusHandler { get; }

        internal Func<InstanceReportFact, CancellationToken, Task> ReportFactHandler { get; }

        internal Func<string, CancellationToken, Task> LogHandler { get; }

    }
}
