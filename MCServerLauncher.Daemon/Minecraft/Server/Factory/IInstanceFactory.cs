using System.Text;
using Downloader;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     MC服务器实例工厂接口, 用于创建服务器实例并生成Daemon可识别的配置文件
/// </summary>
public interface IInstanceFactory
{
}

/// <summary>
///     从压缩包创建服务器实例的工厂接口
/// </summary>
public interface IArchiveInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromArchive(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器核心文件创建服务器实例的工厂接口
/// </summary>
public interface ICoreInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器实例创建脚本创建服务器实例的工厂接口
/// </summary>
public interface IScriptInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromScript(InstanceFactorySetting setting);
}

public static class InstanceFactoryExtensions
{
    /// <summary>
    ///     生成同意 EULA 的文本
    /// </summary>
    /// <returns></returns>
    private static string[] GenerateEula()
    {
        var text = new string[3];
        text[0] =
            "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).";
        text[1] = "#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss zzz yyyy");
        text[2] = "eula=true";
        return text;
    }

    /// <summary>
    ///     为实例生成 EULA 文件
    /// </summary>
    /// <param name="config"></param>
    public static async Task FixEula(InstanceConfig config)
    {
        var eulaPath = Path.Combine(config.WorkingDirectory, "eula.txt");
        var text = File.Exists(eulaPath)
            ? (await File.ReadAllLinesAsync(eulaPath)).Select(x => eulaPath.Trim().StartsWith("eula") ? "eula=true" : x)
            .ToArray()
            : GenerateEula();
        await File.WriteAllLinesAsync(eulaPath, text, Encoding.UTF8);
    }

    /// <summary>
    ///     复制目标文件并依据setting.Target重命名,如果Source是网络资源，则尝试下载他
    /// </summary>
    /// <param name="setting"></param>
    public static async Task CopyAndRenameTarget(InstanceFactorySetting setting)
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

    /// <summary>
    ///     服务器实例创建的dispatcher
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="setting"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Task<InstanceConfig> CreateInstance(this IInstanceFactory factory, InstanceFactorySetting setting)
    {
        switch (setting.SourceType)
        {
            case SourceType.Archive:
                if (factory is IArchiveInstanceFactory archiveFactory)
                    return archiveFactory.CreateInstanceFromArchive(setting);
                break;
            case SourceType.Core:
                if (factory is ICoreInstanceFactory coreFactory) return coreFactory.CreateInstanceFromCore(setting);
                break;
            case SourceType.Script:
                if (factory is IScriptInstanceFactory scriptFactory)
                    return scriptFactory.CreateInstanceFromScript(setting);
                break;
        }

        throw new NotImplementedException($"No suitable factory found for SourceType.{setting.SourceType}");
    }
}