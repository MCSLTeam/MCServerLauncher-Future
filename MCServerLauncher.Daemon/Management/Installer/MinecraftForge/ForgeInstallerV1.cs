using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V1Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using System.Text.Json;
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
            if (await FileMatchesAnySha1(target, info.Checksums, ct)) return true;
            File.Delete(target);
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // 检查forge installer jar文件中是否携带
        if (await ExtractMavenFromInstaller(arti, target, ct))
        {
            if (await FileMatchesAnySha1(target, info.Checksums, ct)) return true;
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

        if (await TryCopyLibraryFromCache(arti, target, info.Checksums, ct)) return true;

        // 检查url
        var url = info.Url;
        if (string.IsNullOrEmpty(url))
        {
            Log.Debug("[ForgeInstallerV1] Library {0} has no url, set default to {url}", arti, CF_LIBRARIES_URL);
            info.Url = CF_LIBRARIES_URL;
        }

        url = arti.GetLocalPath(info.Url!.TrimEnd('/')).Replace("\\", "/");

        // 如果是minecraft的maven
        if (url.StartsWith(CF_LIBRARIES_URL))
            return await DownloadLibraryWithCache(arti, url, target, info.Checksums, ct);

        // 如果是forge的maven，按镜像设置优先使用 BMCLAPI，失败则回退原 URL。
        var bmclApiUrl = url.Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven");
        var success = MirrorType switch
        {
            InstanceFactoryMirror.BmclApi => await DownloadLibraryWithCache(arti, bmclApiUrl, target, info.Checksums, ct),
            InstanceFactoryMirror.None => false,
            _ => throw new NotImplementedException()
        };
        if (success) return true;
        return await DownloadLibraryWithCache(arti, url, target, info.Checksums, ct);
    }

    [RequiresUnreferencedCode(ForgeInstallerTrimMessage)]
    public static ForgeInstallerV1? Create(
        string installerPath,
        string? javaPath = null,
        InstanceFactoryMirror mirror = InstanceFactoryMirror.None
    )

    {
        var rv = ReadInstallerProfile(
            installerPath,
            DeserializeInstallerProfile
        );
        if (rv is null)
        {
            Log.Error("[ForgeInstaller] Failed to get installer profile from {0}", installerPath);
            return null;
        }

        return new ForgeInstallerV1(rv, installerPath, javaPath, mirror);
    }

    [RequiresUnreferencedCode(ForgeInstallerTrimMessage)]
    private static ProfileFile DeserializeInstallerProfile(string content) =>
        JsonSerializer.Deserialize<ProfileFile>(content, InstallProfileJsonSettings.Settings)!;

    public record ProfileFile(InstallV1 Install, VersionInfo VersionInfo);
}
