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
    public void GeneratedMetadataAttributeCarriesOnlyStringValues()
    {
        const string features = "event.publish\ninstance.query\nrpc.register";
        var digest = new string('a', 64);

        var metadata = new GeneratedDaemonPluginMetadataAttribute(
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
        Assert.All(
            typeof(GeneratedDaemonPluginMetadataAttribute).GetConstructors().Single().GetParameters(),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType));
        var usage = Assert.Single(
            typeof(GeneratedDaemonPluginMetadataAttribute)
                .GetCustomAttributes<AttributeUsageAttribute>());
        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    [Fact]
    public void DescriptorFactoryAcceptsSourceGeneratedMetadata()
    {
        var descriptor = PluginProtocol.CreateRpc(
            "community.instance-health",
            "get",
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
        Assert.Equal(descriptor.Method.Value, descriptor.Permission.Value);
        Assert.True(descriptor.RequestTypeInfo.IsReadOnly);
        Assert.True(descriptor.ResultTypeInfo.IsReadOnly);
        Assert.IsAssignableFrom<JsonSerializerContext>(descriptor.RequestTypeInfo.OriginatingResolver);
        Assert.IsAssignableFrom<JsonSerializerContext>(descriptor.ResultTypeInfo.OriginatingResolver);
        Assert.NotNull(descriptor.Documentation);
    }

    [Theory]
    [InlineData("plugin.other.rpc.get")]
    [InlineData("mcsl.instance.start")]
    public void DescriptorFactoryRejectsAbsoluteMethodNames(string method)
    {
        var exception = Assert.Throws<ArgumentException>(() => PluginProtocol.CreateRpc(
            "community.instance-health",
            method,
            PluginJsonContext.Default.EmptyRequest,
            PluginJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "health",
                "Get health",
                "Returns health.",
                "health.empty-request",
                "health.unit-result")));

        Assert.Equal("relativeMethod", exception.ParamName);
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
            "community.instance-health",
            "get",
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
            "community.instance-health",
            "get",
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
            "community.instance-health",
            "get",
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
    public void VerifiedPrincipalIsOpaqueReadOnlyAndRejectsReservedNonMainIdentity()
    {
        Assert.True(typeof(VerifiedPrincipal).IsSealed);
        Assert.Empty(typeof(VerifiedPrincipal).GetConstructors());
        Assert.All(
            typeof(VerifiedPrincipal).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => Assert.Null(property.SetMethod));

        var principal = new VerifiedPrincipal(
            "external-user",
            "token-id",
            "issuer",
            "audience",
            DateTimeOffset.MaxValue,
            ImmutableArray.Create("mcsl.instance.catalog.get"),
            isMainToken: false);
        Assert.Equal("external-user", principal.Subject);
        Assert.False(principal.IsMainToken);

        foreach (var reserved in new[] { "*", "daemon-main", "plugin:community.owner" })
        {
            Assert.Throws<ArgumentException>(() => new VerifiedPrincipal(
                reserved,
                "token-id",
                "issuer",
                "audience",
                DateTimeOffset.MaxValue,
                ImmutableArray.Create("mcsl.instance.catalog.get"),
                isMainToken: false));
        }

        Assert.Throws<ArgumentException>(() => new VerifiedPrincipal(
            "external-user",
            "token-id",
            "issuer",
            "audience",
            DateTimeOffset.MaxValue,
            ImmutableArray.Create("*"),
            isMainToken: true));
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
