using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Common.Contracts.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    Converters =
    [
        typeof(BuiltInProtocolInstanceTypeJsonConverter),
        typeof(BuiltInProtocolInstanceStatusJsonConverter),
        typeof(EventMetaFilterKindJsonConverter),
        typeof(UploadChunkAcknowledgementStatusJsonConverter),
        typeof(InstanceCatalogChangeOperationJsonConverter),
        typeof(OpenRpcEventFieldPresenceJsonConverter)
    ])]
[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(UnitResult))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(PermissionsResult))]
[JsonSerializable(typeof(EventSubscriptionRequest))]
[JsonSerializable(typeof(JsonRpcErrorObject))]
[JsonSerializable(typeof(JsonRpcErrorData))]
[JsonSerializable(typeof(ProtocolOwnerIdentity))]
[JsonSerializable(typeof(UploadChunkAcknowledgement))]
[JsonSerializable(typeof(FileSessionReference))]
[JsonSerializable(typeof(DownloadReadResult))]
[JsonSerializable(typeof(InstanceCatalogItem))]
[JsonSerializable(typeof(InstanceCatalogResult))]
[JsonSerializable(typeof(InstanceCatalogChangedEventData))]
[JsonSerializable(typeof(InstanceLogEventMeta))]
[JsonSerializable(typeof(InstanceLogEventData))]
[JsonSerializable(typeof(DaemonReportEventData))]
[JsonSerializable(typeof(NotificationEventMeta))]
[JsonSerializable(typeof(NotificationEventData))]
[JsonSerializable(typeof(OpenRpcInfo))]
[JsonSerializable(typeof(OpenRpcContentDescriptor))]
[JsonSerializable(typeof(OpenRpcMethod))]
[JsonSerializable(typeof(OpenRpcEventField))]
[JsonSerializable(typeof(OpenRpcEvent))]
[JsonSerializable(typeof(OpenRpcDocument))]
public partial class BuiltInProtocolJsonContext : JsonSerializerContext;
