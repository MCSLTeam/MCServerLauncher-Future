using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Downloader;
using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;
using MCServerLauncher.Daemon.Storage;
using Serilog;
using Version = MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json.Version;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge;

public class ForgeInstaller
{
    protected readonly InstallV1 Profile;
    protected readonly Version Version;
    protected readonly string InstallerPath;
    protected readonly string JavaPath;

    protected ForgeInstaller(InstallV1 profile, string installerPath, string? javaPath = null)
    {
        Profile = profile;
        InstallerPath = installerPath;
        Version = profile.LoadVersion();
        JavaPath = javaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME") ??
            Environment.GetEnvironmentVariable("JRE_HOME") ?? "java";
    }

    public async Task<bool> run(string workingDirectory, CancellationToken ct = default)
    {
        var librariesDir = Path.Combine(workingDirectory, "libraries");
        Directory.CreateDirectory(librariesDir);
        ct.ThrowIfCancellationRequested();

        var contained = Profile.Path;

        // STAGE: 解压main jar
        if (contained is not null)
        {
            var targetPath = Path.Combine(workingDirectory, contained.Filename);
            if (await ExtractMavenFromInstaller(contained, targetPath, ct))
            {
                Log.Debug("[ForgeInstaller] Extracted main jar from installer to {0}", targetPath);
            }
            else
            {
                Log.Warning("[ForgeInstaller] Failed to extract main jar from installer");
                return false;
            }
        }

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载原版服务器核心到指定地址
        var serverJarPath = Profile.ServerJarPath
            .Replace("{ROOT}", Path.GetFullPath(workingDirectory))
            .Replace("{MINECRAFT_VERSION}", Profile.Minecraft)
            .Replace("{LIBRARY_DIR}", Path.GetFullPath(librariesDir));
        if (!await DownloadVanillaServer(await Profile.GetMcDownloadFromBmclApi(ct), serverJarPath, ct))
        {
            if (!await DownloadVanillaServer(await Profile.GetMcDownload(ct), serverJarPath, ct))
            {
                Log.Error("[ForgeInstaller] Failed to download vanilla server core");
                return false;
            }
        }

        ct.ThrowIfCancellationRequested();

        // STAGE: 下载库文件
        var libraries = Version.Libraries.Union(Profile.Libraries).ToList();
        var librariesLeft = await ConsiderLibraries(libraries, librariesDir, ct);
        librariesLeft = await DownloadLibraries(librariesLeft, librariesDir, ct);
        ct.ThrowIfCancellationRequested();

        // STAGE: 运行forge installer的offline模式来应用postprocessors
        var processStartInfo = new ProcessStartInfo
        {
            FileName = JavaPath,
            Arguments = $"-jar {InstallerPath} --offline --installServer",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(processStartInfo)!;
        process.OutputDataReceived += (sender, args) =>
        {
            Log.Debug("[ForgeInstaller] Offline installer stdout: {0}", args.Data);
        };
        process.ErrorDataReceived += (sender, args) =>
        {
            Log.Debug("[ForgeInstaller] Offline installer stderr: {0}", args.Data);
        };
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            Log.Error("[ForgeInstaller] Offline installer exited with code {0}", process.ExitCode);
            return false;
        }

        return true;
    }

    private static async Task<bool> DownloadVanillaServer(Version.Download? dl, string targetPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(dl?.Url)) return false;
        return await Download(dl.Url, targetPath, dl.Sha1, ct);
    }

    private async Task<List<Version.Library>> ConsiderLibraries(List<Version.Library> libraries, string root,
        CancellationToken ct = default)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };
        ConcurrentBag<Version.Library> result = new();
        await Parallel.ForEachAsync(libraries, parallelOptions, async (library, innerCt) =>
        {
            if (!await ConsiderLibrary(library, root, innerCt))
            {
                result.Add(library);
            }
        });
        return result.ToList();
    }

    private static async Task<List<Version.Library>> DownloadLibraries(List<Version.Library> libraries,
        string librariesDir,
        CancellationToken ct = default)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4
        };
        ConcurrentBag<Version.Library> result = new();
        await Parallel.ForEachAsync(libraries, parallelOptions, async (library, innerCt) =>
        {
            if (!await DownloadLibrary(library, librariesDir, innerCt))
            {
                result.Add(library);
            }
        });
        return result.ToList();
    }

    private async Task<bool> ConsiderLibrary(Version.Library library, string root, CancellationToken ct = default)
    {
        var arti = library.Name;
        var target = arti.GetLocalPath(root);
        var download = library.Downloads?.Artifact ?? new Version.LibraryDownload { Path = arti.Path };

        // 检查本地是否已经存在正确的文件
        if (download.Sha1 is not null)
        {
            var fileSha1 = await FileManager.FileSha1(target, ct);
            if (fileSha1 == download.Sha1) return true;
            File.Delete(target);
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // 首先检查forge installer jar文件中是否携带
        if (await ExtractMavenFromInstaller(arti, target, ct))
        {
            if (download.Sha1 is not null)
            {
                var fileSha1 = await FileManager.FileSha1(target, ct);
                if (fileSha1 == download.Sha1) return true;
                File.Delete(target);
            }
        }
        else File.Delete(target);

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

    private static async Task<bool> Download(string url, string target, string? sha1 = null,
        CancellationToken ct = default)
    {
        // 下载
        var dl = new DownloadBuilder()
            .WithUrl(url)
            .WithFileLocation(target)
            .WithConfiguration(new DownloadConfiguration
            {
                ParallelDownload = true,
                ParallelCount = 4,
                MaxTryAgainOnFailover = 3
            }).Build();
        await dl.StartAsync(ct);
        if (dl.Status == DownloadStatus.Failed)
        {
            File.Delete(target);
            return false;
        }

        // 检查sha1
        if (sha1 is not null)
        {
            var fileSha1 = await FileManager.FileSha1(target, ct);
            if (fileSha1 != sha1)
            {
                File.Delete(target);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ExtractMavenFromInstaller(Artifact artifact, string targetPath,
        CancellationToken ct = default)
    {
        using var jar = ZipFile.OpenRead(InstallerPath);
        var entry = jar.GetEntry(Path.Combine("maven", artifact.Path));
        if (entry is null) return false;

        await using var stream = entry.Open();
        await using var file = File.Create(targetPath);
        await stream.CopyToAsync(file, 8192, ct);
        return true;
    }
}