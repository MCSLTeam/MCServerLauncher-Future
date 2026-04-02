using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;

namespace MCServerLauncher.DaemonClient.Serialization;

/// <summary>
/// DaemonClient-owned serializer boundary for RPC envelopes and payload tokens.
/// </summary>
public static class DaemonClientRpcJsonBoundary
{
    public static readonly JsonSerializerOptions StjOptions = CreateStjOptions();

    public static IJsonTypeInfoResolver CreateStjResolver(
        DaemonClientStjReflectionFallbackPolicy fallbackPolicy = DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var resolvers = new List<IJsonTypeInfoResolver>
        {
            StjResolver.CreateDefaultResolver(),
            DaemonClientRpcSerializerContext.Default
        };

        if (fallbackPolicy.ShouldEnableFallback())
            resolvers.Add(new DefaultJsonTypeInfoResolver());

        return JsonTypeInfoResolver.Combine(resolvers.ToArray());
    }

    public static bool UsesReflectionFallback(
        DaemonClientStjReflectionFallbackPolicy fallbackPolicy = DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        return fallbackPolicy.ShouldEnableFallback();
    }

    public static JsonSerializerOptions CreateStjOptions(
        DaemonClientStjReflectionFallbackPolicy fallbackPolicy = DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault,
        bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = CreateStjResolver(fallbackPolicy),
            WriteIndented = writeIndented
        };

        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
