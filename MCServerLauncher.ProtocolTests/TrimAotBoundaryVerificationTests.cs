#if !NO_DAEMON_REFS
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Trim/AOT/source-gen verification smoke tests.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class TrimAotBoundaryVerificationTests
{
    [Fact]
    [Trait("Category", "TrimAot")]
    public void ReflectionDisabledSwitch_DisablesTrimFriendlyFallbackAcrossBoundaries()
    {
        const string key = "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault";
        var hadPrevious = AppContext.TryGetSwitch(key, out var previous);

        try
        {
            AppContext.SetSwitch(key, false);

            Assert.False(DaemonRpcJsonBoundary.UsesReflectionFallback());
            Assert.False(DaemonPersistenceJsonBoundary.UsesReflectionFallback());
            Assert.False(DaemonClientRpcJsonBoundary.UsesReflectionFallback());
        }
        finally
        {
            RestoreAppContextSwitch(key, hadPrevious, previous);
        }
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void RpcBoundary_DisabledFallback_RoundTripsKnownEnvelopeWithoutReflection()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };

        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);
        var json = JsonSerializer.Serialize(request, options);
        var parsed = JsonSerializer.Deserialize<ActionRequest>(json, options);

        Assert.NotNull(parsed);
        Assert.Equal(ActionType.Ping, parsed!.ActionType);
        Assert.True(parsed.Parameter.HasValue);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), parsed.Id);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void DaemonClientBoundary_DisabledFallback_RoundTripsKnownEnvelopeWithoutReflection()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = ParseJsonElement("{\"time\":1717171717171}"),
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444")
        };

        var options = DaemonClientRpcJsonBoundary.CreateStjOptions(DaemonClientStjReflectionFallbackPolicy.Disabled);
        var json = JsonSerializer.Serialize(response, options);
        var parsed = JsonSerializer.Deserialize<ActionResponse>(json, options);

        Assert.NotNull(parsed);
        Assert.Equal(ActionRequestStatus.Ok, parsed!.RequestStatus);
        Assert.True(parsed.Data.HasValue);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), parsed.Id);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void PersistenceBoundary_DisabledFallback_RoundTripsKnownPersistenceTypeWithoutReflection()
    {
        var config = new InstanceConfig
        {
            Name = "trim-aot-instance",
            Target = "server.jar",
            InstanceType = InstanceType.MCJava,
            TargetType = TargetType.Jar
        };

        var options = DaemonPersistenceJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);
        var uuidField = typeof(InstanceConfig).GetField(nameof(InstanceConfig.Uuid), BindingFlags.Public | BindingFlags.Instance);
        var uuidProperty = typeof(InstanceConfig).GetProperty(nameof(InstanceConfig.Uuid), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(uuidField);
        Assert.True(uuidField!.IsPublic);
        Assert.Null(uuidProperty);

        var json = JsonSerializer.Serialize(config, options);
        var parsed = JsonSerializer.Deserialize<InstanceConfig>(json, options);

        Assert.NotNull(parsed);
        Assert.Equal("trim-aot-instance", parsed!.Name);
        Assert.Equal("server.jar", parsed.Target);
        Assert.Equal(InstanceType.MCJava, parsed.InstanceType);
        Assert.Equal(TargetType.Jar, parsed.TargetType);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void PersistenceBoundary_DisabledFallback_RoundTripsAppConfigWithoutReflection()
    {
        var appConfigJson = """
            {
              "port": 11452,
              "secret": "0123456789abcdef0123456789abcdef",
              "main_token": "fedcba9876543210fedcba9876543210",
              "file_download_sessions": 5,
              "verbose": true
            }
            """;

        var options = DaemonPersistenceJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);
        var appConfigType = typeof(AppConfig);
        var typeInfo = options.TypeInfoResolver!.GetTypeInfo(appConfigType, options);
        var parsed = JsonSerializer.Deserialize(appConfigJson, appConfigType, options);

        Assert.NotNull(typeInfo);
        Assert.NotNull(parsed);

        var json = JsonSerializer.Serialize(parsed, appConfigType, options);
        var roundTripped = JsonSerializer.Deserialize(json, appConfigType, options);

        Assert.NotNull(roundTripped);
        Assert.Equal(json, JsonSerializer.Serialize(roundTripped, appConfigType, options));
        Assert.Contains("\"verbose\":true", json);
        Assert.Contains("\"main_token\":\"fedcba9876543210fedcba9876543210\"", json);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void RpcBoundary_DisabledFallback_RejectsUnknownEventRuleDiscriminatorWithoutReflection()
    {
        var fixture = FixtureHarness.LoadFixture(Path.Combine(
            MCServerLauncher.ProtocolTests.Fixtures.Persistence.PersistenceFixturePaths.EventRuleDir,
            "unknown-trigger-discriminator-event-rule.json"));

        var options = DaemonPersistenceJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);
        var ex = Assert.Throws<System.Text.Json.JsonException>(() =>
            JsonSerializer.Deserialize<EventRule>(fixture.GetRawText(), options));

        Assert.Contains("Unknown TriggerDefinition discriminator 'FutureTrigger'", ex.Message);
        Assert.Contains("Known values: ConsoleOutput, Schedule, InstanceStatus", ex.Message);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void RpcBoundary_DisabledFallback_ThrowsForUnknownTypeThatNeedsReflectionResolver()
    {
        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);

        var ex = Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize(new TrimAotUnknownPayload("trim-aot"), options));

        Assert.Contains(nameof(TrimAotUnknownPayload), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "TrimAot")]
    public void RpcBoundary_EnabledFallback_SerializesUnknownTypeViaReflectionResolver()
    {
        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Enabled);
        var json = JsonSerializer.Serialize(new TrimAotUnknownPayload("trim-aot"), options);

        Assert.Contains("trim-aot", json, StringComparison.Ordinal);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static void RestoreAppContextSwitch(string key, bool hadPrevious, bool previous)
    {
        if (hadPrevious)
            AppContext.SetSwitch(key, previous);
        else
            AppContext.SetData(key, null);
    }


    [Fact]
    [Trait("Category", "TrimAot")]
    public void TypeInfoCache_RejectsNonSourceGenType_Explicitly()
    {
        // The cache must NOT silently resolve unregistered types via ambient reflection fallback.
        // Only types explicitly registered in Common or Daemon serializer contexts should be resolvable.
        var ex = Assert.Throws<NotSupportedException>(() =>
        {
            _ = DaemonRpcTypeInfoCache<NonSourceGenEnvelope>.TypeInfo;
        });

        Assert.Contains(nameof(NonSourceGenEnvelope), ex.Message, StringComparison.Ordinal);
    }

    private sealed record TrimAotUnknownPayload(string Value);
    private sealed record NonSourceGenEnvelope(string Payload);
    }
#endif
