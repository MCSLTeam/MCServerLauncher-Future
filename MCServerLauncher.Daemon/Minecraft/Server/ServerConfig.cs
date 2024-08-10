using System.Text;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerConfig
{
    public string Name;
    public string Target;
    public TargetType TargetType;
    public string JavaPath;
    public string[] JavaArgs;
    public Encoding OutputEncoding;
    public Encoding InputEncoding;
    public ServerType ServerType;

    public string GetLaunchArguments()
    {
        return TargetType switch
        {
            TargetType.Jar => $"java {string.Join(" ", JavaArgs)} -jar {Target} nogui",
            TargetType.Script => $"{string.Join(" ", JavaArgs)}"
        };
    }
}