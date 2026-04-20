using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;

public class InstallProfileJsonSettings
{
    public static readonly JsonSerializerOptions Settings = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new Artifact.ArtifactStjConverter() }
    };
}