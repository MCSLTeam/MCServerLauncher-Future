namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action types
/// </summary>
internal enum ActionType
{
    Ping,
    GetJavaList,
    FileUploadRequest,
    FileUploadChunk,
    FileUploadCancel
}