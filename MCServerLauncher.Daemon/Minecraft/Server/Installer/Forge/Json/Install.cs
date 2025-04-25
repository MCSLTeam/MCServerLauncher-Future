using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class Install
{
    [JsonProperty("filePath")] public string? FilePath { get; set; }
    public int Spec { get; set; }
    public string Minecraft { get; set; }
    public string Json { get; set; }
    public Artifact? Path { get; set; }
    public Version.Library[] Libraries { get; set; } = Array.Empty<Version.Library>();
}