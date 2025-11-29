using System.Net.Sockets;
using MCServerLauncher.Common.Network;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Minecraft;

public class MinecraftInstance : InstanceBase
{
    private readonly PropertiesHandler _properties;

    public MinecraftInstance(InstanceConfig config) : base(config)
    {
        var workingDirectory = config.GetWorkingDirectory();
        var propertiesPath = Path.Combine(workingDirectory, PropertiesHandler.FileName);

        _properties = new PropertiesHandler(propertiesPath);
        _properties.OnPropertiesUpdated += properties =>
        {
            Port = ushort.TryParse(properties.GetValueOrDefault("server-port", string.Empty), out var port)
                ? port
                : -1;
        };
        _properties.Load();
    }

    public Dictionary<string, string> Properties =>
        _properties.GetProperties(Status is InstanceStatus.Stopped or InstanceStatus.Crashed);

    public int Port { get; private set; } = -1;

    private async Task<Player[]> GetServerPlayersAsync()
    {
        if (McVersion.Of(Config.McVersion) >= McVersion.Of("1.7") && Status == InstanceStatus.Running)
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

    public override async Task<InstanceReport> GetReportAsync()
    {
        return new InstanceReport(
            Status,
            Config,
            Properties,
            await GetServerPlayersAsync(),
            Process is null ? default : await Process!.Monitor.GetMonitorData()
        );
    }

    public override void Stop()
    {
        Process?.WriteLine("stop");
    }
}