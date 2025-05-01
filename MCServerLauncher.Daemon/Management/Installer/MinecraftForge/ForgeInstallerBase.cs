using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Downloader;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
using Serilog;
using Version = MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json.Version;

namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge;

public abstract class ForgeInstallerBase : IInstanceInstaller
{
    protected ForgeInstallerBase(string installerPath, string? javaPath, InstanceFactoryMirror mirror)
    {
        InstallerPath = installerPath;
        JavaPath = javaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME") ??
            Environment.GetEnvironmentVariable("JRE_HOME") ?? "java";
        MirrorType = mirror;
    }

    protected string JavaPath { get; }
    protected string InstallerPath { get; }

    protected InstanceFactoryMirror MirrorType { get; }
    public abstract InstallV1 Install { get; }
    public abstract Task<bool> Run(InstanceFactorySetting setting, CancellationToken ct = default);

    protected static async Task<List<TLibrary>> ParallelProcessLibraries<TLibrary>(
        List<TLibrary> libraries,
        string libRoot,
        Func<TLibrary, string, CancellationToken, Task<bool>> processor,
        CancellationToken ct = default
    )
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };
        ConcurrentBag<TLibrary> result = new();
        await Parallel.ForEachAsync(libraries, parallelOptions, async (library, innerCt) =>
        {
            var cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(ct, innerCt).Token;
            if (!await processor(library, libRoot, cancellationToken)) result.Add(library);
        });
        return result.ToList();
    }

    protected async Task<bool> RunInstallerOffline(string workingDirectory, CancellationToken ct = default)
    {
        Log.Debug("[ForgeInstaller] Running forge installer in offline mode");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = JavaPath,
            Arguments = $"-jar {Path.GetFileName(InstallerPath)} --offline --installServer",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null) return;

            Log.Verbose("[ForgeInstaller] Offline installer stdout: {0}", args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null) return;

            Log.Verbose("[ForgeInstaller] Offline installer stderr: {0}", args.Data);
        };
        if (!process.Start())
        {
            Log.Error("[ForgeInstaller] Failed to start offline installer");
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            Log.Error("[ForgeInstaller] Offline installer exited with code {0}", process.ExitCode);
            process.Close();
            return false;
        }

        process.Close();
        return true;
    }

    #region Download

    protected async Task<bool> DownloadMinecraft(string targetPath,
        CancellationToken ct = default)
    {
        var success = MirrorType switch
        {
            InstanceFactoryMirror.BmclApi => await DownloadMinecraft(await Install.GetMcDownloadFromBmclApi(ct),
                targetPath, ct),
            InstanceFactoryMirror.None => false,
            _ => throw new NotImplementedException()
        };

        if (success) return true;
        return await DownloadMinecraft(await Install.GetMcDownload(ct), targetPath, ct);
    }

    private static async Task<bool> DownloadMinecraft(Version.Download? dl, string targetPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(dl?.Url)) return false;
        return await Download(dl.Url, targetPath, dl.Sha1, ct);
    }

    protected static Task<bool> Download(string url, string target, string? sha1 = null,
        CancellationToken ct = default)
    {
        return DownloadCore(url, target, async downloadedFilename =>
        {
            // 检查sha1
            if (string.IsNullOrEmpty(sha1))
            {
                var fileSha1 = await FileManager.FileSha1(downloadedFilename, ct);
                if (fileSha1 != sha1)
                {
                    File.Delete(downloadedFilename);
                    return false;
                }
            }

            return true;
        }, ct);
    }

    protected static Task<bool> Download(string url, string target, IEnumerable<string> checksums,
        CancellationToken ct = default)
    {
        return DownloadCore(url, target, async downloadedFilename =>
        {
            var fileSha1 = await FileManager.FileSha1(downloadedFilename, ct);
            var checksumList = checksums.ToArray();

            if (checksumList.Length == 0) return true; // 空则跳过校验
            if (checksumList.Any(checksum => checksum == fileSha1)) return true;
            File.Delete(downloadedFilename);
            return false;
        }, ct);
    }

    protected static async Task<bool> DownloadCore(
        string url,
        string target,
        Func<string, Task<bool>> predicate,
        CancellationToken ct = default
    )
    {
        // 下载
        var dl = new DownloadBuilder()
            .WithUrl(url)
            .WithFileLocation(target)
            .WithConfiguration(new DownloadConfiguration
            {
                ParallelDownload = true,
                ParallelCount = 4,
                MaxTryAgainOnFailover = 3,
                RequestConfiguration = new RequestConfiguration
                {
                    UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36 Edg/135.0.0.0"
                }
            }).Build();
        await dl.StartAsync(ct);
        if (dl.Status == DownloadStatus.Failed)
        {
            Log.Warning("[ForgeInstaller] Failed to download forge installer: {0}", url);
            dl.Dispose(); // 提前释放防止文件占用
            File.Delete(target);
            return false;
        }

        dl.Dispose();

        // 检查sha1
        return await predicate.Invoke(target);
    }

    #endregion

    #region Self Extrat

    protected static TDeserialized? ReadInstallerProfile<TDeserialized>(string installerPath,
        Func<string, TDeserialized> reader)
    {
        using var archive = new ZipArchive(File.OpenRead(installerPath));
        var profileJson = archive.GetEntry("install_profile.json");
        if (profileJson is null)
        {
            Log.Error("[ForgeInstaller] Failed to find install_profile.json in the installer");
            return default;
        }

        try
        {
            using var s = profileJson.Open();
            using var sr = new StreamReader(s);
            var content = sr.ReadToEnd();
            return reader.Invoke(content);
        }
        catch (SystemException e)when (e is NotSupportedException or InvalidDataException)
        {
            Log.Error("[ForgeInstaller] Failed to read install_profile.json in forge installer: {0}", e);
            return default;
        }
        catch (Exception e) when (e is NullReferenceException or JsonException)
        {
            Log.Error("[ForgeInstaller] Failed to parse install_profile.json: {0}", e);
            return default;
        }
        catch (IOException e)
        {
            Log.Error("[ForgeInstaller] Failed to open forge installer: {0}", e);
            return default;
        }
    }

    protected async Task<bool> ExtractContained(
        string inZipPath,
        string targetPath,
        CancellationToken ct = default
    )
    {
        using var jar = ZipFile.OpenRead(InstallerPath);
        var entry = jar.GetEntry(inZipPath.Replace("\\", "/"));
        if (entry is null) return false;

        await using var stream = entry.Open();
        await using var file = File.Create(targetPath);
        await stream.CopyToAsync(file, 8192, ct);
        return true;
    }

    protected Task<bool> ExtractMavenFromInstaller(Artifact artifact, string targetPath,
        CancellationToken ct = default)
    {
        return ExtractContained(Path.Combine("maven", artifact.Path), targetPath, ct);
    }

    protected async Task<bool> ExtractProfileContained(
        string workingDirectory,
        CancellationToken ct = default
    )
    {
        var contained = Install.Path;

        // STAGE: 解压main jar
        if (contained is not null)
        {
            var targetPath = Path.Combine(workingDirectory, contained.Filename);
            if (await ExtractContained(contained.Filename, targetPath, ct))
            {
                Log.Debug("[ForgeInstaller] Extracted main jar from installer to {0}", targetPath);
            }
            else
            {
                Log.Warning("[ForgeInstaller] Failed to extract main jar from installer");
                return false;
            }
        }

        return true;
    }

    protected void DeleteInstaller()
    {
        File.Delete(InstallerPath);
        File.Delete(InstallerPath + ".log");
    }

    #endregion
}