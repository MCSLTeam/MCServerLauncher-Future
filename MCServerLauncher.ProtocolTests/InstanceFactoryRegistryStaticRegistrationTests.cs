using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

[Collection("InstanceFactoryRegistryIsolation")]
public class InstanceFactoryRegistryStaticRegistrationTests
{
    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public void InitializeDefaults_MCVanillaCore_ResolvesUniversalFactory()
    {
        InstanceFactoryRegistryHarness.ResetAndInitializeDefaults();

        var factory = InstanceFactoryRegistryHarness.GetResolvedFactory(
            InstanceFactoryRegistryHarness.CreateSetting(InstanceType.MCVanilla, SourceType.Core, "1.20.1"));

        Assert.IsType<MCUniversalFactory>(factory.Target);
    }

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public void InitializeDefaults_MCForgeCore_ResolvesForgeFactory()
    {
        InstanceFactoryRegistryHarness.ResetAndInitializeDefaults();

        var factory = InstanceFactoryRegistryHarness.GetResolvedFactory(
            InstanceFactoryRegistryHarness.CreateSetting(InstanceType.MCForge, SourceType.Core, "1.20.1"));

        Assert.IsType<MCForgeFactory>(factory.Target);
    }

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public void InitializeDefaults_MCForgeArchive_ResolvesUniversalFactory()
    {
        InstanceFactoryRegistryHarness.ResetAndInitializeDefaults();

        var factory = InstanceFactoryRegistryHarness.GetResolvedFactory(
            InstanceFactoryRegistryHarness.CreateSetting(InstanceType.MCForge, SourceType.Archive, "1.5.2"));

        Assert.IsType<MCUniversalFactory>(factory.Target);
    }

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public void InitializeDefaults_UnsupportedSourceType_ThrowsNotImplementedException()
    {
        InstanceFactoryRegistryHarness.ResetAndInitializeDefaults();

        var setting = InstanceFactoryRegistryHarness.CreateSetting(InstanceType.MCForge, SourceType.Script, "1.20.1");

        Assert.Throws<NotImplementedException>(() => InstanceFactoryRegistryHarness.GetResolvedFactory(setting));
    }

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public void LoadFactoryFromType_DuplicateStaticRegistration_ThrowsInvalidOperationException()
    {
        InstanceFactoryRegistry.Reset();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            InstanceFactoryRegistry.LoadFactoryFromType(typeof(DuplicateVanillaCoreFactory)));

        Assert.Contains("MCVanilla", exception.Message);
        Assert.Contains("Core", exception.Message);
        Assert.Contains("1.20.1", exception.Message);
    }

    [InstanceFactory(InstanceType.MCVanilla, SourceType.Core, "1.20.1", "1.20.1")]
    [InstanceFactory(InstanceType.MCVanilla, SourceType.Core, "1.20.1", "1.20.1")]
    private sealed class DuplicateVanillaCoreFactory : ICoreInstanceFactory
    {
        public Task<Result<InstanceConfig, Error>> CreateInstanceFromCore(InstanceFactorySetting setting)
            => throw new NotSupportedException();

        public Func<MinecraftInstance, Task<Result<Unit, Error>>>[] GetPostProcessors()
            => [];
    }
}
