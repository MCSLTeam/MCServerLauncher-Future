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

    public event Action<Guid, string>? OnLog;
    public event Action<Guid, InstanceStatus>? OnStatusChanged;

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
        if (Process is not null)
        {
            if (!Process.HasExit)
                return false;

            Process.Close();
            Process.Dispose();
        }

        var startInfoResult = Config.TryGetStartInfo();
        if (startInfoResult.IsErr(out var error))
        {
            Log.Error("[Instance] Failed to build start info for instance '{0}': {1}", Config.Uuid, error);
            return false;
        }

        var startInfo = startInfoResult.Unwrap();
        Process = new InstanceProcess(startInfo, Config.CanSafeCastTo<MinecraftInstance>());
        Process.OnStatusChanged += status => OnStatusChanged?.Invoke(Config.Uuid, status);
        Process.OnLog += message => OnLog?.Invoke(Config.Uuid, message);

        ct.ThrowIfCancellationRequested();
        return await Process.StartAsync();
    }

    public virtual void Stop()
    {
        Process?.KillProcess();
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
}
