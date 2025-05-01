using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;

public class InstallProfileJsonSettings
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = new List<JsonConverter>
        {
            new Artifact.ArtifactConverter()
        }
    };
}