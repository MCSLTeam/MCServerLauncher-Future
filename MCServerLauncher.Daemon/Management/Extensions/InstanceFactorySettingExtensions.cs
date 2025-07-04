using Downloader;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Extensions;

public static class InstanceFactorySettingExtensions
{
    /// <summary>
    ///     复制目标文件并依据setting.Target重命名,如果Source是网络资源，则尝试下载他
    /// </summary>
    /// <param name="setting"></param>
    public static async Task<Result<Unit, Error>> CopyAndRenameTarget(this InstanceFactorySetting setting)
    {
        var workingDirectory = setting.GetWorkingDirectory();

        var dstName = Path.GetFileName(setting.Source);
        var dst = Path.Combine(workingDirectory, dstName);

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            // if Source is a local file, copy it
            if (uri.IsFile)
            {
                // get file
                var sourcePath = uri.LocalPath;
                // copy
                if (sourcePath != Path.GetFullPath(sourcePath)) File.Copy(sourcePath, dst);
            }
            // if Source is a internet resource, download it
            else if (uri.Scheme == Uri.UriSchemeFtp || uri.Scheme == Uri.UriSchemeFtps ||
                     uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var dl = DownloadBuilder
                    .New()
                    .WithUrl(setting.Source)
                    .WithFileLocation(dst)
                    .WithConfiguration(new DownloadConfiguration
                    {
                        ChunkCount = 8,
                        ParallelDownload = true
                    }).Build()!;
                await dl.StartAsync();
                if (dl.Status == DownloadStatus.Failed)
                {
                    File.Delete(dst);
                    return Result.Err<Unit, Error>("Failed to download source");
                }
            }
        }
        else if (setting.Source != dst)
        {
            var copy = ResultExt.Try(() => File.Copy(setting.Source, dst));
            if (copy.IsErr(out var err))
            {
                File.Delete(dst);
                return Result.Err<Unit, Error>($"failed to copy source: {err}");
            }
        }

        // rename
        return setting.Target != dst
            ? ResultExt.Try(() => File.Move(dst, Path.Combine(workingDirectory, setting.Target)))
                .MapErr(ex => new Error().CauseBy(ex))
            : ResultExt.Ok(Unit.Default);
    }

    /// <summary>
    ///     应用实例工厂
    /// </summary>
    /// <param name="setting"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <exception cref="InstanceFactoryException"></exception>
    /// <returns></returns>
    public static Task<Result<InstanceConfig, Error>> ApplyInstanceFactory(this InstanceFactorySetting setting)
    {
        var instanceFactory = InstanceFactoryRegistry.GetInstanceFactory(setting);
        Log.Information("[InstanceManager] Running InstanceFactory for instance '{0}'", setting.Name);
        return instanceFactory.Invoke(setting);
    }

    public static Result<Unit, Error> ValidateSetting(this InstanceFactorySetting setting)
    {
        if (setting.SourceType is SourceType.None)
            return ResultExt.Err<Unit>("source_type could not be none");

        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && !File.Exists(uri.LocalPath))
                return ResultExt.Err<Unit>($"source not found at {uri.LocalPath}");
        }
        else if (!File.Exists(setting.Source))
        {
            return ResultExt.Err<Unit>($"source not found at {setting.Source}");
        }

        return setting.ValidateConfig();
    }
}