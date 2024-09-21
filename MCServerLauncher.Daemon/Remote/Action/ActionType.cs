namespace MCServerLauncher.Daemon.Remote.Action;

internal enum ActionType
{
    Ping,
    GetJavaList,
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel,
    FileDownloadRequest,
    FileDownloadRange,
    FileDownloadClose,
    GetFileInfo,
    GetDirectoryInfo
}