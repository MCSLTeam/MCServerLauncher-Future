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

    public static InstanceFactorySetting CreateSetting(InstanceType instanceType, SourceType sourceType, string mcVersion)
    {
        return new InstanceFactorySetting
        {
            InstanceType = instanceType,
            SourceType = sourceType,
            McVersion = mcVersion,
            Source = "characterization-source"
        };
    }

    public static Delegate GetResolvedFactory(InstanceFactorySetting setting)
    {
        return InstanceFactoryRegistry.GetInstanceFactory(setting);
    }
}

[CollectionDefinition("InstanceFactoryRegistryIsolation", DisableParallelization = true)]
public sealed class InstanceFactoryRegistryIsolationCollection;
