using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

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