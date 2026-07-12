namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal enum V2ConnectionCloseReason
{
    Graceful,
    SlowConsumer,
    SendFailure,
    Peer,
    Abort
}

internal enum V2ConnectionStopCause
{
    Graceful,
    SlowConsumer,
    SendTimeout,
    SendFailure,
    Peer,
    Abort
}

internal interface IV2OutboundSender
{
    ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken);

    ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken);
}

internal interface IV2ConnectionCleanup
{
    ValueTask CleanupAsync(CancellationToken cancellationToken);
}
