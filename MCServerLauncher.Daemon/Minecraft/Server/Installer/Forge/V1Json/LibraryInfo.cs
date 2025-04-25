using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.V1Json;

public class LibraryInfo
{
    public Artifact Name { get; set; }
    public string? Url { get; set; }
    public string[] Checksums { get; set; } = Array.Empty<string>();
    [JsonProperty("serverreq")] public bool Required { get; set; } = true;
}