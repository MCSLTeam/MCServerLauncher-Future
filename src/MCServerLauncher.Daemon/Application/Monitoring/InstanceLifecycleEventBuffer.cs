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
    private readonly TimeProvider _timeProvider;
    private int _dropped;

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
    /// Takes everything recorded so far, oldest first, together with how many events were dropped
    /// because the buffer was full.
    /// </summary>
    internal (ImmutableArray<MonitoringInstanceEvent> Events, int Dropped) Drain()
    {
        var drained = ImmutableArray.CreateBuilder<MonitoringInstanceEvent>();
        while (_pending.TryDequeue(out var pending))
            drained.Add(pending);

        return (drained.ToImmutable(), Interlocked.Exchange(ref _dropped, 0));
    }

    private void Enqueue(MonitoringInstanceEvent pending)
    {
        // Counted, not silently discarded: the drain reports the loss so a reader knows the event
        // list is incomplete rather than assuming nothing happened.
        if (_pending.Count >= MaximumPendingEvents)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        _pending.Enqueue(pending);
    }
}
