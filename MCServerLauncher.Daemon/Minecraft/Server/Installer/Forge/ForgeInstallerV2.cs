using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;
using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.V2Json;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
using Serilog;
using Version = MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json.Version;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge;

public sealed class ForgeInstallerV2 : ForgeInstallerBase
{
    private ForgeInstallerV2(InstallV1 profile, string installerPath, string? javaPath, InstanceFactoryMirror mirror)
        : base(installerPath, javaPath, mirror)
    {
        Install = profile;
        Version = profile.LoadVersion(installerPath);
    }

    public Version Version { get; }

    public override InstallV1 Install { get; }

    public static ForgeInstallerV2? Create(
        string installerPath,
        string? javaPath = null,
        InstanceFactoryMirror mirror = InstanceFactoryMirror.None
    )
    {
        var profile = ReadInstallerProfile(
            installerPath,
            content => JsonConvert.DeserializeObject<InstallV1>(content, InstallProfileJsonSettings.Settings)!
        );
        if (profile is null)
        {
            Log.Error("[ForgeInstaller] Failed to get installer profile from {0}", installerPath);
            return null;
        }

        return new ForgeInstallerV2(profile, installerPath, javaPath, mirror);
    }

    public override async Task<bool> Run(string workingDirectory, CancellationToken ct = default)
    {
        var libRoot = Path.Combine(workingDirectory, "libraries");
        Directory.CreateDirectory(libRoot);
        ct.ThrowIfCancellationRequested();

        // STAGE: 解压main jar
        await ExtractProfileContained(workingDirectory, ct);

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载原版服务器核心到指定地址
        Log.Debug("[ForgeInstaller] Downloading vanilla server core");
        var serverJarPath = Install.ServerJarPath
            .Replace("{ROOT}", Path.GetFullPath(workingDirectory))
            .Replace("{MINECRAFT_VERSION}", Install.Minecraft)
            .Replace("{LIBRARY_DIR}", Path.GetFullPath(libRoot));
        if (!await DownloadMinecraft(serverJarPath, ct))
        {
            Log.Error("[ForgeInstaller] Failed to download vanilla server core");
            return false;
        }

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载库文件
        var librariesLeft = Version.Libraries.Union(Install.Libraries).DistinctBy(x => x.Name.Descriptor).ToList();

        Log.Debug("[ForgeInstaller] Considering {0} libraries", librariesLeft.Count);
        librariesLeft = await ParallelProcessLibraries(librariesLeft, libRoot, ConsiderLibrary, ct);

        Log.Debug("[ForgeInstaller] Downloading {0} libraries", librariesLeft.Count);
        librariesLeft = await ParallelProcessLibraries(librariesLeft, libRoot, DownloadLibrary, ct);
        foreach (var library in librariesLeft) Log.Warning("[ForgeInstaller] Library {0} not downloaded", library.Name);

        ct.ThrowIfCancellationRequested();

        // STAGE: 运行forge installer的offline模式来应用postprocessors
        var rv = await RunInstallerOffline(workingDirectory, ct);
        if (rv) DeleteInstaller();
        return rv;
    }


    private async Task<bool> ConsiderLibrary(Version.Library library, string libRoot, CancellationToken ct = default)
    {
        var arti = library.Name;
        var target = arti.GetLocalPath(libRoot);
        var download = library.Downloads?.Artifact ?? new Version.LibraryDownload { Path = arti.Path };

        // 检查本地是否已经存在正确的文件
        if (string.IsNullOrEmpty(download.Sha1) && File.Exists(target))
        {
            var fileSha1 = await FileManager.FileSha1(target, ct);
            if (fileSha1 == download.Sha1) return true;
            File.Delete(target);
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // 检查forge installer jar文件中是否携带
        if (await ExtractMavenFromInstaller(arti, target, ct))
        {
            if (string.IsNullOrEmpty(download.Sha1))
            {
                var fileSha1 = await FileManager.FileSha1(target, ct);
                if (fileSha1 == download.Sha1) return true;
                File.Delete(target);
            }
        }
        else
        {
            File.Delete(target);
        }

        return false;
    }

    private static async Task<bool> DownloadLibrary(Version.Library library, string root,
        CancellationToken ct = default)
    {
        var arti = library.Name;
        var target = arti.GetLocalPath(root);
        var download = library.Downloads?.Artifact ?? new Version.LibraryDownload { Path = arti.Path };

        // TODO library本地缓存库,首先从本地缓存库查找,找不到或者sha1对不上再从镜像下载

        // 检查url
        var url = download.Url;
        if (string.IsNullOrEmpty(url))
        {
            Log.Error("[ForgeInstaller] Library {0} has no url", arti);
            return false;
        }

        // 首先使用bmclapi进行加速,如果失败则使用原url
        var bmclApiUrl = url.Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven");
        if (!await Download(bmclApiUrl, target, download.Sha1, ct))
            if (!await Download(url, target, download.Sha1, ct))
                return false;

        return true;
    }
}