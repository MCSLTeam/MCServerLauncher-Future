using System.Net.Sockets;
using MCServerLauncher.Common.Network;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Communicate;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Serilog;
using TouchSocket.Core;
using DisposableObject = MCServerLauncher.Daemon.Utils.DisposableObject;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class Instance : DisposableObject
{
    private readonly PropertiesHandler _properties;
    private InstanceConfig _config;
    private InstanceProcess? _process;

    public Instance(InstanceConfig config)
    {
        _config = config;
        _properties = new PropertiesHandler();

        var workingDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(workingDirectory, InstanceConfig.FileName);
        var propertiesPath = Path.Combine(workingDirectory, PropertiesHandler.FileName);
        var configChange = new FileChange(configPath);
        var propertiesChange = new FileChange(propertiesPath);

        _properties.Load(propertiesPath);
        OnReloadSettings += () =>
        {
            if (!configChange.HasChanged()) return;
            try
            {
                var newConfig = FileManager.ReadJson<InstanceConfig>(configPath)!;
                if (_config.Uuid != newConfig.Uuid)
                {
                    Log.Debug("[Instance] Uuid changed, ignored");
                    return;
                }

                _config = newConfig;
            }
            catch (Exception e)
            {
                Log.Debug("[Instance] Failed to refresh config at '{0}', ignored: {1}", configPath, e.Message);
            }
        };
        OnReloadSettings += () =>
        {
            if (!propertiesChange.HasChanged()) return;

            _properties.Load(propertiesPath);
            Port = ushort.TryParse(_properties.GetProperty("server-port"), out var port) ? port : -1;
        };
    }

    public InstanceConfig Config
    {
        get
        {
            OnReloadSettings?.Invoke();
            return _config;
        }
    }

    public int Port { get; private set; } = -1;
    public InstanceStatus Status => _process?.Status ?? InstanceStatus.Stopped;

    /// <summary>
    ///     进程id, 如果为-1，则表示进程不存在
    /// </summary>
    public int ServerProcessId => _process?.ServerProcessId ?? -1;

    private event Action? OnReloadSettings;

    public event Action<Guid, string>? OnLog;
    public event Action<Guid, InstanceStatus>? OnStatusChanged;

    private async Task<Player[]> GetServerPlayersAsync()
    {
        if (McVersion.Of(_config.McVersion) >= McVersion.Of("1.7") && Status == InstanceStatus.Running)
            try
            {
                var status = await SlpClient.GetStatusModern("127.0.0.1", Port);
                if (status != null)
                    return status.Payload.Players.Sample.Select(player => new Player(player.Name, player.Id)).ToArray();
            }
            catch (Exception e)when (e is SocketException or ArgumentOutOfRangeException)
            {
                return Array.Empty<Player>();
            }

        return Array.Empty<Player>();
    }

    // TODO 使用SlpClient获取服务器信息(例如玩家列表)
    public async Task<InstanceReport> GetReportAsync()
    {
        ReloadSettings();

        return new InstanceReport(
            Status,
            _config,
            _properties.ServerPropertiesList,
            await GetServerPlayersAsync()
        );
    }


    protected override void ProtectedDispose()
    {
        _process?.SafeDispose();
    }

    private void ReloadSettings()
    {
        if (Status is InstanceStatus.Stopped or InstanceStatus.Crashed)
            OnReloadSettings?.Invoke();
    }

    #region Process

    // TODO ,stderr的接收；Player List改为使用MC SLP协议
    public async Task<bool> StartAsync(int delayToCheck = 500)
    {
        if (_process is not null)
        {
            if (!_process.HasExit) return false;
            _process.Close();
            _process.Dispose();
        }

        _process = new InstanceProcess(_config.GetStartInfo());
        _process.OnStatusChanged += st =>
        {
            OnStatusChanged?.Invoke(_config.Uuid, st);
            if (st is InstanceStatus.Starting or InstanceStatus.Running) OnReloadSettings?.Invoke();
        };
        _process.OnLog += message => OnLog?.Invoke(_config.Uuid, message);

        return await _process.StartAsync();
    }

    public void KillProcess()
    {
        _process?.KillProcess();
    }

    public Task WaitForExitAsync(CancellationToken ct = default)
    {
        return _process?.WaitForExitAsync(ct) ?? Task.CompletedTask;
    }

    public void WriteLine(string? message)
    {
        _process?.WriteLine(message);
    }

    public Task<(long Memory, double Cpu)> GetMonitorData() =>
        _process?.GetMonitorData() ?? Task.FromResult((-1L, 0.0));

    #endregion
}