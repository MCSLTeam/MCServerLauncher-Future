using System.Text;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerConfig
{
    public Encoding InputEncoding;
    public string[] JavaArgs;
    public string JavaPath;
    public string Name;
    public Encoding OutputEncoding;
    public ServerType ServerType;
    public string Target;
    public TargetType TargetType;

    public string GetLaunchArguments()
    {
        return TargetType switch
        {
            TargetType.Jar => $"java {string.Join(" ", JavaArgs)} -jar {Target} nogui",
            TargetType.Script => $"{string.Join(" ", JavaArgs)}"
        };
    }
}