using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Collections.Generic;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Daemon-owned serializer boundary for RPC envelopes and payload tokens.
/// </summary>
public static class DaemonRpcJsonBoundary
{
    public static readonly JsonSerializerOptions StjOptions = CreateStjOptions();

    public static IJsonTypeInfoResolver CreateStjResolver(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var resolvers = new List<IJsonTypeInfoResolver>
        {
            StjResolver.CreateDefaultResolver(),
            DaemonRpcSerializerContext.Default
        };

        if (fallbackPolicy.ShouldEnableFallback())
            resolvers.Add(new DefaultJsonTypeInfoResolver());

        return JsonTypeInfoResolver.Combine(resolvers.ToArray());
    }

    public static bool UsesReflectionFallback(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        return fallbackPolicy.ShouldEnableFallback();
    }

    public static JsonSerializerOptions CreateStjOptions(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = CreateStjResolver(fallbackPolicy)
        };

        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
