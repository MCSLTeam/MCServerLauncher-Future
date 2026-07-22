using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Common.Network;
using MCServerLauncher.Common.ProtoType.Instance;
using Serilog;

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

    private async Task<Player[]> GetServerPlayersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Config.InstanceType.RequiresNumericMinecraftVersion() || string.IsNullOrWhiteSpace(Config.McVersion))
            return [];

        if (McVersion.Of(Config.McVersion) >= McVersion.Of("1.7") && Status == InstanceStatus.Running)
            try
            {
                ct.ThrowIfCancellationRequested();
                var status = await SlpClient.GetStatusModern("127.0.0.1", Port);
                if (status != null)
                    return status.Payload.Players.Sample.Select(player => new Player(player.Name, player.Id)).ToArray();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Debug(e, "[MinecraftInstance] Failed to query server player list for instance {InstanceId}", Config.Uuid);
                return [];
            }

        return [];
    }

    public override async Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new InstanceReport(
            Status,
            Config,
            Properties,
            await GetServerPlayersAsync(ct),
            Process is null ? default : await Process.Monitor.GetMonitorData(),
            ReadyTimedOut);
    }

    public override async Task<bool> StopAsync(CancellationToken ct = default)
    {
        var process = Process;
        if (process is null)
            return false;

        // Return after Stopping succeeds; terminal Stopped comes from process finalizer.
        if (!await process.RequestStoppingAsync(ct).ConfigureAwait(false))
            return false;

        try
        {
            process.WriteLine("stop");
        }
        catch (Exception exception) when (
            (exception is InvalidOperationException or IOException or ObjectDisposedException) &&
            (process.HasExit || process.Status is InstanceStatus.Stopped or InstanceStatus.Crashed))
        {
            // The process may exit after Stopping is committed but before stdin is written.
            // Stopping is the public success boundary, so that race remains a successful stop.
        }
        return true;
    }
}
