using System.Reflection;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Extensions;
using MCServerLauncher.Daemon.Management.Minecraft;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Factory;

public static class InstanceFactoryRegistry
{
    private const string CF_TEMPLATE =
        "[InstanceFactoryRegistry] Loaded \"{0}\" as {1}(SourceType={2}); Minecraft version range: \"{3}\" ~ \"{4}\"";

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
                // 获取当前实例的SourceTypeMapping, 没有就初始化
                var sourceTypeMapping
                    = InstanceFactoryMapping.GetValueOrDefault(attr.InstanceType) ??
                      (InstanceFactoryMapping[attr.InstanceType] =
                          new Dictionary<SourceType, Dictionary<(McVersion, McVersion),
                              Func<InstanceFactorySetting, Task<InstanceConfig>>>>());

                var allowedSourceType = attr.AllowedSourceType;

                // 如果allowedSourceType为Core，或者没有限制，且工厂实例实现了ICoreInstanceFactory, 则注册CoreFactory
                if (
                    allowedSourceType is SourceType.Core or SourceType.None &&
                    factoryInstance is ICoreInstanceFactory coreInstanceFactory
                )
                {
                    RegisterInstanceFactory(
                        sourceTypeMapping,
                        SourceType.Core,
                        coreInstanceFactory.CreateInstanceFromCore,
                        attr
                    );
                    Log.Verbose(CF_TEMPLATE, type.FullName, attr.InstanceType, SourceType.Core, attr.MinVersion,
                        attr.MaxVersion);
                }

                // 如果allowedSourceType为Archive，或者没有限制，且工厂实例实现了IArchiveInstanceFactory, 则注册ArchiveFactory
                if (
                    allowedSourceType is SourceType.Archive or SourceType.None &&
                    factoryInstance is IArchiveInstanceFactory archiveInstanceFactory
                )
                {
                    RegisterInstanceFactory(
                        sourceTypeMapping,
                        SourceType.Archive,
                        archiveInstanceFactory.CreateInstanceFromArchive,
                        attr
                    );
                    Log.Verbose(CF_TEMPLATE, type.FullName, attr.InstanceType, SourceType.Archive, attr.MinVersion,
                        attr.MaxVersion);
                }

                // 如果allowedSourceType为Script，或者没有限制，且工厂实例实现了IScriptInstanceFactory, 则注册ScriptFactory
                if (
                    allowedSourceType is SourceType.Script or SourceType.None &&
                    factoryInstance is IScriptInstanceFactory scriptInstanceFactory)
                {
                    RegisterInstanceFactory(
                        sourceTypeMapping,
                        SourceType.Script,
                        scriptInstanceFactory.CreateInstanceFromScript,
                        attr
                    );
                    Log.Verbose(CF_TEMPLATE, type.FullName, attr.InstanceType, SourceType.Script, attr.MinVersion,
                        attr.MaxVersion);
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
    ///     获取实例工厂
    /// </summary>
    /// <param name="setting"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Func<InstanceFactorySetting, Task<InstanceConfig>> GetInstanceFactory(InstanceFactorySetting setting)
    {
        if (InstanceFactoryMapping.TryGetValue(setting.InstanceType, out var sourceTypeMapping))
        {
            if (sourceTypeMapping.TryGetValue(setting.SourceType, out var versionMapping))
            {
                var targetVersion = McVersion.Of(setting.McVersion);
                var kv = versionMapping.FirstOrDefault(kv => targetVersion.Between(kv.Key.Item1, kv.Key.Item2));
                if (kv.Value != null) return kv.Value;

                throw new NotImplementedException(
                    $"Unsupported minecraft version({setting.McVersion}) for {setting.InstanceType}(SourceType={setting.SourceType})");
            }

            throw new NotImplementedException(
                $"Unsupported source type({setting.SourceType}) for {setting.InstanceType}");
        }

        throw new NotImplementedException($"Unsupported instance type({setting.InstanceType})");
    }
}