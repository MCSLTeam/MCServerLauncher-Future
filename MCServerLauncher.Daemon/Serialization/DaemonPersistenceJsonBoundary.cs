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
/// Daemon-owned serializer boundary for daemon-managed persistence JSON.
/// </summary>
public static class DaemonPersistenceJsonBoundary
{
    public static readonly JsonSerializerOptions StjOptions = CreateStjOptions();
    public static readonly JsonSerializerOptions StjWriteIndentedOptions = CreateStjOptions(writeIndented: true);

    public static IJsonTypeInfoResolver CreateStjResolver(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        var resolvers = new List<IJsonTypeInfoResolver>
        {
            DaemonPersistenceSerializerContext.Default,
            StjResolver.CreateDefaultResolver()
        };

        var reflectionFallbackResolver = JsonSerializer.IsReflectionEnabledByDefault
            ? fallbackPolicy.ShouldEnableFallback() ? CreateReflectionFallbackResolver() : null
            : null;

        if (reflectionFallbackResolver is not null)
            resolvers.Add(reflectionFallbackResolver);

        return JsonTypeInfoResolver.Combine(resolvers.ToArray());
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Persistence reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Persistence reflection fallback is an explicit compatibility path guarded by JsonSerializer.IsReflectionEnabledByDefault and the boundary fallback policy.")]
    private static DefaultJsonTypeInfoResolver CreateReflectionFallbackResolver() => new();

    public static bool UsesReflectionFallback(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault)
    {
        return JsonSerializer.IsReflectionEnabledByDefault && fallbackPolicy.ShouldEnableFallback();
    }

    public static JsonSerializerOptions CreateStjOptions(
        DaemonStjReflectionFallbackPolicy fallbackPolicy = DaemonStjReflectionFallbackPolicy.TrimFriendlyDefault,
        bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = CreateStjResolver(fallbackPolicy),
            IncludeFields = true,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());
        options.Converters.Add(new PermissionStjConverter());
        return options;
    }
}
