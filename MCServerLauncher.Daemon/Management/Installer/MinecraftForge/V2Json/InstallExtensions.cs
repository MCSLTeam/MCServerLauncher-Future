using System.IO.Compression;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using Newtonsoft.Json;
using Version = MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json.Version;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;

public static class InstallExtensions
{
    public static Version LoadVersion(this Install profile, string installerPath)
    {
        using var installer = new ZipArchive(File.OpenRead(installerPath));
        using var s = installer.GetEntry(profile.Json.TrimStart('/'))!.Open();
        using var sr = new StreamReader(s);

        return JsonConvert.DeserializeObject<Version>(sr.ReadToEnd(), InstallProfileJsonSettings.Settings)!;
    }

    public static async Task<Version.Download?> GetMcDownloadFromBmclApi(this Install profile,
        CancellationToken ct = default)
    {
        var manifestUrl = $"https://bmclapi2.bangbang93.com/version/{profile.Minecraft}/json";
        using var client = new HttpClient();
        var version = await client.GetStringAsync(manifestUrl, ct);
        var versionObj = JsonConvert.DeserializeObject<Version>(version, InstallProfileJsonSettings.Settings);
        var download = versionObj?.GetDownload("server");

        if (download is not null) download.Url = $"https://bmclapi2.bangbang93.com/version/{profile.Minecraft}/server";
        return download;
    }

    public static async Task<Version.Download?> GetMcDownload(this Install profile, CancellationToken ct = default)
    {
        using var client = new HttpClient();
        var manifest = await client.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest.json", ct);
        var manifestObj = JsonConvert.DeserializeObject<Manifest>(manifest, InstallProfileJsonSettings.Settings);
        if (manifestObj is null) return null;

        var version = await client.GetStringAsync(manifestObj.GetUrl(profile.Minecraft), ct);
        var versionObj = JsonConvert.DeserializeObject<Version>(version, InstallProfileJsonSettings.Settings);
        return versionObj?.GetDownload("server");
    }
}