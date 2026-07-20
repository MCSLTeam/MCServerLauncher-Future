using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using Serilog;
using TouchSocket.Core;
using DisposableObject = MCServerLauncher.Daemon.Utils.DisposableObject;

namespace MCServerLauncher.Daemon.Management;

public abstract class InstanceBase : DisposableObject, IInstance
{
    protected InstanceConfig ProtectedConfig;

    protected InstanceBase(InstanceConfig config)
    {
        ProtectedConfig = config;
    }

    public InstanceConfig Config => ProtectedConfig;

    public InstanceProcess? Process { get; private set; }
    public InstanceStatus Status => Process?.Status ?? InstanceStatus.Stopped;
    public int ServerProcessId => Process?.ServerProcessId ?? -1;

    public event Func<Guid, string, CancellationToken, Task>? OnLog;
    public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

    public virtual async Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new InstanceReport(
            Status,
            Config,
            new Dictionary<string, string>(),
            [],
            Process is null ? default : await Process.Monitor.GetMonitorData());
    }

    public async Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Always clear any previous InstanceProcess before a new start. Unix PTY children are
        // attached via Process.GetProcessById; after Kill/halt HasExited may stay false, which
        // previously made the next StartAsync return false permanently until daemon restart.
        if (Process is not null)
            await DisposeManagedProcessAsync(Process, ct).ConfigureAwait(false);

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
        var process = new InstanceProcess(
            startInfo,
            Config.CanSafeCastTo<MinecraftInstance>(),
            Config.ConsoleMode);
        Process = process;
        process.OnStatusChanged += OnProcessStatusChangedAsync;
        process.OnLog += OnProcessLogAsync;

        try
        {
            ct.ThrowIfCancellationRequested();
            var started = await process.StartAsync(delayToCheck, ct);
            if (!started)
            {
                Log.Error(
                    "[Instance] Process exited immediately after start for '{InstanceId}' (console_mode={ConsoleMode})",
                    Config.Uuid,
                    Config.ConsoleMode);
                ResetProcess(process);
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
            ResetProcess(process);
            throw;
        }
    }

    public virtual void Stop()
    {
        // Default stop is hard-kill + clear handle. MinecraftInstance overrides to send "stop".
        ForceKillAndClear();
    }

    /// <summary>
    /// Immediately kills the managed process and drops <see cref="Process"/> so a later
    /// <see cref="StartAsync"/> cannot be blocked by a stale HasExited=false handle.
    /// </summary>
    public void ForceKillAndClear()
    {
        var process = Process;
        if (process is null)
            return;

        try
        {
            process.KillProcess();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[Instance] KillProcess failed for '{InstanceId}'", Config.Uuid);
        }

        ResetProcess(process);
    }

    public IReadOnlyList<string> GetLogHistory()
    {
        return Process?.GetLogHistory() ?? [];
    }

    protected override void ProtectedDispose()
    {
        Process?.SafeDispose();
        Process = null;
    }

    private Task OnProcessLogAsync(string message, CancellationToken cancellationToken)
    {
        return InvokeAsync(OnLog, Config.Uuid, message, cancellationToken);
    }

    private async Task DisposeManagedProcessAsync(InstanceProcess process, CancellationToken ct)
    {
        try
        {
            if (!process.HasExit)
                process.KillProcess();
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "[Instance] Failed to force-stop stale process before restart for '{InstanceId}'",
                Config.Uuid);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "[Instance] WaitForExit before restart failed for '{InstanceId}'",
                Config.Uuid);
        }

        ResetProcess(process);
    }

    private void ResetProcess(InstanceProcess process)
    {
        process.OnStatusChanged -= OnProcessStatusChangedAsync;
        process.OnLog -= OnProcessLogAsync;
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

        if (ReferenceEquals(Process, process))
            Process = null;
    }

    private Task OnProcessStatusChangedAsync(InstanceStatus status, CancellationToken cancellationToken)
    {
        return InvokeAsync(OnStatusChanged, Config.Uuid, status, cancellationToken);
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
}
