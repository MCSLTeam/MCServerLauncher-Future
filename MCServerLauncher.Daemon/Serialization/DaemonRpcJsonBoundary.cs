using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Collections.Generic;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Serialization;

/// <summary>
/// Daemon-owned serializer boundary for RPC envelopes and payload tokens.
/// Composition: Common-owned wire-contract contexts are consumed first via
/// <see cref="StjResolver.CreateDefaultResolver()"/>, followed by daemon-local
/// additions (<see cref="DaemonRpcSerializerContext"/>). Reflection fallback is
/// switch-controlled and never the normal path.
/// </summary>
public static class DaemonRpcJsonBoundary
{
    public static readonly JsonSerializerOptions StjOptions = CreateStjOptions();

    /// <summary>
    /// STJ options with reflection fallback explicitly disabled.
    /// Used by <see cref="DaemonRpcTypeInfoCache{T}"/> to guarantee that only types
    /// registered in Common or Daemon source-generated contexts are resolvable.
    /// Unknown types throw <see cref="NotSupportedException"/> rather than silently
    /// falling back to reflection.
    /// </summary>
    public static readonly JsonSerializerOptions SourceGenStjOptions =
        CreateStjOptions(DaemonStjReflectionFallbackPolicy.Disabled);

    public static IJsonTypeInfoResolver CreateStjResolver(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var resolvers = new List<IJsonTypeInfoResolver>
        {
            // 1. Common-owned wire-contract contexts (envelopes, parameters, results, event types)
            StjResolver.CreateDefaultResolver(),
            // 2. Daemon-local additions (ActionError, Permission, etc.)
            DaemonRpcSerializerContext.Default
        };

        var reflectionFallbackResolver = JsonSerializer.IsReflectionEnabledByDefault
            ? fallbackPolicy.ShouldEnableFallback() ? CreateReflectionFallbackResolver() : null
            : null;

        if (reflectionFallbackResolver is not null)
            resolvers.Add(reflectionFallbackResolver);

        return JsonTypeInfoResolver.Combine(resolvers.ToArray());
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "RPC reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "RPC reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    private static DefaultJsonTypeInfoResolver CreateReflectionFallbackResolver() => new();

    public static bool UsesReflectionFallback(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        return JsonSerializer.IsReflectionEnabledByDefault && fallbackPolicy.ShouldEnableFallback();
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
