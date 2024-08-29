using System.Text;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class ServerConfig
{
    public Encoding InputEncoding { get; set; } = Encoding.UTF8;
    public string WorkingDirectory { get; set; }
    public string[] JavaArgs { get; set; }
    public string JavaPath { get; set; }
    public string Name { get; set; }
    public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
    public ServerType ServerType { get; set; }
    public string Target { get; set; }
    public TargetType TargetType { get; set; }

    public (string, string) GetLaunchScript()
    {
        return TargetType switch
        {
            TargetType.Jar => ("java", $"{string.Join(" ", JavaArgs)} -jar {Target} nogui"),
            TargetType.Script => (Path.Combine(Directory.GetCurrentDirectory(), WorkingDirectory, Target), "")
        };
    }
}