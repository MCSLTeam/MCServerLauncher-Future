using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    ///     STJ options with reflection fallback explicitly disabled.
    ///     Used by <see cref="DaemonClientRpcTypeInfoCache{T}" /> to guarantee that only types
    ///     registered in Common or DaemonClient source-generated contexts are resolvable.
    ///     Unknown types throw <see cref="NotSupportedException" /> rather than silently
    ///     falling back to reflection.
    /// </summary>
    public static readonly JsonSerializerOptions SourceGenStjOptions =
        CreateStjOptions(DaemonClientStjReflectionFallbackPolicy.Disabled);

    public static IJsonTypeInfoResolver CreateStjResolver(
        DaemonClientStjReflectionFallbackPolicy fallbackPolicy = DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var resolvers = new List<IJsonTypeInfoResolver>
        {
            StjResolver.CreateDefaultResolver(),
            DaemonClientRpcSerializerContext.Default
        };

        var reflectionFallbackResolver = JsonSerializer.IsReflectionEnabledByDefault
            ? fallbackPolicy.ShouldEnableFallback() ? CreateReflectionFallbackResolver() : null
            : null;

        if (reflectionFallbackResolver is not null)
            resolvers.Add(reflectionFallbackResolver);
        return JsonTypeInfoResolver.Combine(resolvers.ToArray());
    }

    public static bool UsesReflectionFallback(
        DaemonClientStjReflectionFallbackPolicy fallbackPolicy = DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        return JsonSerializer.IsReflectionEnabledByDefault && fallbackPolicy.ShouldEnableFallback();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "RPC reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "RPC reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    private static DefaultJsonTypeInfoResolver CreateReflectionFallbackResolver() => new();

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
