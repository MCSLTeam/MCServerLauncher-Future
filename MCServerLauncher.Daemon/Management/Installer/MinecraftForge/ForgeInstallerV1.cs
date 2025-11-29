using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V1Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge;

public class ForgeInstallerV1 : ForgeInstallerBase
{
    private const string CF_LIBRARIES_URL = "https://libraries.minecraft.net/";

    private ForgeInstallerV1(ProfileFile profile, string installerPath, string? javaPath, InstanceFactoryMirror mirror)
        : base(installerPath, javaPath, mirror)
    {
        Install = profile.Install;
        VersionInfo = profile.VersionInfo;
    }

    public VersionInfo VersionInfo { get; }
    public override InstallV1 Install { get; }

    public override async Task<Result<Unit, Error>> Run(InstanceFactorySetting setting, CancellationToken ct = default)
    {
        var workingDirectory = setting.GetWorkingDirectory();
        var librariesDir = Path.Combine(workingDirectory, "libraries");
        Directory.CreateDirectory(librariesDir);
        ct.ThrowIfCancellationRequested();


        // STAGE: 解压main jar
        await ExtractContained(Install.FilePath!, Path.Combine(workingDirectory, Install.FilePath!), ct);

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载原版服务器核心到指定地址
        Log.Debug("[ForgeInstaller] Downloading vanilla server core");
        var serverJarPath = Path.Combine(workingDirectory, $"minecraft_server.{Install.Minecraft}.jar");
        if (!await DownloadMinecraft(serverJarPath, ct))
        {
            Log.Error("[ForgeInstaller] Failed to download vanilla server core");
            return ResultExt.Err("Failed to download vanilla server core");
        }

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载库文件
        var librariesLeft = VersionInfo.Libraries.Where(lib => lib.Required).ToList();

        Log.Debug("[ForgeInstaller] Considering {0} libraries", librariesLeft.Count);
        librariesLeft = await ParallelProcessLibraries(librariesLeft, librariesDir, ConsiderLibrary, ct);

        Log.Debug("[ForgeInstaller] Downloading {0} libraries", librariesLeft.Count);
        librariesLeft = await ParallelProcessLibraries(librariesLeft, librariesDir, DownloadLibrary, ct);
        foreach (var library in librariesLeft) Log.Warning("[ForgeInstaller] Library {0} not downloaded", library.Name);

        ct.ThrowIfCancellationRequested();

        // STAGE: 运行forge installer的offline模式来应用postprocessors
        if (await RunInstallerOffline(workingDirectory, ct)) return ResultExt.Ok();

        DeleteInstaller();
        return ResultExt.Err("Failed to apply forge post-processors");
    }

    private async Task<bool> ConsiderLibrary(LibraryInfo info, string libRoot, CancellationToken ct = default)
    {
        var arti = info.Name;
        var target = arti.GetLocalPath(libRoot);

        // 检查本地是否已经存在正确的文件
        if (File.Exists(target))
        {
            var fileSha1 = await FileManager.FileSha1(target, ct);
            if (info.Checksums.Any(sha1 => sha1 == fileSha1)) return true;
            File.Delete(target);
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // 检查forge installer jar文件中是否携带
        if (await ExtractMavenFromInstaller(arti, target, ct))
        {
            var fileSha1 = await FileManager.FileSha1(target, ct);
            if (info.Checksums.Any(sha1 => sha1 == fileSha1)) return true;
            File.Delete(target);
        }
        else
        {
            File.Delete(target);
        }

        return false;
    }

    private async Task<bool> DownloadLibrary(
        LibraryInfo info,
        string root,
        CancellationToken ct = default
    )
    {
        var arti = info.Name;
        var target = arti.GetLocalPath(root);

        // TODO library本地缓存库,首先从本地缓存库查找,找不到或者sha1对不上再从镜像下载

        // 检查url
        var url = info.Url;
        if (string.IsNullOrEmpty(url))
        {
            Log.Debug("[ForgeInstallerV1] Library {0} has no url, set default to {url}", arti, CF_LIBRARIES_URL);
            info.Url = CF_LIBRARIES_URL;
        }

        url = arti.GetLocalPath(info.Url!.TrimEnd('/')).Replace("\\", "/");

        // 如果是minecraft的maven
        if (url.StartsWith(CF_LIBRARIES_URL)) return await Download(url, target, info.Checksums, ct);

        // 如果是forge的maven，首先使用bmclapi进行加速,如果失败则使用原url
        var bmclApiUrl = url.Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven");
        if (!await Download(bmclApiUrl, target, info.Checksums, ct))
            if (!await Download(url, target, info.Checksums, ct))
                return false;
        var success = MirrorType switch
        {
            InstanceFactoryMirror.BmclApi => await Download(bmclApiUrl, target, info.Checksums, ct),
            InstanceFactoryMirror.None => false,
            _ => throw new NotImplementedException()
        };
        if (success) return true;
        return await Download(url, target, info.Checksums, ct);
    }

    public static ForgeInstallerV1? Create(
        string installerPath,
        string? javaPath = null,
        InstanceFactoryMirror mirror = InstanceFactoryMirror.None
    )

    {
        var rv = ReadInstallerProfile(
            installerPath,
            content => JsonConvert.DeserializeObject<ProfileFile>(content, InstallProfileJsonSettings.Settings)!
        );
        if (rv is null)
        {
            Log.Error("[ForgeInstaller] Failed to get installer profile from {0}", installerPath);
            return null;
        }

        return new ForgeInstallerV1(rv, installerPath, javaPath, mirror);
    }

    public record ProfileFile(InstallV1 Install, VersionInfo VersionInfo);
}