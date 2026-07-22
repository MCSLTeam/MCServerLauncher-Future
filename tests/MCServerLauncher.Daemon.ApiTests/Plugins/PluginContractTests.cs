using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using System.Reflection;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApiTests.Plugins;

public sealed class PluginContractTests
{
    [Fact]
    public void PluginIdentityRequiresTheCanonicalLowercaseDottedId()
    {
        var identity = new PluginIdentity("community.instance-health", "1.0.0");

        Assert.Equal("community.instance-health", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
        Assert.Throws<ArgumentException>(() => new PluginIdentity("Community.InstanceHealth", "1.0.0"));
        Assert.Throws<ArgumentException>(() => new PluginIdentity("community._health", "1.0.0"));
    }

    [Fact]
    public void AdapterMetadataIsNormalizedImmutableAndSelfConsistent()
    {
        var features = ImmutableArray.Create("event.publish", "instance.query", "rpc.register");
        var digest = new string('a', 64);

        var metadata = new PluginAdapterMetadata(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.Generated.DaemonPluginAdapter",
            "[2.0.0, 3.0.0)",
            features,
            digest);

        Assert.Equal("community.instance-health", metadata.PackageId);
        Assert.Equal(features, metadata.Features);
        Assert.Equal(digest, metadata.ManifestDigest);
        Assert.Throws<ArgumentException>(() => new PluginAdapterMetadata(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.Generated.DaemonPluginAdapter",
            "[2.0.0, 3.0.0)",
            default,
            digest));
        Assert.Throws<ArgumentException>(() => new PluginAdapterMetadata(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.Generated.DaemonPluginAdapter",
            "[2.0.0, 3.0.0)",
            ["rpc.register", "event.publish"],
            digest));
        Assert.Throws<ArgumentException>(() => new PluginAdapterMetadata(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.Generated.DaemonPluginAdapter",
            "[2.0.0, 3.0.0)",
            features,
            digest.ToUpperInvariant()));
    }

    [Fact]
    public void DescriptorFactoryAcceptsSourceGeneratedMetadata()
    {
        var descriptor = PluginProtocol.CreateRpc(
            "plugin.community.instance-health.rpc.get",
            "plugin.community.instance-health.rpc",
            PluginJsonContext.Default.EmptyRequest,
            PluginJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "health",
                "Get health",
                "Returns health.",
                "health.empty-request",
                "health.unit-result"));

        Assert.Equal(typeof(EmptyRequest), descriptor.RequestTypeInfo.Type);
        Assert.Equal(typeof(UnitResult), descriptor.ResultTypeInfo.Type);
        Assert.Equal("plugin.community.instance-health.rpc.get", descriptor.Method.Value);
        Assert.True(descriptor.RequestTypeInfo.IsReadOnly);
        Assert.True(descriptor.ResultTypeInfo.IsReadOnly);
        Assert.IsAssignableFrom<JsonSerializerContext>(descriptor.RequestTypeInfo.OriginatingResolver);
        Assert.IsAssignableFrom<JsonSerializerContext>(descriptor.ResultTypeInfo.OriginatingResolver);
        Assert.NotNull(descriptor.Documentation);
    }

    [Fact]
    public void DescriptorFactoryRejectsReflectionMetadataWithSourceGeneratedOptions()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = PluginJsonContext.Default };
        var reflectionTypeInfo = (JsonTypeInfo<EmptyRequest>)new DefaultJsonTypeInfoResolver()
            .GetTypeInfo(typeof(EmptyRequest), options);

        Assert.Same(PluginJsonContext.Default, reflectionTypeInfo.Options.TypeInfoResolver);
        Assert.IsType<DefaultJsonTypeInfoResolver>(reflectionTypeInfo.OriginatingResolver);

        var exception = Assert.Throws<ArgumentException>(() => PluginProtocol.CreateRpc(
            "plugin.community.instance-health.rpc.get",
            "plugin.community.instance-health.rpc",
            reflectionTypeInfo,
            PluginJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "health",
                "Get health",
                "Returns health.",
                "health.empty-request",
                "health.unit-result")));

        Assert.Equal("typeInfo", exception.ParamName);
    }

    [Fact]
    public void DescriptorFactoryRejectsMetadataWithoutAnOriginatingResolver()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = PluginJsonContext.Default };
        var typeInfo = JsonTypeInfo.CreateJsonTypeInfo<EmptyRequest>(options);

        Assert.Same(PluginJsonContext.Default, typeInfo.Options.TypeInfoResolver);
        Assert.Null(typeInfo.OriginatingResolver);

        var exception = Assert.Throws<ArgumentException>(() => PluginProtocol.CreateRpc(
            "plugin.community.instance-health.rpc.get",
            "plugin.community.instance-health.rpc",
            typeInfo,
            PluginJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "health",
                "Get health",
                "Returns health.",
                "health.empty-request",
                "health.unit-result")));

        Assert.Equal("typeInfo", exception.ParamName);
    }

    [Fact]
    public void DescriptorFactoryRejectsMissingDocumentation()
    {
        Assert.Throws<ArgumentNullException>(() => PluginProtocol.CreateRpc(
            "plugin.community.instance-health.rpc.get",
            "plugin.community.instance-health.rpc",
            PluginJsonContext.Default.EmptyRequest,
            PluginJsonContext.Default.UnitResult,
            documentation: null!));
    }

    [Fact]
    public void PluginIdentityAndErrorAreHostConstructed()
    {
        var identity = new PluginIdentity("community.instance-health", "1.0.0");
        var error = new PluginError(identity, "health.failed", "Health check failed.");

        Assert.Equal(identity, error.Identity);
        Assert.Equal("health.failed", error.Code);
        Assert.Equal(DaemonErrorKind.Internal, error.Kind);
        Assert.True(typeof(PluginError).IsSealed);
        Assert.Empty(typeof(PluginError).GetConstructors());
        Assert.Single(
            typeof(PluginIdentity).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            constructor => constructor.GetParameters().Length == 2);
        Assert.Null(typeof(PluginError).GetProperty("Stage"));
    }

    [Fact]
    public void PluginErrorFactoryBuildsDaemonResultsWithoutPluginErrorResultLeak()
    {
        var factory = new TestPluginErrorFactory(new PluginIdentity("community.instance-health", "1.0.0"));

        var unitResult = factory.Fail("health.failed", "Health check failed.");
        var typedResult = factory.Fail<UnitResult>("health.failed", "Health check failed.");

        Assert.True(unitResult.IsErr(out var unitError));
        Assert.True(typedResult.IsErr(out var typedError));
        Assert.IsType<PluginError>(unitError);
        Assert.IsType<PluginError>(typedError);
    }

    private sealed class TestPluginErrorFactory(PluginIdentity identity) : IPluginErrorFactory
    {
        public PluginError Create(string code, string message, JsonElement? details = null) =>
            new(identity, code, message, details);
    }
}

[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(UnitResult))]
internal partial class PluginJsonContext : JsonSerializerContext;
