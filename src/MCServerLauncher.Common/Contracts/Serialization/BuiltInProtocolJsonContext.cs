using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Common.Contracts.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    AllowDuplicateProperties = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
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
[JsonSerializable(typeof(JsonRpcRequestId))]
[JsonSerializable(typeof(JsonRpcObjectPayload))]
[JsonSerializable(typeof(JsonRpcRequestEnvelope))]
[JsonSerializable(typeof(JsonRpcSuccessResponseEnvelope))]
[JsonSerializable(typeof(JsonRpcErrorResponseEnvelope))]
[JsonSerializable(typeof(JsonRpcOptionalPayload))]
[JsonSerializable(typeof(JsonRpcRemoteEventParameters))]
[JsonSerializable(typeof(JsonRpcRemoteEventNotification))]
[JsonSerializable(typeof(JsonRpcUploadAcknowledgementNotification))]
public partial class BuiltInProtocolJsonContext : JsonSerializerContext;
