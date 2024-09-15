namespace MCServerLauncher.Daemon.Minecraft.Server;

public enum ServerStatus
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Crashed,
}

public class InstanceStatus
{
    public ServerStatus Status { get; set; }
    public ServerConfig Config { get; private set; }
    public List<string> Properties { get; private set; }
    public List<string> Players { get; private set; } = new();
    
    public  InstanceStatus(ServerStatus status, ServerConfig config, List<string> properties, IEnumerable<string> players)
    {
        Status = status;
        Config = config;
        Properties = properties;
        Players.AddRange(players);
    }
}