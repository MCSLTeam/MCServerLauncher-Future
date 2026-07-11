using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Serialization;

internal sealed class InstanceTypeJsonConverter()
    : JsonStringEnumConverter<InstanceType>(JsonNamingPolicy.SnakeCaseLower);

internal sealed class TargetTypeJsonConverter()
    : JsonStringEnumConverter<TargetType>(JsonNamingPolicy.SnakeCaseLower);

internal sealed class SourceTypeJsonConverter()
    : JsonStringEnumConverter<SourceType>(JsonNamingPolicy.SnakeCaseLower);

internal sealed class InstanceFactoryMirrorJsonConverter()
    : JsonStringEnumConverter<InstanceFactoryMirror>(JsonNamingPolicy.SnakeCaseLower);

internal sealed class InstanceStatusJsonConverter()
    : JsonStringEnumConverter<InstanceStatus>(JsonNamingPolicy.SnakeCaseLower);
