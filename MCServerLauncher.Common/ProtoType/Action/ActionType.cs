namespace MCServerLauncher.Common.ProtoType.Action;

public enum ActionType
{
    // Event subsystem
    SubscribeEvent,
    UnsubscribeEvent,

    // MISC
    Ping,
    GetSystemInfo,
    GetPermissions,
    GetJavaList,
    GetDirectoryInfo,
    GetFileInfo,

    // File down/upload
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel,
    FileDownloadRequest,
    FileDownloadRange,
    FileDownloadClose,

    // Instance operation
    AddInstance,
    RemoveInstance,
    StartInstance,
    StopInstance,
    KillInstance,
    SendToInstance,
    GetInstanceReport,
    GetAllReports
}