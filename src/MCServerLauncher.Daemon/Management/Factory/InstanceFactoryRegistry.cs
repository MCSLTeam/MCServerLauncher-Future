using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
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
                    Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>>
                >
            >
        > InstanceFactoryMapping = new();

    public static void Reset()
    {
        InstanceFactoryMapping.Clear();
    }

    public static void InitializeDefaults()
    {
        Reset();

        LoadFactoryFromType(GetDefaultUniversalFactoryType());
        LoadFactoryFromType(GetDefaultForgeFactoryType());
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type GetDefaultUniversalFactoryType()
    {
        return typeof(MCUniversalFactory);
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type GetDefaultForgeFactoryType()
    {
        return typeof(MCForgeFactory);
    }

    public static void LoadFactoryFromType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
    {
        var attributes = type.GetCustomAttributes<InstanceFactoryAttribute>().ToArray();

        if (attributes.Length == 0) return;

        var factoryInstance = Activator.CreateInstance(type)!;

        foreach (var attr in attributes)
        {
            // 获取当前实例的SourceTypeMapping, 没有就初始化
            var sourceTypeMapping
                = InstanceFactoryMapping.GetValueOrDefault(attr.InstanceType) ??
                  (InstanceFactoryMapping[attr.InstanceType] =
                      new Dictionary<SourceType, Dictionary<(McVersion, McVersion),
                          Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>>>>());

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

    private static void RegisterInstanceFactory(
        Dictionary<SourceType, Dictionary<(McVersion, McVersion),
                Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>>>>
            sourceTypeMapping,
        SourceType sourceType,
        Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>> instanceFactory,
        InstanceFactoryAttribute attribute
    )
    {
        var factories =
            sourceTypeMapping.GetValueOrDefault(sourceType) ??
            (sourceTypeMapping[sourceType] =
                new Dictionary<(McVersion, McVersion),
                    Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>>>());

        var versionRange = (McVersion.Of(attribute.MinVersion), McVersion.Of(attribute.MaxVersion));

        if (!factories.TryAdd(versionRange, instanceFactory))
        {
            throw new InvalidOperationException(
                $"Duplicate instance factory registration for InstanceType={attribute.InstanceType}, SourceType={sourceType}, VersionRange={attribute.MinVersion}~{attribute.MaxVersion}."
            );
        }
    }

    /// <summary>
    ///     获取实例工厂
    /// </summary>
    /// <param name="setting"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>> GetInstanceFactory(
        InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        if (InstanceFactoryMapping.TryGetValue(config.InstanceType, out var sourceTypeMapping))
        {
            if (sourceTypeMapping.TryGetValue(setting.SourceType, out var versionMapping))
            {
                if (config.InstanceType == InstanceType.MCJava && string.IsNullOrWhiteSpace(config.Version))
                {
                    var fallbackFactory = versionMapping
                        .OrderBy(kv => kv.Key.Item1)
                        .Select(kv => kv.Value)
                        .FirstOrDefault();
                    if (fallbackFactory is not null)
                        return fallbackFactory;
                }

                var targetVersion = McVersion.Of(config.Version);
                var kv = versionMapping.FirstOrDefault(kv => targetVersion.Between(kv.Key.Item1, kv.Key.Item2));
                if (kv.Value != null) return kv.Value;

                throw new NotImplementedException(
                    $"Unsupported minecraft version({config.Version}) for {config.InstanceType}(SourceType={setting.SourceType})");
            }

            throw new NotImplementedException(
                $"Unsupported source type({setting.SourceType}) for {config.InstanceType}");
        }

        throw new NotImplementedException($"Unsupported instance type({config.InstanceType})");
    }
}
