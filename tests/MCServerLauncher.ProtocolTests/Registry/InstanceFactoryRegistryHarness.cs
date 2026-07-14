using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Factory;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

internal static class InstanceFactoryRegistryHarness
{
    public static void ResetAndInitializeDefaults()
    {
        InstanceFactoryRegistry.Reset();
        InstanceFactoryRegistry.InitializeDefaults();
    }

    public static InstanceFactoryConfiguration CreateSetting(
        InstanceType instanceType,
        SourceType sourceType,
        string mcVersion)
    {
        return new InstanceFactoryConfiguration(
            new InstanceConfiguration(
                Guid.NewGuid(),
                "characterization-instance",
                "server.jar",
                instanceType,
                TargetType.Jar,
                mcVersion,
                "utf-8",
                "utf-8",
                "java",
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                JsonSerializer.SerializeToElement(Array.Empty<object>())),
            "characterization-source",
            sourceType,
            InstanceFactoryMirror.None,
            false);
    }

    public static Delegate GetResolvedFactory(InstanceFactoryConfiguration setting)
    {
        return InstanceFactoryRegistry.GetInstanceFactory(setting);
    }
}

[CollectionDefinition("InstanceFactoryRegistryIsolation", DisableParallelization = true)]
public sealed class InstanceFactoryRegistryIsolationCollection;
