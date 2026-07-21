using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.DaemonClient.Protocol;

internal static class V2ClientProtocol
{
    internal static RpcDescriptor<EmptyRequest, PermissionsResult> GetAuthPermissions => BuiltInProtocolDefinitions.GetAuthPermissions;
    internal static RpcDescriptor<EmptyRequest, PingResult> PingDaemon => BuiltInProtocolDefinitions.PingDaemon;
    internal static RpcDescriptor<PathTransferRequest, UnitResult> CopyDirectory => BuiltInProtocolDefinitions.CopyDirectory;
    internal static RpcDescriptor<PathRequest, UnitResult> CreateDirectory => BuiltInProtocolDefinitions.CreateDirectory;
    internal static RpcDescriptor<DeleteDirectoryRequest, UnitResult> DeleteDirectory => BuiltInProtocolDefinitions.DeleteDirectory;
    internal static RpcDescriptor<PathRequest, DirectoryDetails> GetDirectoryInfo => BuiltInProtocolDefinitions.GetDirectoryInfo;
    internal static RpcDescriptor<PathTransferRequest, UnitResult> MoveDirectory => BuiltInProtocolDefinitions.MoveDirectory;
    internal static RpcDescriptor<PathRenameRequest, UnitResult> RenameDirectory => BuiltInProtocolDefinitions.RenameDirectory;
    internal static RpcDescriptor<EventSubscriptionRequest, UnitResult> SubscribeEvent => BuiltInProtocolDefinitions.SubscribeEvent;
    internal static RpcDescriptor<EventSubscriptionRequest, UnitResult> UnsubscribeEvent => BuiltInProtocolDefinitions.UnsubscribeEvent;
    internal static RpcDescriptor<PathTransferRequest, UnitResult> CopyFile => BuiltInProtocolDefinitions.CopyFile;
    internal static RpcDescriptor<PathRequest, UnitResult> DeleteFile => BuiltInProtocolDefinitions.DeleteFile;
    internal static RpcDescriptor<FileSessionReference, UnitResult> CloseDownload => BuiltInProtocolDefinitions.CloseDownload;
    internal static RpcDescriptor<DownloadOpenRequest, DownloadSession> OpenDownload => BuiltInProtocolDefinitions.OpenDownload;
    internal static RpcDescriptor<DownloadChunkRequest, DownloadReadResult> ReadDownload => BuiltInProtocolDefinitions.ReadDownload;
    internal static RpcDescriptor<PathRequest, FileDetails> GetFileInfo => BuiltInProtocolDefinitions.GetFileInfo;
    internal static RpcDescriptor<PathTransferRequest, UnitResult> MoveFile => BuiltInProtocolDefinitions.MoveFile;
    internal static RpcDescriptor<PathRenameRequest, UnitResult> RenameFile => BuiltInProtocolDefinitions.RenameFile;
    internal static RpcDescriptor<FileSessionReference, UnitResult> CancelUpload => BuiltInProtocolDefinitions.CancelUpload;
    internal static RpcDescriptor<FileSessionReference, UnitResult> CloseUpload => BuiltInProtocolDefinitions.CloseUpload;
    internal static RpcDescriptor<UploadOpenRequest, UploadSession> OpenUpload => BuiltInProtocolDefinitions.OpenUpload;
    internal static RpcDescriptor<EmptyRequest, InstanceCatalogResult> GetInstanceCatalog => BuiltInProtocolDefinitions.GetInstanceCatalog;
    internal static RpcDescriptor<InstanceCommandRequest, UnitResult> SendInstanceCommand => BuiltInProtocolDefinitions.SendInstanceCommand;
    internal static RpcDescriptor<ConsoleOpenRequest, ConsoleSession> OpenConsole => BuiltInProtocolDefinitions.OpenConsole;
    internal static RpcDescriptor<ConsoleResizeRequest, UnitResult> ResizeConsole => BuiltInProtocolDefinitions.ResizeConsole;
    internal static RpcDescriptor<ConsoleSessionReference, UnitResult> CloseConsole => BuiltInProtocolDefinitions.CloseConsole;
    internal static RpcDescriptor<CreateInstanceRequest, CreateInstanceResult> CreateInstance => BuiltInProtocolDefinitions.CreateInstance;
    internal static RpcDescriptor<EventRuleQuery, EventRuleSet> GetInstanceEventRules => BuiltInProtocolDefinitions.GetInstanceEventRules;
    internal static RpcDescriptor<EventRuleUpdateRequest, UnitResult> UpdateInstanceEventRules => BuiltInProtocolDefinitions.UpdateInstanceEventRules;
    internal static RpcDescriptor<InstanceReference, UnitResult> HaltInstance => BuiltInProtocolDefinitions.HaltInstance;
    internal static RpcDescriptor<InstanceLogQuery, InstanceLogResult> GetInstanceLog => BuiltInProtocolDefinitions.GetInstanceLog;
    internal static RpcDescriptor<InstanceReference, UnitResult> RemoveInstance => BuiltInProtocolDefinitions.RemoveInstance;
    internal static RpcDescriptor<InstanceReference, InstanceReport> GetInstanceReport => BuiltInProtocolDefinitions.GetInstanceReport;
    internal static RpcDescriptor<EmptyRequest, InstanceReportList> ListInstanceReports => BuiltInProtocolDefinitions.ListInstanceReports;
    internal static RpcDescriptor<InstanceReference, InstanceSettingsResult> GetInstanceSettings => BuiltInProtocolDefinitions.GetInstanceSettings;
    internal static RpcDescriptor<UpdateInstanceSettingsRequest, UpdateInstanceSettingsResult> UpdateInstanceSettings => BuiltInProtocolDefinitions.UpdateInstanceSettings;
    internal static RpcDescriptor<InstanceReference, UnitResult> StartInstance => BuiltInProtocolDefinitions.StartInstance;
    internal static RpcDescriptor<InstanceReference, UnitResult> StopInstance => BuiltInProtocolDefinitions.StopInstance;
    internal static RpcDescriptor<EmptyRequest, JavaRuntimeList> ListJavaRuntimes => BuiltInProtocolDefinitions.ListJavaRuntimes;
    internal static RpcDescriptor<OperationCancelRequest, OperationCancelResult> CancelOperation => BuiltInProtocolDefinitions.CancelOperation;
    internal static RpcDescriptor<OperationReference, OperationSnapshot> GetOperation => BuiltInProtocolDefinitions.GetOperation;
    internal static RpcDescriptor<OperationListQuery, OperationListResult> ListOperations => BuiltInProtocolDefinitions.ListOperations;
    internal static RpcDescriptor<ProvisioningResolveRequest, ProvisioningPlanSnapshot> ResolveProvisioning => BuiltInProtocolDefinitions.ResolveProvisioning;
    internal static RpcDescriptor<ProvisioningPlanReference, ProvisioningPlanSnapshot> GetProvisioningPlan => BuiltInProtocolDefinitions.GetProvisioningPlan;
    internal static RpcDescriptor<ProvisioningExecuteRequest, ProvisioningExecuteResult> ExecuteProvisioning => BuiltInProtocolDefinitions.ExecuteProvisioning;
    internal static RpcDescriptor<EmptyRequest, SystemInfo> GetSystemInfo => BuiltInProtocolDefinitions.GetSystemInfo;
    internal static RpcDescriptor<EmptyRequest, OpenRpcDocument> DiscoverRpc => BuiltInProtocolDefinitions.DiscoverRpc;

    internal static EventDescriptor<InstanceCatalogChangedEventData, EmptyRequest> InstanceCatalogChanged => BuiltInProtocolDefinitions.InstanceCatalogChanged;
    internal static EventDescriptor<DaemonReportEventData, EmptyRequest> DaemonReport => BuiltInProtocolDefinitions.DaemonReport;
    internal static EventDescriptor<InstanceLogEventData, InstanceLogEventMeta> InstanceLog => BuiltInProtocolDefinitions.InstanceLog;
    internal static EventDescriptor<NotificationEventData, NotificationEventMeta> Notification => BuiltInProtocolDefinitions.Notification;

    internal static ImmutableArray<RpcDescriptor> Rpcs { get; } =
    [
        GetAuthPermissions, PingDaemon, CopyDirectory, CreateDirectory, DeleteDirectory, GetDirectoryInfo, MoveDirectory, RenameDirectory,
        SubscribeEvent, UnsubscribeEvent, CopyFile, DeleteFile, CloseDownload, OpenDownload, ReadDownload, GetFileInfo, MoveFile, RenameFile,
        CancelUpload, CloseUpload, OpenUpload, GetInstanceCatalog, SendInstanceCommand, OpenConsole, ResizeConsole, CloseConsole, CreateInstance, GetInstanceEventRules,
        UpdateInstanceEventRules, HaltInstance, GetInstanceLog, RemoveInstance, GetInstanceReport, ListInstanceReports, GetInstanceSettings,
        UpdateInstanceSettings, StartInstance, StopInstance, ListJavaRuntimes, CancelOperation, GetOperation, ListOperations, ResolveProvisioning, GetProvisioningPlan, ExecuteProvisioning, GetSystemInfo,
        DiscoverRpc
    ];

    internal static ImmutableArray<EventDescriptor> Events { get; } =
    [InstanceCatalogChanged, DaemonReport, InstanceLog, Notification];
}
