namespace MCServerLauncher.DaemonClient;

public enum DaemonConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Synchronizing = 2,
    Ready = 3,
    Reconnecting = 4,
    Closing = 5,
    Closed = 6
}
