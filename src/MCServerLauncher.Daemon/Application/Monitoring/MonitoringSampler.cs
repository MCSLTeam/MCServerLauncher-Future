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
    private readonly object _lifecycleGate = new();
    private readonly Dictionary<Guid, InstanceStatus> _lastStatuses = new();
    private readonly HashSet<Guid> _reportedReadyTimeouts = [];
    private readonly BoundedJsonlLog<MonitoringSample> _log;
    private readonly IInstanceManager _instances;
    private readonly IAsyncTimedLazyCell<SystemInfo> _systemInfo;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly ILogger<MonitoringSampler>? _logger;
    private MonitoringSample? _latest;
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
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(systemInfo);
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
            var instanceSamples = ImmutableArray.CreateBuilder<MonitoringInstanceSample>();
            var observed = new List<(Guid InstanceId, InstanceStatus Status, bool ReadyTimedOut)>();
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
                observed.Add((instance.Config.Uuid, status, process?.ReadyTimedOut == true));
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
                CollectSignificantEvents(observed));
            _log.Append(sample);
            Volatile.Write(ref _latest, sample);
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
            // reader from reading the silence as an uninterrupted stretch of observation.
            AppendGap(_timeProvider.GetUtcNow());
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
            return new MonitoringRawWindow(ImmutableArray<MonitoringSample>.Empty, false, _log.DroppedRecords);
        }

        // One record over the budget: ReadRange silently drops its oldest records past the cap, so
        // an overflow has to be seen here rather than inferred from a result that looks complete.
        var raw = _log.ReadRange(notBefore, notAfter, maximumRecords + 1);
        return new MonitoringRawWindow([.. raw], raw.Count > maximumRecords, _log.DroppedRecords);
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

            if (buckets.TryGetValue(bucket, out var existing) && existing.Gap && !sample.Gap)
                continue;
            buckets[bucket] = sample;
        }

        var points = ImmutableArray.CreateBuilder<MonitoringSample>(buckets.Count);
        foreach (var pair in buckets)
        {
            // A gap winner still carries the bucket's events: the hole and the transitions are
            // both things the reader needs, and the gap flag already says the span is incomplete.
            points.Add(bucketEvents.TryGetValue(pair.Key, out var collected)
                ? pair.Value with { Events = collected.ToImmutable() }
                : pair.Value);
        }

        return new MonitoringQueryResult(points.ToImmutable(), _log.DroppedRecords);
    }

    private void RecordStartupGapIfNeeded()
    {
        var last = _log.ReadNewestFirst(1).FirstOrDefault();
        if (last is null || last.Gap)
            return;

        var now = _timeProvider.GetUtcNow();
        if (now - last.Timestamp <= _interval * 2)
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
    /// Lifecycle transitions observed since the previous tick, plus ready-timeout observations.
    /// Nothing derived from log content is recorded here: the plan keeps full console logs out of
    /// the metrics history. The first tick that sees an instance establishes its baseline and
    /// reports no transition, so a daemon restart does not manufacture a burst of fake events.
    /// </summary>
    private ImmutableArray<MonitoringInstanceEvent> CollectSignificantEvents(
        List<(Guid InstanceId, InstanceStatus Status, bool ReadyTimedOut)> observed)
    {
        var events = ImmutableArray.CreateBuilder<MonitoringInstanceEvent>();
        lock (_lifecycleGate)
        {
            var present = new HashSet<Guid>(observed.Count);
            foreach (var (instanceId, status, readyTimedOut) in observed)
            {
                present.Add(instanceId);
                if (_lastStatuses.TryGetValue(instanceId, out var previous) && previous != status)
                {
                    events.Add(new MonitoringInstanceEvent(
                        instanceId,
                        MonitoringEventKind.StatusChanged,
                        status,
                        previous));
                }

                _lastStatuses[instanceId] = status;

                // A ready timeout latches on the process, so record the edge only: repeating it on
                // every later tick would drown the history in one stuck start.
                if (readyTimedOut)
                {
                    if (_reportedReadyTimeouts.Add(instanceId))
                    {
                        events.Add(new MonitoringInstanceEvent(
                            instanceId,
                            MonitoringEventKind.ReadyTimeout,
                            status,
                            null));
                    }
                }
                else
                {
                    _reportedReadyTimeouts.Remove(instanceId);
                }
            }

            // An instance that is gone must not keep a status behind it: were it re-added later, the
            // stale entry would report a transition that no running process ever made.
            foreach (var stale in _lastStatuses.Keys.Except(present).ToArray())
                _lastStatuses.Remove(stale);
            _reportedReadyTimeouts.RemoveWhere(id => !present.Contains(id));
        }

        return events.ToImmutable();
    }

    /// <summary>
    /// A gap carries null for every metric that was never collected. Zero would be a measurement,
    /// and a reader judging a sustained condition must not mistake a hole for a reading of zero.
    /// </summary>
    private void AppendGap(DateTimeOffset at) =>
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
/// more records than the caller's budget, and <see cref="DroppedRecords" /> counts history records
/// lost to write failures; either one means the evidence is incomplete.
/// </summary>
internal sealed record MonitoringRawWindow(
    ImmutableArray<MonitoringSample> Samples,
    bool Truncated,
    long DroppedRecords);
