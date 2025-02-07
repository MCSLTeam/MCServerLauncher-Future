using System.Reflection;
using Downloader;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

public class InstanceFactorySetting : InstanceConfig
{
    public string mcVersion { get; set; }
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
    public static void LoadFactories()
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
                if (factoryInstance is ICoreInstanceFactory coreInstanceFactory)
                {
                    var coreFactories =
                        sourceTypeMapping.GetValueOrDefault(SourceType.Core) ??
                        (sourceTypeMapping[SourceType.Core] =
                            new Dictionary<(McVersion, McVersion),
                                Func<InstanceFactorySetting, Task<InstanceConfig>>>());

                    var factoryMethodInfo = coreInstanceFactory.GetType().GetMethods()[0];

                    coreFactories[(McVersion.Of(attr.MinVersion), McVersion.Of(attr.MaxVersion))] = setting =>
                        (factoryMethodInfo.Invoke(factoryInstance, new object[] { setting }) as Task<InstanceConfig>)!;
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.Name}\" as {attr.InstanceType}(type={SourceType.Core}) \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }

                if (factoryInstance is IArchiveInstanceFactory archiveInstanceFactory)
                {
                    var archiveFactories =
                        sourceTypeMapping.GetValueOrDefault(SourceType.Archive) ??
                        (sourceTypeMapping[SourceType.Archive] =
                            new Dictionary<(McVersion, McVersion),
                                Func<InstanceFactorySetting, Task<InstanceConfig>>>());

                    var factoryMethodInfo = archiveInstanceFactory.GetType().GetMethods()[0];

                    archiveFactories[(McVersion.Of(attr.MinVersion), McVersion.Of(attr.MaxVersion))] = setting =>
                        (factoryMethodInfo.Invoke(factoryInstance, new object[] { setting }) as Task<InstanceConfig>)!;
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.Name}\" as {attr.InstanceType}(type={SourceType.Archive}) \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }

                if (factoryInstance is IScriptInstanceFactory scriptInstanceFactory)
                {
                    var scriptFactories =
                        sourceTypeMapping.GetValueOrDefault(SourceType.Script) ??
                        (sourceTypeMapping[SourceType.Script] =
                            new Dictionary<(McVersion, McVersion),
                                Func<InstanceFactorySetting, Task<InstanceConfig>>>());

                    var factoryMethodInfo = scriptInstanceFactory.GetType().GetMethods()[0];

                    scriptFactories[(McVersion.Of(attr.MinVersion), McVersion.Of(attr.MaxVersion))] = setting =>
                        (factoryMethodInfo.Invoke(factoryInstance, new object[] { setting }) as Task<InstanceConfig>)!;
                    Log.Debug(
                        $"[InstanceFactorySetting] Loaded \"{type.Name}\" as {attr.InstanceType}(type={SourceType.Script}) \"{attr.MinVersion}\" ~ \"{attr.MaxVersion}\"");
                }
            }
        }
    }


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
            _ => throw new NotImplementedException()
        };
    }

    public static Task<InstanceConfig> ApplyInstanceFactory(this InstanceFactorySetting setting)
    {
        if (InstanceFactoryMapping.TryGetValue(setting.InstanceType, out var sourceTypeMapping))
        {
            if (sourceTypeMapping.TryGetValue(setting.SourceType, out var versionMapping))
            {
                var targetVersion = McVersion.Of(setting.Target);
                var kv = versionMapping.FirstOrDefault(kv => targetVersion.Between(kv.Key.Item1, kv.Key.Item2));
                if (kv.Value != null)
                {
                    var factory = kv.Value(setting);
                    return factory;
                }

                throw new Exception(
                    $"Unsupported minecraft version({setting.mcVersion}) for {setting.InstanceType}(SourceType={setting.SourceType})");
            }

            throw new Exception($"Unsupported source type({setting.SourceType}) for {setting.InstanceType}");
        }

        throw new Exception($"Unsupported instance type({setting.InstanceType})");
    }
}