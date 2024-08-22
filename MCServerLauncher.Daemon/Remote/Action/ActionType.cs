namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///    Action types
/// </summary>
internal enum ActionType
{
    HeartBeat,
    GetJavaList,
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel
}