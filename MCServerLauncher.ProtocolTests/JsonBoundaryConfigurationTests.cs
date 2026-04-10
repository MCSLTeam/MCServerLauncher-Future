#if !NO_DAEMON_REFS
using System;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.ProtocolTests.Helpers;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Verify serializer-boundary ownership scaffolding and trim-friendly fallback policy behavior.
/// </summary>
[Collection("RuntimeSwitchIsolation")]
public class JsonBoundaryConfigurationTests
{
    [Fact]
    public void DaemonRpcBoundary_ProvidesNewtonsoftAndStjOptions()
    {
        Assert.NotNull(DaemonRpcJsonBoundary.StjOptions);
        Assert.NotNull(DaemonRpcJsonBoundary.StjOptions.TypeInfoResolver);
    }

    [Fact]
    public void DaemonRpcBoundary_StjOptions_AreOwnedByCommonContexts()
    {
        var options = DaemonRpcJsonBoundary.StjOptions;

        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
        Assert.NotNull(options.GetTypeInfo(typeof(ActionRequest)));
        Assert.NotNull(options.GetTypeInfo(typeof(ActionResponse)));
        Assert.NotNull(options.GetTypeInfo(typeof(EventPacket)));
    }

    [Fact]
    public void DaemonPersistenceBoundary_ProvidesNewtonsoftAndStjOptions()
    {
        Assert.NotNull(DaemonPersistenceJsonBoundary.StjOptions);
        Assert.NotNull(DaemonPersistenceJsonBoundary.StjOptions.TypeInfoResolver);
    }

    [Fact]
    public void DaemonPersistenceBoundary_StjOptions_AreOwnedByCommonContexts()
    {
        var options = DaemonPersistenceJsonBoundary.StjOptions;

        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
        Assert.NotNull(options.GetTypeInfo(typeof(InstanceConfig)));
        Assert.NotNull(options.GetTypeInfo(typeof(InstanceFactorySetting)));
    }

    [Fact]
    public void DaemonClientRpcBoundary_ProvidesNewtonsoftAndStjOptions()
    {
        Assert.NotNull(DaemonClientRpcJsonBoundary.StjOptions);
        Assert.NotNull(DaemonClientRpcJsonBoundary.StjOptions.TypeInfoResolver);
    }

    [Fact]
    public void DaemonClientRpcBoundary_StjOptions_AreOwnedByCommonContexts()
    {
        var options = DaemonClientRpcJsonBoundary.StjOptions;

        Assert.Equal(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
        Assert.NotNull(options.GetTypeInfo(typeof(ActionRequest)));
        Assert.NotNull(options.GetTypeInfo(typeof(ActionResponse)));
        Assert.NotNull(options.GetTypeInfo(typeof(EventPacket)));
    }

    [Fact]
    public void DaemonRpcBoundary_FallbackPolicy_DisabledHasResolver()
    {
        var resolver = DaemonRpcJsonBoundary.CreateStjResolver(DaemonStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);
    }

    [Fact]
    public void DaemonPersistenceBoundary_FallbackPolicy_DisabledHasResolver()
    {
        var resolver = DaemonPersistenceJsonBoundary.CreateStjResolver(DaemonStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);
    }

    [Fact]
    public void DaemonClientRpcBoundary_FallbackPolicy_DisabledHasResolver()
    {
        var resolver = DaemonClientRpcJsonBoundary.CreateStjResolver(DaemonClientStjReflectionFallbackPolicy.Disabled);
        Assert.NotNull(resolver);
    }

    [Fact]
    public void DisabledFallback_DoesNotSerializeUnknownTypes()
    {
        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);

        var ex = Assert.Throws<NotSupportedException>(() =>
            JsonSerializer.Serialize(new UnknownBoundaryPayload("fallback-off"), options));

        Assert.Contains(nameof(UnknownBoundaryPayload), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledFallback_SerializesUnknownTypes()
    {
        var options = DaemonRpcJsonBoundary.CreateStjOptions(DaemonStjReflectionFallbackPolicy.Enabled);
        var json = JsonSerializer.Serialize(new UnknownBoundaryPayload("fallback-on"), options);

        Assert.Contains("\"wire_value\":\"fallback-on\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TrimFriendlyPolicyTracksRuntimeReflectionSwitch_DefaultOnWhenUnset()
    {
        const string key = "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault";
        var hadPrevious = AppContext.TryGetSwitch(key, out var previous);

        try
        {
            AppContext.SetData(key, null);
            Assert.True(DaemonRpcJsonBoundary.UsesReflectionFallback());
            Assert.True(DaemonPersistenceJsonBoundary.UsesReflectionFallback());
            Assert.True(DaemonClientRpcJsonBoundary.UsesReflectionFallback());
        }
        finally
        {
            if (hadPrevious)
                AppContext.SetSwitch(key, previous);
            else
                AppContext.SetData(key, null);
        }
    }

    [Fact]
    public void TrimFriendlyPolicyTracksRuntimeReflectionSwitch_RespectsOff()
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
            if (hadPrevious)
                AppContext.SetSwitch(key, previous);
            else
                AppContext.SetData(key, null);
        }
    }

    private sealed record UnknownBoundaryPayload(string WireValue);
}
#endif
