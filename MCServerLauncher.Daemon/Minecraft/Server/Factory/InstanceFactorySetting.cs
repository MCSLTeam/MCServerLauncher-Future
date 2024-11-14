using Downloader;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

public class InstanceFactorySetting : InstanceConfig
{
    // TODO 支持网络上的Source
    public string Source { get; set; }
    public SourceType SourceType { get; set; }
    public bool UsePostProcess { get; set; } = false;

    public InstanceConfig GetInstanceConfig()
    {
        return this;
    }
}

public enum SourceType
{
    Archive,
    Core,
    Script
}

public static class InstanceFactorySettingExtensions
{
    /// <summary>
    ///     复制目标文件并依据setting.Target重命名,如果Source是网络资源，则尝试下载他
    /// </summary>
    /// <param name="setting"></param>
    public static async Task CopyAndRenameTarget(this InstanceFactorySetting setting)
    {
        var dstName = Path.GetFileName(setting.Source);
        var dst = Path.Combine(setting.WorkingDirectory, dstName);

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
                await DownloadBuilder
                    .New()
                    .WithUrl(setting.Source)
                    .WithFileLocation(dst)
                    .WithConfiguration(new DownloadConfiguration
                    {
                        ChunkCount = 8,
                        ParallelDownload = true
                    }).Build()
                    .StartAsync();
            }
        }
        else if (setting.Source != dst)
        {
            File.Copy(setting.Source, dst);
        }

        // rename
        if (setting.Target != dst) File.Move(dst, Path.Combine(setting.WorkingDirectory, setting.Target));
    }

    public static IInstanceFactory GetInstanceFactory(this InstanceFactorySetting setting)
    {
        return setting.InstanceType switch
        {
            InstanceType.Vanilla => new UniversalFactory(),
            InstanceType.Fabric => new FabricFactory(),
            InstanceType.Spigot => new SpigotFactory(),
            _ => throw new NotImplementedException()
        };
    }
}