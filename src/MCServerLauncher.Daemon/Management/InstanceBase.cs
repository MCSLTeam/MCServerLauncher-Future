using System.Diagnostics;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Management.Minecraft;
using Serilog;
using DisposableObject = MCServerLauncher.Daemon.Utils.DisposableObject;

namespace MCServerLauncher.Daemon.Management;

public abstract class InstanceBase : DisposableObject, IInstance, IInstanceReportFactSource, IInstanceProcessGenerationSource
{
    private readonly Func<ProcessStartInfo, InstanceType, ConsoleMode, InstanceProcess> _processFactory;
    private readonly object _processBindingGate = new();
    private ProcessBinding? _processBinding;
    private long _nextProcessGeneration;
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
            await DisposeManagedProcessAsync(existingBinding, ct).ConfigureAwait(false);

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
    public async Task ForceKillAndClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var binding = Volatile.Read(ref _processBinding);
        if (binding is null)
            return;
        var process = binding.Source;

        try
        {
            // Once termination is requested, the per-instance manager gate must remain held
            // until the old operating-system process tree and lifecycle publications drain.
            await process.KillAndDrainAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[Instance] Process-tree halt failed for '{InstanceId}'", Config.Uuid);
            throw;
        }

        ResetProcess(binding);
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return Process?.GetLogHistory() ?? [];
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
        Task managerPublication;
        lock (_processBindingGate)
        {
            if (!IsCurrentBinding(binding))
                return;

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

    private async Task DisposeManagedProcessAsync(ProcessBinding binding, CancellationToken ct)
    {
        var process = binding.Source;
        try
        {
            // Starting a new generation is forbidden until the previous process tree, output,
            // and lifecycle publication tail have fully drained.
            await process.KillAndDrainAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "[Instance] Failed to drain stale process before restart for '{InstanceId}'",
                Config.Uuid);
            throw;
        }

        ResetProcess(binding);
    }

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

        await managerPublication.ConfigureAwait(false);
        if (!IsCurrentBinding(binding))
            return;

        await InvokeAsync(OnStatusChanged, Config.Uuid, status, cancellationToken).ConfigureAwait(false);
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
