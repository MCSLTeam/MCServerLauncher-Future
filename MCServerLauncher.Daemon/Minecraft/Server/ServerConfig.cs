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

    public string GetLaunchScript()
    {
        return TargetType switch
        {
            TargetType.Jar => $"{string.Join(" ", JavaArgs)} -jar {Target}",
            TargetType.Script => $"{string.Join(" ", JavaArgs)}"
        };
    }
}