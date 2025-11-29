using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Serilog;
using TouchSocket.Core;
using DisposableObject = MCServerLauncher.Daemon.Utils.DisposableObject;

namespace MCServerLauncher.Daemon.Management;

public abstract class InstanceBase : DisposableObject, IInstance
{
    private readonly Action _configReloader;
    protected InstanceConfig ProtectedConfig;

    protected InstanceBase(InstanceConfig config)
    {
        ProtectedConfig = config;

        var workingDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(workingDirectory, InstanceConfig.FileName);
        var configChange = new FileChange(configPath);
        _configReloader = () =>
        {
            if (!configChange.HasChanged()) return;

            try
            {
                var newConfig = FileManager.ReadJson<InstanceConfig>(configPath)!;
                if (Config.Uuid != newConfig.Uuid)
                {
                    Log.Debug("[Instance] Uuid changed, ignored");
                    return;
                }

                ProtectedConfig = newConfig;
                ConfigReloaded?.Invoke();
            }
            catch (Exception e)
            {
                Log.Debug("[Instance] Failed to refresh config at '{0}', ignored: {1}", configPath, e.Message);
            }
        };
    }

    public InstanceConfig Config
    {
        get
        {
            RequestReloadConfig();
            return ProtectedConfig;
        }
    }

    public InstanceProcess? Process { get; private set; }
    public InstanceStatus Status => Process?.Status ?? InstanceStatus.Stopped;
    public int ServerProcessId => Process?.ServerProcessId ?? -1;

    public event Action<Guid, string>? OnLog;
    public event Action<Guid, InstanceStatus>? OnStatusChanged;

    public virtual async Task<InstanceReport> GetReportAsync()
    {
        return new InstanceReport(
            Status,
            Config,
            new Dictionary<string, string>(),
            Array.Empty<Player>(),
            Process is null ? default : await Process!.Monitor.GetMonitorData()
        );
    }

    public async Task<bool> StartAsync(int delayToCheck = 500)
    {
        if (Process is not null)
        {
            if (!Process.HasExit) return false;
            Process.Close();
            Process.Dispose();
        }

        RequestReloadConfig();

        Process = new InstanceProcess(Config.GetStartInfo(), Config.CanCastTo<MinecraftInstance>());
        Process.OnStatusChanged += st => { OnStatusChanged?.Invoke(Config.Uuid, st); };
        Process.OnLog += message => OnLog?.Invoke(Config.Uuid, message);

        return await Process.StartAsync();
    }

    public virtual void Stop()
    {
        Process?.KillProcess();
    }

    protected event Action? ConfigReloaded;

    /// <summary>
    ///     请求重载配置, 这并不会每次都重载, 而是会检查文件是否被修改, 在决定是否重载
    /// </summary>
    private void RequestReloadConfig()
    {
        if (Status is InstanceStatus.Stopped or InstanceStatus.Crashed)
            _configReloader.Invoke();
    }

    protected override void ProtectedDispose()
    {
        Process?.SafeDispose();
        Process = null;
    }
}