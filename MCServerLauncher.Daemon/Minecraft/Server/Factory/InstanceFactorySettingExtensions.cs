using System.Reflection;
using Downloader;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

public static class InstanceFactorySettingExtensions
{
    private static readonly
        Dictionary<
            InstanceType,
            Dictionary<
                SourceType,
                Dictionary<
                    (McVersion, McVersion),
                    Func<InstanceFactorySetting, Task<InstanceConfig>>
                >
            >
        > InstanceFactoryMapping = new();

    /// <summary>
    ///     加载所有IInstanceFactory
    /// </summary>
    public static void RegisterFactories()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var type in assembly.GetTypes())
        {
            var attributes = type.GetCustomAttributes<InstanceFactoryAttribute>().ToArray();

            if (attributes.Length == 0) continue;

            var factoryInstance = Activator.CreateInstance(type)!;
            foreach (var attr in attributes)
            {
                var sourceTypeMapping = InstanceFactoryMapping.GetValueOrDefault(attr.InstanceType) ??
                                        (InstanceFactoryMapping[attr.InstanceType] =
                                            new Dictionary<SourceType, Dictionary<(McVersion, McVersion),
                                                Func<InstanceFactorySetting, Task<InstanceConfig>>>>());

                var allowedSourceType = attr.AllowedSourceType;
                if ((allowedSourceType == SourceType.Core || attr.AllowedSourceType == SourceType.None) &&
                    factoryInstance is ICoreInstanceFactory coreInstanceFactory
                   )
                {
                    RegisterInstanceFactory(sourceTypeMapping, SourceType.Core,
                        coreInstanceFactory.CreateInstanceFromCore, attr);
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.FullName}\" as {attr.InstanceType}(SourceType={SourceType.Core}); Minecraft version range: \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }

                if ((allowedSourceType == SourceType.Archive || attr.AllowedSourceType == SourceType.None) &&
                    factoryInstance is IArchiveInstanceFactory archiveInstanceFactory
                   )
                {
                    RegisterInstanceFactory(sourceTypeMapping, SourceType.Archive,
                        archiveInstanceFactory.CreateInstanceFromArchive, attr);
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.FullName}\" as {attr.InstanceType}(SourceType={SourceType.Archive}); Minecraft version range: \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }

                if ((allowedSourceType == SourceType.Script || attr.AllowedSourceType == SourceType.None) &&
                    factoryInstance is IScriptInstanceFactory scriptInstanceFactory)
                {
                    RegisterInstanceFactory(sourceTypeMapping, SourceType.Script,
                        scriptInstanceFactory.CreateInstanceFromScript, attr);
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.FullName}\" as {attr.InstanceType}(SourceType={SourceType.Script}); Minecraft version range: \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }
            }
        }
    }

    private static void RegisterInstanceFactory(
        Dictionary<SourceType, Dictionary<(McVersion, McVersion), Func<InstanceFactorySetting, Task<InstanceConfig>>>>
            sourceTypeMapping,
        SourceType sourceType,
        Func<InstanceFactorySetting, Task<InstanceConfig>> instanceFactory,
        InstanceFactoryAttribute attribute
    )
    {
        var factories =
            sourceTypeMapping.GetValueOrDefault(sourceType) ??
            (sourceTypeMapping[sourceType] =
                new Dictionary<(McVersion, McVersion), Func<InstanceFactorySetting, Task<InstanceConfig>>>());


        factories[(McVersion.Of(attribute.MinVersion), McVersion.Of(attribute.MaxVersion))] = instanceFactory;
    }


    /// <summary>
    ///     复制目标文件并依据setting.Target重命名,如果Source是网络资源，则尝试下载他
    /// </summary>
    /// <param name="setting"></param>
    public static async Task CopyAndRenameTarget(this InstanceFactorySetting setting)
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
        if (setting.Target != dst) File.Move(dst, Path.Combine(workingDirectory, setting.Target));
    }

    public static Task<InstanceConfig> ApplyInstanceFactory(this InstanceFactorySetting setting)
    {
        if (InstanceFactoryMapping.TryGetValue(setting.InstanceType, out var sourceTypeMapping))
        {
            if (sourceTypeMapping.TryGetValue(setting.SourceType, out var versionMapping))
            {
                var targetVersion = McVersion.Of(setting.McVersion);
                var kv = versionMapping.FirstOrDefault(kv => targetVersion.Between(kv.Key.Item1, kv.Key.Item2));
                if (kv.Value != null)
                {
                    var factory = kv.Value(setting);
                    return factory;
                }

                throw new Exception(
                    $"Unsupported minecraft version({setting.McVersion}) for {setting.InstanceType}(SourceType={setting.SourceType})");
            }

            throw new Exception($"Unsupported source type({setting.SourceType}) for {setting.InstanceType}");
        }

        throw new Exception($"Unsupported instance type({setting.InstanceType})");
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
        else if (!File.Exists(setting.Source)) return false;

        if (setting.Uuid == Guid.Empty) return false;

        return true;
    }
}