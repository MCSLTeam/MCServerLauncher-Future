namespace MCServerLauncher.Daemon.Remote.Action
{
    internal enum ActionType
    {
        HeartBeat,
        FileUploadRequest,
        FileUploadChunk,
        FileUploadCancel
    }
}