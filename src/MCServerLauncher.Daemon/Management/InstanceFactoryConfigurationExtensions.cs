using System.Text;
using Downloader;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Detection;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

public static class InstanceFactoryConfigurationExtensions
{
    public static async Task<Result<Unit, Error>> CopyAndRenameTarget(this InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        var workingDirectory = config.GetWorkingDirectory();
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                workingDirectory,
                config.Target,
                out var targetPath,
                out var targetError))
        {
            return ResultExt.Err<Unit>(targetError!);
        }

        var destinationName = Path.GetFileName(setting.Source);
        var destinationPath = Path.Combine(workingDirectory, destinationName);

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                var sourcePath = uri.LocalPath;
                if (sourcePath != Path.GetFullPath(sourcePath))
                    File.Copy(sourcePath, destinationPath);
            }
            else if (uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == Uri.UriSchemeFtps ||
                     uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var download = DownloadBuilder
                    .New()
                    .WithUrl(setting.Source)
                    .WithFileLocation(destinationPath)
                    .WithConfiguration(new DownloadConfiguration
                    {
                        ChunkCount = 8,
                        ParallelDownload = true
                    }).Build()!;
                await download.StartAsync();
                if (download.Status == DownloadStatus.Failed)
                {
                    File.Delete(destinationPath);
                    return Result.Err<Unit, Error>("Failed to download source");
                }
            }
        }
        else if (setting.Source != destinationPath)
        {
            var resolvedSource = FileManager.ResolveAndValidatePath(setting.Source);
            var copy = ResultExt.Try(() => File.Copy(resolvedSource, destinationPath));
            if (copy.IsErr(out var error))
            {
                File.Delete(destinationPath);
                return Result.Err<Unit, Error>($"failed to copy source: {error}");
            }
        }

        return config.Target != destinationPath
            ? ResultExt.Try(() => File.Move(destinationPath, targetPath))
                .MapErr(static exception => new Error().CauseBy(exception))
            : ResultExt.Ok(Unit.Default);
    }

    public static Task<Result<InstanceConfiguration, Error>> ApplyInstanceFactory(
        this InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                config.GetWorkingDirectory(),
                config.Target,
                out _,
                out var targetError))
        {
            return Task.FromResult(ResultExt.Err<InstanceConfiguration>(targetError!));
        }

        var reconciledSetting = InstanceVersionDetector.Reconcile(
            setting,
            source => FileManager.ResolveAndValidatePath(source));
        var instanceFactory = InstanceFactoryRegistry.GetInstanceFactory(reconciledSetting);
        Log.Information(
            "[InstanceManager] Running InstanceFactory for instance '{0}' as {1}",
            reconciledSetting.Configuration.Name,
            reconciledSetting.Configuration.InstanceType);
        return instanceFactory.Invoke(reconciledSetting);
    }

    public static Result<Unit, Error> ValidateSetting(this InstanceFactoryConfiguration setting)
    {
        if (setting.SourceType is SourceType.None)
            return ResultExt.Err<Unit>("source_type could not be none");

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && !File.Exists(uri.LocalPath))
                return ResultExt.Err<Unit>($"source not found at {uri.LocalPath}");
        }
        else
        {
            var resolvedPath = FileManager.ResolveAndValidatePath(setting.Source);
            if (!File.Exists(resolvedPath))
                return ResultExt.Err<Unit>($"source not found at {setting.Source}");
        }

        var reconciledSetting = InstanceVersionDetector.Reconcile(
            setting,
            source => FileManager.ResolveAndValidatePath(source));
        return reconciledSetting.Configuration.ValidateConfig();
    }

    public static async Task<Result<Unit, Error>> FixEula(this InstanceFactoryConfiguration setting)
    {
        var eulaPath = Path.Combine(setting.Configuration.GetWorkingDirectory(), "eula.txt");

        var eula = await ReadOrGenerateEula(eulaPath);
        if (eula.IsErr(out var readError) && readError is not null)
            return ResultExt.Err<Unit>(readError);

        return await WriteEula(eulaPath, eula.Unwrap());
    }

    private static string[] GenerateEula()
    {
        return
        [
            "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).",
            "#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss zzz yyyy"),
            "eula=true"
        ];
    }

    private static async Task<Result<string[], Error>> ReadOrGenerateEula(string eulaPath)
    {
        var result = await ResultExt.TryAsync(async path =>
        {
            return File.Exists(path)
                ? (await File.ReadAllLinesAsync(path))
                .Select(static line => line.Trim().StartsWith("eula") ? "eula=true" : line)
                .ToArray()
                : GenerateEula();
        }, eulaPath);

        return result.MapErr(static exception => new Error("Failed to read EULA").CauseBy(exception));
    }

    private static async Task<Result<Unit, Error>> WriteEula(string eulaPath, string[] text)
    {
        var result = await ResultExt.TryAsync(async state =>
        {
            var (path, lines) = state;
            await File.WriteAllLinesAsync(path, lines, Encoding.UTF8);
        }, (eulaPath, text));

        return result.MapErr(static exception => new Error("Failed to write EULA").CauseBy(exception));
    }
}
