using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void ProtocolIdentifiersUseTheirOwnValidatedGrammar()
    {
        Assert.Throws<ArgumentException>(() => new RpcMethod("MCSl.instance.start"));
        Assert.Throws<ArgumentException>(() => new RpcMethod("mcsl..instance.start"));
        Assert.Throws<ArgumentException>(() => new RpcMethod("mcsl.bad-.next"));
        Assert.Throws<ArgumentException>(() => new RpcMethod("mcsl.bad_.next"));
        Assert.Throws<ArgumentException>(() => new RpcMethod("mcsl.instance.start!"));
        Assert.Throws<ArgumentException>(() => new EventName(""));
        Assert.Throws<ArgumentException>(() => new EventName("mcsl.event.changed_"));
        Assert.Throws<ArgumentException>(() => new PermissionName("instance read"));
        Assert.Throws<ArgumentException>(() => new PermissionName("mcsl.**"));
        Assert.Throws<ArgumentException>(() => new PluginFeature("RPC.register"));
        Assert.Throws<ArgumentException>(() => new PluginFeature("rpc/register"));

        Assert.Equal("mcsl.instance.start", new RpcMethod("mcsl.instance.start").Value);
        Assert.Equal("mcsl.event.instance-log", new EventName("mcsl.event.instance-log").Value);
        Assert.Equal("*", new PermissionName("*").Value);
        Assert.Equal("instance.read", new PermissionName("instance.read").Value);
        Assert.Equal("rpc.register", new PluginFeature("rpc.register").Value);
    }

    [Fact]
    public void ProtocolIdentifiersCannotBeDefaultConstructedAndHaveValueEquality()
    {
        Assert.DoesNotContain(typeof(RpcMethod).GetConstructors(), constructor => constructor.GetParameters().Length == 0);
        Assert.DoesNotContain(typeof(EventName).GetConstructors(), constructor => constructor.GetParameters().Length == 0);
        Assert.DoesNotContain(typeof(PermissionName).GetConstructors(), constructor => constructor.GetParameters().Length == 0);
        Assert.DoesNotContain(typeof(PluginFeature).GetConstructors(), constructor => constructor.GetParameters().Length == 0);

        Assert.ThrowsAny<ArgumentException>(() => new RpcMethod(null!));
        Assert.ThrowsAny<ArgumentException>(() => new EventName(null!));
        Assert.ThrowsAny<ArgumentException>(() => new PermissionName(null!));
        Assert.ThrowsAny<ArgumentException>(() => new PluginFeature(null!));

        Assert.Equal(new RpcMethod("mcsl.instance.start"), new RpcMethod("mcsl.instance.start"));
        Assert.NotEqual(new RpcMethod("mcsl.instance.start"), new RpcMethod("mcsl.instance.stop"));
        Assert.Equal(new PermissionName("*"), new PermissionName("*"));
    }

    [Fact]
    public void DescriptorConstructionIsHostControlledAndCatalogUsesNonGenericBases()
    {
        Assert.True(typeof(RpcDescriptor).IsAbstract);
        Assert.True(typeof(EventDescriptor).IsAbstract);
        Assert.Empty(typeof(RpcDescriptor<SampleRequest, SampleResult>).GetConstructors());
        Assert.Empty(typeof(EventDescriptor<SampleResult, SampleMetadata>).GetConstructors());
        Assert.All(BuiltInProtocolDefinitions.Rpcs, descriptor => Assert.IsAssignableFrom<RpcDescriptor>(descriptor));
        Assert.All(BuiltInProtocolDefinitions.Events, descriptor => Assert.IsAssignableFrom<EventDescriptor>(descriptor));
    }

    [Fact]
    public void HostControlledDescriptorBasesDoNotExposePublicOrProtectedConstruction()
    {
        var rpcConstructor = Assert.Single(typeof(RpcDescriptor).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        var eventConstructor = Assert.Single(typeof(EventDescriptor).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));

        Assert.True(rpcConstructor.IsFamilyAndAssembly);
        Assert.True(eventConstructor.IsFamilyAndAssembly);
    }

    [Fact]
    public void PluginFeaturesIncludePhaseOneAndPreviewVocabulary()
    {
        Assert.Equal("rpc.register", PluginFeature.RpcRegister.Value);
        Assert.Equal("event.publish", PluginFeature.EventPublish.Value);
        Assert.Equal("instance.query", PluginFeature.InstanceQuery.Value);
        Assert.Equal("network.http.listen", PluginFeature.NetworkHttpListen.Value);
        Assert.Equal("auth.verify", PluginFeature.AuthVerify.Value);
    }

    public sealed record SampleRequest(string Value);

    public sealed record SampleResult(string Value);

    public sealed record SampleMetadata(string Source);
}

[JsonSerializable(typeof(ProtocolContractTests.SampleRequest))]
[JsonSerializable(typeof(ProtocolContractTests.SampleResult))]
[JsonSerializable(typeof(ProtocolContractTests.SampleMetadata))]
internal partial class ApiTestJsonContext : JsonSerializerContext;
