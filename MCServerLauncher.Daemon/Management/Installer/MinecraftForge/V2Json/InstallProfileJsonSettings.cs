using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;

public static class InstallProfileJsonSettings
{
    public static readonly JsonSerializerOptions Settings = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new Artifact.ArtifactConverter() }
    };
}
