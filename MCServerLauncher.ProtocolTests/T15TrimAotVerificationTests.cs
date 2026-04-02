#if !NO_DAEMON_REFS
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T15: trim/AOT/source-gen verification smoke tests.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class T15TrimAotVerificationTests
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

    private sealed record TrimAotUnknownPayload(string Value);
}
#endif
