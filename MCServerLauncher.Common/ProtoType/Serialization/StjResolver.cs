using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Composition point for STJ type resolvers - allows later runtime paths to extend without modifying JsonSettings
/// </summary>
public static class StjResolver
{
    /// <summary>
    /// Creates a combined resolver with all Common-side STJ contexts
    /// </summary>
    public static IJsonTypeInfoResolver CreateDefaultResolver()
    {
        return JsonTypeInfoResolver.Combine(
            RpcEnvelopeContext.Default,
            ActionParametersContext.Default,
            ActionResultsContext.Default,
            EventDataContext.Default,
            PersistenceContext.Default
        );
    }

    /// <summary>
    /// Creates JsonSerializerOptions with default resolver and converters
    /// </summary>
    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = CreateDefaultResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());

        return options;
    }
}
