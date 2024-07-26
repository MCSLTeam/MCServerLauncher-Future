namespace MCServerLauncher.Daemon.Remote.Action;

internal enum ActionType
{
    HeartBeat,
    GetJavaList,
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel
}