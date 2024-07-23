namespace MCServerLauncher.Daemon.Remote.Action
{
    internal enum ActionType
    {
        Message,
        Ping,
        FileUploadRequest,
        FileUploadChunk,
        FileUploadCancel
    }
}