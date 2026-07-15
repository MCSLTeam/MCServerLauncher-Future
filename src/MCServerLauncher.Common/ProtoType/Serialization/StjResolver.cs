using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Serialization;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Composes the explicit Common-owned source-generated JSON contexts.
/// </summary>
public static class StjResolver
{
    public static IJsonTypeInfoResolver CreateDefaultResolver()
    {
        return JsonTypeInfoResolver.Combine(
            ApplicationContractJsonContext.Default,
            BuiltInProtocolJsonContext.Default,
            PersistenceContext.Default);
    }

    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = CreateDefaultResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        options.Converters.Add(new GuidStjConverter());
        options.Converters.Add(new EncodingStjConverter());
        options.Converters.Add(new PlaceHolderStringStjConverter());
        options.Converters.Add(new InstanceTypeJsonConverter());
        options.Converters.Add(new TargetTypeJsonConverter());
        options.Converters.Add(new SourceTypeJsonConverter());
        options.Converters.Add(new InstanceFactoryMirrorJsonConverter());
        return options;
    }
}
