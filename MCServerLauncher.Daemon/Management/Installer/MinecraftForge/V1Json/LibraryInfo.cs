using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V1Json;

public class LibraryInfo
{
    public Artifact Name { get; set; }
    public string? Url { get; set; }
    public string[] Checksums { get; set; } = Array.Empty<string>();
    [JsonProperty("serverreq")] public bool Required { get; set; } = true;
}