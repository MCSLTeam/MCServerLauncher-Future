using Downloader;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;

namespace MCServerLauncher.Daemon.Minecraft.Extensions;

public static class InstanceFactorySettingExtensions
{
    /// <summary>
    ///     复制目标文件并依据setting.Target重命名,如果Source是网络资源，则尝试下载他
    /// </summary>
    /// <param name="setting"></param>
    public static async Task<bool> CopyAndRenameTarget(this InstanceFactorySetting setting)
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
                    return false;
                }
            }
        }
        else if (setting.Source != dst)
        {
            File.Copy(setting.Source, dst);
        }

        // rename
        if (setting.Target != dst) File.Move(dst, Path.Combine(workingDirectory, setting.Target));
        return true;
    }

    /// <summary>
    ///     应用实例工厂
    /// </summary>
    /// <param name="setting"></param>
    /// <exception cref="NotImplementedException"></exception>
    /// <exception cref="InstanceFactoryException"></exception>
    /// <returns></returns>
    public static Task<InstanceConfig> ApplyInstanceFactory(this InstanceFactorySetting setting)
    {
        return InstanceFactoryRegistry.GetInstanceFactory(setting).Invoke(setting);
    }

    public static bool ValidateSetting(this InstanceFactorySetting setting)
    {
        if (
            setting.SourceType == SourceType.None ||
            setting.InstanceType == InstanceType.None ||
            setting.TargetType == TargetType.None
        )
            return false;

        try
        {
            McVersion.Of(setting.McVersion);
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }

        if (setting.JavaPath.ToLower() != "java" && !File.Exists(setting.JavaPath)) return false;
        if (Uri.TryCreate(setting.Source, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile && !File.Exists(uri.LocalPath)) return false;
        }
        else if (!File.Exists(setting.Source))
        {
            return false;
        }

        if (setting.Uuid == Guid.Empty) return false;

        return true;
    }
}