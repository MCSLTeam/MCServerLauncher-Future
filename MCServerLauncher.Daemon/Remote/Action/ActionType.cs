namespace MCServerLauncher.Daemon.Remote.Action;

public enum ActionType
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