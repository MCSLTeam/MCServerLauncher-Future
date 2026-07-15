using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management;
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

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public async Task ResolvedFactory_PropagatesCancellationToken()
    {
        InstanceFactoryRegistry.Reset();
        TokenCapturingFactory.ObservedToken = default;
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(TokenCapturingFactory));
        var setting = InstanceFactoryRegistryHarness.CreateSetting(
            InstanceType.MCBedrock,
            SourceType.Core,
            "1.20.1");
        var factory = Assert.IsType<Func<InstanceFactoryConfiguration, CancellationToken,
            Task<Result<InstanceConfiguration, DaemonError>>>>(
            InstanceFactoryRegistryHarness.GetResolvedFactory(setting));
        using var cancellationSource = new CancellationTokenSource();

        try
        {
            var result = await factory(setting, cancellationSource.Token);

            Assert.True(result.IsOk(out _));
            Assert.Equal(cancellationSource.Token, TokenCapturingFactory.ObservedToken);
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
        }
    }

    [Fact]
    [Trait("Category", "InstanceFactoryRegistry")]
    public async Task ApplyInstanceFactory_FactoryNotImplementedException_ReturnsInternalError()
    {
        InstanceFactoryRegistry.Reset();
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(NotImplementedThrowingFactory));
        var setting = InstanceFactoryRegistryHarness.CreateSetting(
            InstanceType.MCBedrock,
            SourceType.Core,
            "1.20.1");

        try
        {
            var result = await setting.ApplyInstanceFactory();

            Assert.True(result.IsErr(out var error));
            var internalError = Assert.IsType<InternalDaemonError>(error);
            Assert.Equal("instance.factory.failed", internalError.Code);
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
        }
    }

    [InstanceFactory(InstanceType.MCVanilla, SourceType.Core, "1.20.1", "1.20.1")]
    [InstanceFactory(InstanceType.MCVanilla, SourceType.Core, "1.20.1", "1.20.1")]
    private sealed class DuplicateVanillaCoreFactory : ICoreInstanceFactory
    {
        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Func<MinecraftInstance, Task<Result<Unit, DaemonError>>>[] GetPostProcessors()
            => [];
    }

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class TokenCapturingFactory : ICoreInstanceFactory
    {
        public static CancellationToken ObservedToken { get; set; }

        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(ResultExt.Ok(setting.Configuration));
        }
    }

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class NotImplementedThrowingFactory : ICoreInstanceFactory
    {
        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("factory implementation defect");
        }
    }
}
