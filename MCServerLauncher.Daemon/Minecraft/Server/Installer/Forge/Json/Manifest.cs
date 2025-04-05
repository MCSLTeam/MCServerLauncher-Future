using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class Manifest
{
    [JsonProperty("versions")] private List<Info>? _versions;

    public string GetUrl(string version)
    {
        // return versions == null ? null : versions.stream().filter(v -> version.equals(v.getId())).map(Info::getUrl).findFirst().orElse(null);
        return _versions?.FirstOrDefault(v => v.Id == version)?.Url ?? string.Empty;
    }

    public record Info(string Id, string Url);
}