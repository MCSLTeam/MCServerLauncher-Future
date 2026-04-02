using System.Text.Json.Serialization;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Common.ProtoType.Files;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// STJ source-generation context for RPC envelope types (ActionRequest, ActionResponse, EventPacket)
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ActionRequest))]
[JsonSerializable(typeof(ActionResponse))]
[JsonSerializable(typeof(EventPacket))]
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
/// STJ source-generation context for event data types
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstanceLogEventData))]
[JsonSerializable(typeof(DaemonReportEventData))]
public partial class EventDataContext : JsonSerializerContext
{
}

/// <summary>
/// STJ source-generation context for persistence types (InstanceConfig, InstanceFactorySetting)
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(InstanceConfig))]
[JsonSerializable(typeof(InstanceFactorySetting))]
public partial class PersistenceContext : JsonSerializerContext
{
}
