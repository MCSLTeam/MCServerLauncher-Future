namespace MCServerLauncher.Daemon.Bootstrap;

internal interface ILegacyEventQueueParticipant
{
    void StopAccepting();

    Task DrainAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns the daemon shutdown participation of the V1 event queue without taking plugin ownership.
/// </summary>
internal sealed class LegacyEventQueueControl
{
    private readonly object _gate = new();
    private ILegacyEventQueueParticipant? _participant;
    private int _stopping;

    internal void Attach(ILegacyEventQueueParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        lock (_gate)
        {
            if (_participant is not null)
                throw new InvalidOperationException("The legacy event queue participant is already attached.");

            _participant = participant;
        }
    }

    internal void StopAccepting()
    {
        var participant = GetParticipant();
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
            participant.StopAccepting();
    }

    internal Task DrainAsync(CancellationToken cancellationToken)
    {
        return GetParticipant().DrainAsync(cancellationToken);
    }

    private ILegacyEventQueueParticipant GetParticipant()
    {
        lock (_gate)
            return _participant ?? throw new InvalidOperationException("The legacy event queue participant was not attached.");
    }
}
