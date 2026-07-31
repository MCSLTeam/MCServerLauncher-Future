using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Daemon.API.State;

namespace MCServerLauncher.Daemon.ApplicationCore.Monitoring;

/// <summary>
/// Holds instance lifecycle transitions between the commit that produced them and the monitoring
/// sample that persists them.
/// </summary>
/// <remarks>
/// The sampler used to derive events by diffing its own previous tick against the current one, which
/// only ever sees the net change: a sequence that returns to where it started vanished completely,
/// the intermediate states of a longer sequence were always lost, and what did survive carried the
/// tick's timestamp rather than the moment it happened. Recording at the authoritative commit is the
/// only place that observes every transition, because that is where the catalog decides one occurred.
///
/// Bounded, because nothing else limits how fast an instance can flap, and overflow is counted rather
/// than silent — the same rule the dropped-sample gap markers follow. A hole a reader cannot see is
/// worse than no history.
/// </remarks>
internal sealed class InstanceLifecycleEventBuffer
{
    /// <summary>
    /// Generous relative to one sampling interval: a flapping instance produces a handful of
    /// transitions per second at worst, and the cap exists to bound a pathological loop rather than
    /// to ration ordinary use.
    /// </summary>
    internal const int MaximumPendingEvents = 4096;

    private readonly ConcurrentQueue<MonitoringInstanceEvent> _pending = new();
    private readonly List<MonitoringInstanceEvent> _staged = [];
    private readonly TimeProvider _timeProvider;
    private int _dropped;
    private int _stagedDropped;
    private int _stagedCount;

    internal InstanceLifecycleEventBuffer(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Records whatever changed between two catalog states. <paramref name="previous" /> is null for
    /// an instance the catalog has not published before.
    /// </summary>
    /// <remarks>
    /// A first observation is a baseline, not a transition, so it produces no status event — there is
    /// no previous status a reader could attribute it to. A ready timeout is latched on the process,
    /// so only its rising edge is recorded; repeating it would drown the history in one stuck start.
    /// </remarks>
    internal void Record(InstanceSnapshot? previous, InstanceSnapshot next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var at = _timeProvider.GetUtcNow();

        if (previous is not null && previous.Status != next.Status)
        {
            Enqueue(new MonitoringInstanceEvent(
                next.Id,
                MonitoringEventKind.StatusChanged,
                next.Status,
                previous.Status,
                at));
        }

        if (next.ReadyTimedOut && previous?.ReadyTimedOut != true)
        {
            Enqueue(new MonitoringInstanceEvent(
                next.Id,
                MonitoringEventKind.ReadyTimeout,
                next.Status,
                null,
                at));
        }
    }

    /// <summary>
    /// Everything recorded so far, oldest first, together with how many events were dropped because
    /// the buffer was full. Nothing is discarded until <see cref="Commit" />.
    /// </summary>
    /// <remarks>
    /// Two phases, because the sample that carries these events is written after they are taken and
    /// the write can fail. Discarding on the way out would lose them for a sample that never reached
    /// the log; holding them until the append is acknowledged means the next tick carries them
    /// instead — once, and still in order, since a retry keeps the earlier batch ahead of whatever
    /// arrived meanwhile. Single-consumer: only the sampler calls these.
    /// </remarks>
    internal (ImmutableArray<MonitoringInstanceEvent> Events, int Dropped) Snapshot()
    {
        while (_pending.TryDequeue(out var pending))
            _staged.Add(pending);

        Volatile.Write(ref _stagedCount, _staged.Count);
        _stagedDropped += Interlocked.Exchange(ref _dropped, 0);
        return ([.. _staged], _stagedDropped);
    }

    /// <summary>Acknowledges that the last <see cref="Snapshot" /> reached the log.</summary>
    internal void Commit()
    {
        _staged.Clear();
        Volatile.Write(ref _stagedCount, 0);
        _stagedDropped = 0;
    }

    private void Enqueue(MonitoringInstanceEvent pending)
    {
        // Counted, not silently discarded: the snapshot reports the loss so a reader knows the event
        // list is incomplete rather than assuming nothing happened. Events awaiting acknowledgement
        // count against the bound too, or a log that keeps refusing writes would grow it without
        // limit - which is what the bound is for.
        if (_pending.Count + Volatile.Read(ref _stagedCount) >= MaximumPendingEvents)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        _pending.Enqueue(pending);
    }
}
