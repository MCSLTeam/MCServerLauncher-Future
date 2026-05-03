using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;

public class Install
{
    [JsonProperty("filePath")] public string? FilePath { get; set; }
    public int Spec { get; set; }
    public required string Minecraft { get; set; }
    public required string Json { get; set; }
    public Artifact? Path { get; set; }
    public Version.Library[] Libraries { get; set; } = Array.Empty<Version.Library>();
}