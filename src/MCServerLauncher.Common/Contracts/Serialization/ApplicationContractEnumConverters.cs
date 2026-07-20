using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
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

internal sealed class EventMetaFilterKindJsonConverter()
    : JsonStringEnumConverter<EventMetaFilterKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class DaemonErrorWireKindJsonConverter()
    : JsonStringEnumConverter<DaemonErrorWireKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class UploadChunkAcknowledgementStatusJsonConverter()
    : JsonStringEnumConverter<UploadChunkAcknowledgementStatus>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class InstanceCatalogChangeOperationJsonConverter()
    : JsonStringEnumConverter<InstanceCatalogChangeOperation>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class OpenRpcEventFieldPresenceJsonConverter()
    : JsonStringEnumConverter<OpenRpcEventFieldPresence>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class BuiltInProtocolInstanceTypeJsonConverter()
    : JsonStringEnumConverter<InstanceType>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class BuiltInProtocolInstanceStatusJsonConverter()
    : JsonStringEnumConverter<InstanceStatus>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

internal sealed class ConsoleModeJsonConverter()
    : JsonStringEnumConverter<MCServerLauncher.Common.ProtoType.Instance.ConsoleMode>(JsonNamingPolicy.SnakeCaseLower);
