using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Source-generated metadata for shared persistence models.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Converters =
    [
        typeof(InstanceTypeJsonConverter),
        typeof(TargetTypeJsonConverter),
        typeof(SourceTypeJsonConverter),
        typeof(InstanceFactoryMirrorJsonConverter)
    ])]
[JsonSerializable(typeof(InstanceConfig))]
[JsonSerializable(typeof(InstanceFactorySetting))]
public partial class PersistenceContext : JsonSerializerContext
{
}
