using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Collections.Generic;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Serialization;

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
        return options;
    }
}
