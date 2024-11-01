namespace MCServerLauncher.Daemon.Minecraft.Server;

public enum ServerStatus
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Crashed
}

// TODO:更好的序列化
public class InstanceStatus
{
    public InstanceStatus(ServerStatus status, InstanceConfig config, List<string> properties,
        IEnumerable<string> players)
    {
        Status = status;
        Config = config;
        Properties = properties;
        Players.AddRange(players);
    }

    public ServerStatus Status { get; set; }
    public InstanceConfig Config { get; private set; }
    public List<string> Properties { get; private set; }
    public List<string> Players { get; } = new();
}

public static class InstanceStatusExtensions
{
    public static bool IsStoppedOrCrashed(this ServerStatus status)
    {
        return status is ServerStatus.Stopped or ServerStatus.Crashed;
    }
}