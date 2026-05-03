using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Common.ProtoType.Files;

namespace MCServerLauncher.Common.ProtoType.Serialization;

// Canonical wire-contract source-generation owner.
// All contexts defined here are the single source of truth for STJ metadata used by
// daemon/client RPC boundaries. Daemon/client boundaries consume Common contexts first
// and keep only their local additions in project-local serializer contexts.
/// <summary>
/// RPC envelope types — the top-level wire packets exchanged between daemon and client.
/// Common is the canonical source-generated metadata owner for these types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ActionRequest))]
[JsonSerializable(typeof(ActionResponse))]
[JsonSerializable(typeof(EventPacket))]
[JsonSerializable(typeof(EventPacket[]))]
public partial class RpcEnvelopeContext : JsonSerializerContext
{
}

/// <summary>
/// STJ source-generation context for typed action parameters
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(EmptyActionParameter))]
[JsonSerializable(typeof(SubscribeEventParameter))]
[JsonSerializable(typeof(UnsubscribeEventParameter))]
[JsonSerializable(typeof(FileUploadRequestParameter))]
[JsonSerializable(typeof(FileUploadChunkParameter))]
[JsonSerializable(typeof(FileUploadCancelParameter))]
[JsonSerializable(typeof(FileDownloadRequestParameter))]
[JsonSerializable(typeof(FileDownloadRangeParameter))]
[JsonSerializable(typeof(FileDownloadCloseParameter))]
[JsonSerializable(typeof(GetFileInfoParameter))]
[JsonSerializable(typeof(GetDirectoryInfoParameter))]
[JsonSerializable(typeof(DeleteFileParameter))]
[JsonSerializable(typeof(DeleteDirectoryParameter))]
[JsonSerializable(typeof(RenameFileParameter))]
[JsonSerializable(typeof(RenameDirectoryParameter))]
[JsonSerializable(typeof(CreateDirectoryParameter))]
[JsonSerializable(typeof(MoveFileParameter))]
[JsonSerializable(typeof(MoveDirectoryParameter))]
[JsonSerializable(typeof(CopyFileParameter))]
[JsonSerializable(typeof(CopyDirectoryParameter))]
[JsonSerializable(typeof(AddInstanceParameter))]
[JsonSerializable(typeof(RemoveInstanceParameter))]
[JsonSerializable(typeof(StartInstanceParameter))]
[JsonSerializable(typeof(StopInstanceParameter))]
[JsonSerializable(typeof(SendToInstanceParameter))]
[JsonSerializable(typeof(KillInstanceParameter))]
[JsonSerializable(typeof(GetInstanceReportParameter))]
[JsonSerializable(typeof(GetInstanceLogHistoryParameter))]
[JsonSerializable(typeof(GetEventRulesParameter))]
[JsonSerializable(typeof(SaveEventRulesParameter))]
public partial class ActionParametersContext : JsonSerializerContext
{
}

/// <summary>
/// STJ source-generation context for typed action results
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(EmptyActionResult))]
[JsonSerializable(typeof(GetPermissionsResult))]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(GetJavaListResult))]
[JsonSerializable(typeof(FileUploadRequestResult))]
[JsonSerializable(typeof(FileUploadChunkResult))]
[JsonSerializable(typeof(FileDownloadRequestResult))]
[JsonSerializable(typeof(FileDownloadRangeResult))]
[JsonSerializable(typeof(GetFileInfoResult))]
[JsonSerializable(typeof(GetDirectoryInfoResult))]
[JsonSerializable(typeof(AddInstanceResult))]
[JsonSerializable(typeof(GetInstanceReportResult))]
[JsonSerializable(typeof(GetInstanceLogHistoryResult))]
[JsonSerializable(typeof(GetAllReportsResult))]
[JsonSerializable(typeof(GetSystemInfoResult))]
[JsonSerializable(typeof(GetEventRulesResult))]
public partial class ActionResultsContext : JsonSerializerContext
{
}

/// <summary>
/// Event data and meta types — used in EventPacket payloads on the RPC wire path.
/// Common is the canonical source-generated metadata owner for these types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstanceLogEventData))]
[JsonSerializable(typeof(DaemonReportEventData))]
[JsonSerializable(typeof(InstanceLogEventMeta))]
public partial class EventDataContext : JsonSerializerContext
{
}

/// <summary>
/// Persistence types (InstanceConfig, InstanceFactorySetting) — used by daemon persistence paths.
/// Shared between daemon persistence and instance management; not wire-facing RPC types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstanceConfig))]
[JsonSerializable(typeof(InstanceFactorySetting))]
public partial class PersistenceContext : JsonSerializerContext
{
}
