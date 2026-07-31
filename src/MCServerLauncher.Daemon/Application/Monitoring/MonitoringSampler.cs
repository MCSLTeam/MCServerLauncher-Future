using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Monitoring;

/// <summary>
/// Periodic daemon metrics sampler over the shared bounded JSONL history. Each tick records the
/// cached system info (CPU, memory, disk) plus every managed instance with its cached process
/// counters and how long it has been silent, and the lifecycle transitions observed since the
/// previous tick — sampling is passive and never queries a game server. A restart hole larger than
/// two intervals, and every tick that fails to produce a point, are recorded as explicit gap
/// markers so absence in the history is always structured, never silent.
/// </summary>
internal sealed class MonitoringSampler : IDisposable, IAsyncDisposable
{
    internal const int DefaultQueryPoints = 500;
    internal const int MaximumQueryPoints = 2000;
    // Raw-read ceiling before downsampling. A window holding more than this keeps its newest
    // records (see BoundedJsonlLog.ReadRange), so an over-long window shortens from the far end
    // instead of ending before the data the caller actually watches.
    private const int MaximumRawReadRecords = 100_000;

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();

    /// <summary>
    /// Guards the cross-tick lifecycle memory below. <see cref="SampleOnceAsync" /> is reachable
    /// from the tick loop and directly, so the two must not interleave over it.
    /// </summary>
    private readonly BoundedJsonlLog<MonitoringSample> _log;
    private readonly IInstanceManager _instances;
    private readonly IAsyncTimedLazyCell<SystemInfo> _systemInfo;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly ILogger<MonitoringSampler>? _logger;
    private MonitoringSample? _latest;
    private readonly InstanceLifecycleEventBuffer _lifecycleEvents;
    private readonly object _pendingGapGate = new();
    private DateTimeOffset? _pendingGapFrom;
    private DateTimeOffset? _pendingGapTo;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;

    public MonitoringSampler(
        DaemonMonitoringConfig config,
        IInstanceManager instances,
        IAsyncTimedLazyCell<SystemInfo> systemInfo,
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        ILogger<MonitoringSampler>? logger = null,
        TimeSpan? interval = null,
        InstanceLifecycleEventBuffer? lifecycleEvents = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(systemInfo);
        // Taken from the manager rather than wired through the container, because the alternative is
        // an optional dependency that silently leaves the history eventless if a registration is ever
        // missed, and IInstanceManager cannot carry it — that interface is the packaged API boundary
        // and this buffer is a daemon internal. A caller may still supply one, which is what lets a
        // test drive transitions without a real manager.
        _lifecycleEvents = lifecycleEvents
            ?? (instances as InstanceManager)?.LifecycleEvents
            ?? new InstanceLifecycleEventBuffer(timeProvider);
        config.Validate();
        _instances = instances;
        _systemInfo = systemInfo;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? DefaultInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_interval, TimeSpan.Zero);
        _logger = logger;
        _log = new BoundedJsonlLog<MonitoringSample>(
            Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "monitoring")),
            "metrics",
            ApplicationContractJsonContext.Default.MonitoringSample,
            static sample => sample.Timestamp,
            config.MaximumBytes,
            TimeSpan.FromDays(config.RetentionDays),
            timeProvider,
            logger);
        RecordStartupGapIfNeeded();
    }

    internal MonitoringSample? Latest => Volatile.Read(ref _latest);

    internal long DroppedRecords => _log.DroppedRecords;

    /// <summary>
    /// The cadence the history is written at. Readers that judge coverage of a time range need the
    /// sampler's own interval, not their own tick rate, to tell a normal spacing from a hole.
    /// </summary>
    internal TimeSpan SampleInterval => _interval;

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is { IsCompleted: false })
                return;

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(_runCancellation.Token);
        }
    }

    internal void RequestStop()
    {
        lock (_gate)
            _runCancellation?.Cancel();

        // A graceful stop is the last chance to write a remembered hole down. Without this the
        // evidence dies with the process and the window reads as fully observed after a restart.
        FlushPendingGaps();
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        lock (_gate)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is not null)
            await runTask.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runCancellation?.Cancel();
        }

        FlushPendingGaps();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await StopAsync();
        lock (_gate)
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
        }
    }

    internal async Task<MonitoringSample> SampleOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var info = await _systemInfo.Value.ConfigureAwait(false);
            // Taken before the instances are walked, so an event committed during this tick belongs
            // to the next sample rather than arriving after its own status was already recorded.
            // Not removed yet: the sample carrying them can still fail to reach the log.
            var lifecycle = _lifecycleEvents.Snapshot();
            var instanceSamples = ImmutableArray.CreateBuilder<MonitoringInstanceSample>();
            foreach (var instance in _instances.Instances.Values.OrderBy(static entry => entry.Config.Uuid))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Deliberately the 2-second cached counters, never GetReportAsync: a report issues a
                // real server ping per instance per tick, and this sampler must stay passive.
                var process = instance.Process;
                var counters = process is { Monitor: { } monitor }
                    ? await monitor.GetMonitorData().ConfigureAwait(false)
                    : default;
                var status = instance.Status;
                instanceSamples.Add(new MonitoringInstanceSample(
                    instance.Config.Uuid,
                    instance.Config.Name,
                    status,
                    counters.Cpu,
                    counters.Memory,
                    MeasureSilence(status, process, now)));
            }

            var used = info.Mem.TotalKilobytes >= info.Mem.FreeKilobytes
                ? info.Mem.TotalKilobytes - info.Mem.FreeKilobytes
                : 0;
            var sample = new MonitoringSample(
                now,
                Gap: false,
                info.Cpu.Usage,
                used,
                info.Mem.TotalKilobytes,
                instanceSamples.ToImmutable(),
                // The cached system info is already awaited above; disk comes out of it rather than
                // from a fresh filesystem call, so a wider record costs the tick nothing.
                info.Drive.TotalBytes,
                info.Drive.FreeBytes,
                // Taken from the buffer rather than derived by comparing this tick with the last one.
                // The commit that produced a transition is the only place that sees it: a diff of two
                // ticks reports the net change, so a sequence returning to its starting status
                // vanished and the intermediate states of a longer one were always lost.
                lifecycle.Events,
                lifecycle.Dropped);

            // Before the record itself, so the hole is ordered ahead of the sample that proves the
            // log is writable again.
            FlushPendingGaps();
            if (_log.Append(sample))
            {
                // Only now: the events are in the history, so the buffer may forget them, and
                // mcsl.monitoring.current.get is defined as the newest *retained* sample. Publishing
                // one that never reached the log would serve its events twice — once from the failed
                // sample and again from the one that carries them next tick — and events have no id
                // for a consumer to tell the repeat from a new occurrence.
                _lifecycleEvents.Commit();
                Volatile.Write(ref _latest, sample);
            }
            else
            {
                RememberDroppedRecord(now);
            }

            return sample;
        }
        catch (OperationCanceledException)
        {
            // A cancelled sample means the sampler is stopping; the hole up to the next start is
            // the restart hole, and the startup marker already covers it.
            throw;
        }
        catch
        {
            // A tick that produced no point is time nobody observed. Marking it keeps a later
            // reader from reading the silence as an uninterrupted stretch of observation, and a
            // marker that cannot be written is itself a hole worth remembering.
            var at = _timeProvider.GetUtcNow();
            if (!AppendGap(at))
                RememberDroppedRecord(at);
            throw;
        }
    }

    /// <summary>
    /// Lossless oldest-first raw read: no bucketing, no downsampling. Callers that must not judge
    /// an incomplete series read this instead of <see cref="Query" /> and refuse on
    /// <see cref="MonitoringRawWindow.Truncated" /> or a non-zero dropped-record count.
    /// </summary>
    internal MonitoringRawWindow ReadRawWindow(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        if (notAfter < notBefore)
        {
            return new MonitoringRawWindow(ImmutableArray<MonitoringSample>.Empty, false, false);
        }

        // One record over the budget: ReadRange silently drops its oldest records past the cap, so
        // an overflow has to be seen here rather than inferred from a result that looks complete.
        var raw = _log.ReadRange(notBefore, notAfter, maximumRecords + 1);

        // Only a drop inside this window makes this window incomplete. The log's drop counter is
        // monotonic for the process lifetime, so reading it as evidence would condemn every later
        // window for one transient write failure and never recover short of a daemon restart.
        var newestDrop = _log.NewestDropAt;
        var droppedInside = newestDrop is not null && newestDrop >= notBefore && newestDrop <= notAfter;
        return new MonitoringRawWindow([.. raw], raw.Count > maximumRecords, droppedInside);
    }

    /// <summary>
    /// Bounded range query, oldest-first, deterministically downsampled: fixed window-aligned
    /// buckets, last sample per bucket, and a gap record always wins its bucket so downsampling
    /// can never hide a hole. The result never exceeds the requested point count.
    /// </summary>
    internal MonitoringQueryResult Query(MonitoringQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.NotAfter < query.NotBefore)
        {
            return new MonitoringQueryResult(ImmutableArray<MonitoringSample>.Empty, _log.DroppedRecords);
        }

        var cap = Math.Clamp(query.MaximumPoints ?? DefaultQueryPoints, 1, MaximumQueryPoints);
        var raw = _log.ReadRange(query.NotBefore, query.NotAfter, MaximumRawReadRecords);
        if (raw.Count <= cap)
        {
            return new MonitoringQueryResult([.. raw], _log.DroppedRecords);
        }

        var window = query.NotAfter - query.NotBefore;
        var bucketTicks = Math.Max(1, (long)Math.Ceiling(window.Ticks / (double)cap));
        var buckets = new SortedDictionary<long, MonitoringSample>();
        var bucketEvents = new SortedDictionary<long, ImmutableArray<MonitoringInstanceEvent>.Builder>();
        var bucketDropped = new SortedDictionary<long, int>();
        foreach (var sample in raw)
        {
            // Buckets are relative to the window, not the epoch: an epoch-aligned grid splits an
            // unaligned window across cap + 1 buckets and returns more points than were asked for.
            // The closing boundary folds into the last bucket for the same reason.
            var offset = sample.Timestamp.UtcTicks - query.NotBefore.UtcTicks;
            var bucket = Math.Clamp(offset / bucketTicks, 0, cap - 1);

            // A metric is a reading taken at an instant, so keeping one per bucket loses nothing
            // but resolution. An event is something that happened during the bucket's span, so
            // letting it ride the single surviving sample would discard most of them outright —
            // the same silent loss the gap rule below exists to prevent. Accumulate instead.
            var events = sample.Events;
            if (events is not null && events.Value.Length > 0)
            {
                if (!bucketEvents.TryGetValue(bucket, out var collected))
                    bucketEvents[bucket] = collected = ImmutableArray.CreateBuilder<MonitoringInstanceEvent>();
                collected.AddRange(events.Value);
            }

            // For the same reason, and with the same consequence if it is skipped: the bucket winner
            // usually carries zero, so an earlier sample's non-zero loss would vanish and the result
            // would read as a complete event list. Null only survives if every input was null.
            if (sample.DroppedEvents is { } dropped)
                bucketDropped[bucket] = bucketDropped.TryGetValue(bucket, out var running) ? running + dropped : dropped;

            if (buckets.TryGetValue(bucket, out var existing) && existing.Gap && !sample.Gap)
                continue;
            buckets[bucket] = sample;
        }

        var points = ImmutableArray.CreateBuilder<MonitoringSample>(buckets.Count);
        foreach (var pair in buckets)
        {
            // A gap winner still carries the bucket's events: the hole and the transitions are
            // both things the reader needs, and the gap flag already says the span is incomplete.
            var winner = pair.Value;
            if (bucketEvents.TryGetValue(pair.Key, out var collected))
                winner = winner with { Events = collected.ToImmutable() };
            winner = winner with
            {
                DroppedEvents = bucketDropped.TryGetValue(pair.Key, out var dropped) ? dropped : null
            };
            points.Add(winner);
        }

        return new MonitoringQueryResult(points.ToImmutable(), _log.DroppedRecords);
    }

    private void RecordStartupGapIfNeeded()
    {
        var last = _log.ReadNewestFirst(1).FirstOrDefault();
        if (last is null || last.Gap)
            return;

        var now = _timeProvider.GetUtcNow();
        // Exactly two intervals already means one cadence went unobserved, so the boundary belongs
        // inside the hole, not outside it.
        if (now - last.Timestamp < _interval * 2)
            return;

        AppendGap(now);
    }

    /// <summary>
    /// How long an instance has gone without producing output, or null when that question has no
    /// answer: silence carries no meaning for an instance that is not Running, and an instance that
    /// has produced no output at all was never observed to be responsive in the first place. Null
    /// is "not measured" — never a measured zero.
    /// </summary>
    private static double? MeasureSilence(InstanceStatus status, InstanceProcess? process, DateTimeOffset now)
    {
        if (status != InstanceStatus.Running || process?.LastOutputAt is not { } lastOutput)
            return null;

        var silence = (now - lastOutput).TotalSeconds;
        return silence > 0 ? silence : 0;
    }

    /// <summary>
    /// A gap carries null for every metric that was never collected. Zero would be a measurement,
    /// and a reader judging a sustained condition must not mistake a hole for a reading of zero.
    /// </summary>
    private bool AppendGap(DateTimeOffset at) =>
        _log.Append(new MonitoringSample(
            at,
            Gap: true,
            0,
            0,
            0,
            ImmutableArray<MonitoringInstanceSample>.Empty,
            DiskTotalBytes: null,
            DiskFreeBytes: null,
            Events: null));

    /// <summary>
    /// Remembers that a record never reached the log, so the hole can be written into the history
    /// itself rather than living only in this process's memory.
    /// </summary>
    /// <remarks>
    /// The log's dropped count and newest-drop instant are both in-memory, so a restart forgets them
    /// and a reader then treats a window that is missing a record as fully observed. The startup
    /// marker does not save it either: it only fires when the newest persisted record is old enough,
    /// so a quick restart after a dropped tick leaves no evidence at all. Two instants are kept
    /// rather than one per lost tick, because the pair bounds an outage of any length and collapses
    /// to a single marker for the common single drop.
    /// </remarks>
    private void RememberDroppedRecord(DateTimeOffset at)
    {
        lock (_pendingGapGate)
        {
            _pendingGapFrom ??= at;
            _pendingGapTo = at;
        }
    }

    /// <summary>
    /// Commits the remembered holes, oldest first. Called before the record that proves the log is
    /// writable again, so the retained series stays in timestamp order, and on the stop and dispose
    /// paths so a graceful shutdown does not take the evidence with it.
    /// </summary>
    /// <remarks>
    /// The markers are cleared only once their gap records are actually written. A gap append that
    /// fails leaves the hole pending for the next attempt, because the whole point is that the
    /// evidence outlives this process.
    /// </remarks>
    private void FlushPendingGaps()
    {
        DateTimeOffset from;
        DateTimeOffset to;
        lock (_pendingGapGate)
        {
            if (_pendingGapFrom is null)
                return;
            from = _pendingGapFrom.Value;
            to = _pendingGapTo!.Value;
        }

        if (!AppendGap(from))
            return;
        if (to != from && !AppendGap(to))
        {
            // The opening marker landed, so the window already reads as incomplete from `from`
            // onwards. Keep the closing one pending rather than forgetting where the outage ended.
            lock (_pendingGapGate)
            {
                _pendingGapFrom = to;
            }

            return;
        }

        lock (_pendingGapGate)
        {
            if (_pendingGapTo == to)
            {
                _pendingGapFrom = null;
                _pendingGapTo = null;
            }
            else
            {
                // A tick dropped while this flush was running. Keep the newer hole pending.
                _pendingGapFrom = _pendingGapTo;
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await SampleOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A failed tick loses one point, never the sampler; the sample path already
                    // recorded the hole it left behind.
                    _logger?.LogWarning(exception, "[MonitoringSampler] Skipped a metrics sample.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "[MonitoringSampler] The metrics sampler stopped unexpectedly.");
        }
    }
}

/// <summary>
/// A raw metrics window exactly as it was retained. <see cref="Truncated" /> means the window held
/// more records than the caller's budget, and <see cref="DroppedInside" /> means a write failure
/// lost a record whose timestamp falls in this window; either one means the evidence is incomplete.
/// </summary>
/// <remarks>
/// <see cref="DroppedInside" /> is scoped to the window rather than reporting the log's lifetime
/// drop count on purpose. A drop before the window start lost a record this window never covered,
/// so it says nothing about this window's completeness — and a lifetime counter never falls, so
/// judging on it would disable every later evaluation until the daemon restarted.
/// </remarks>
internal sealed record MonitoringRawWindow(
    ImmutableArray<MonitoringSample> Samples,
    bool Truncated,
    bool DroppedInside);
