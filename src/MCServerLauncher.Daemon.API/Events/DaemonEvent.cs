namespace MCServerLauncher.Daemon.API.Events;

/// <param name="Timestamp">The event timestamp in Unix milliseconds.</param>
public sealed record DaemonEvent<TData, TMeta>(
    long Sequence,
    long Timestamp,
    DaemonEventField<TMeta> Meta,
    DaemonEventField<TData> Data);
