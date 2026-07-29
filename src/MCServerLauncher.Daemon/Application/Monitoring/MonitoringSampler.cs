using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Monitoring;

/// <summary>
/// Periodic daemon metrics sampler over the shared bounded JSONL history. Each tick records the
/// cached system info plus every managed instance with its cached process counters — sampling is
/// passive and never queries a game server. A restart hole larger than two intervals, and every
/// tick that fails to produce a point, are recorded as explicit gap markers so absence in the
/// history is always structured, never silent.
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
            var info = await _systemInfo.Value.ConfigureAwait(false);
            var instanceSamples = ImmutableArray.CreateBuilder<MonitoringInstanceSample>();
            foreach (var instance in _instances.Instances.Values.OrderBy(static entry => entry.Config.Uuid))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var counters = instance.Process is { Monitor: { } monitor }
                    ? await monitor.GetMonitorData().ConfigureAwait(false)
                    : default;
                instanceSamples.Add(new MonitoringInstanceSample(
                    instance.Config.Uuid,
                    instance.Config.Name,
                    instance.Status,
                    counters.Cpu,
                    counters.Memory));
            }

            var used = info.Mem.TotalKilobytes >= info.Mem.FreeKilobytes
                ? info.Mem.TotalKilobytes - info.Mem.FreeKilobytes
                : 0;
            var sample = new MonitoringSample(
                _timeProvider.GetUtcNow(),
                Gap: false,
                info.Cpu.Usage,
                used,
                info.Mem.TotalKilobytes,
                instanceSamples.ToImmutable());
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
        foreach (var sample in raw)
        {
            // Buckets are relative to the window, not the epoch: an epoch-aligned grid splits an
            // unaligned window across cap + 1 buckets and returns more points than were asked for.
            // The closing boundary folds into the last bucket for the same reason.
            var offset = sample.Timestamp.UtcTicks - query.NotBefore.UtcTicks;
            var bucket = Math.Clamp(offset / bucketTicks, 0, cap - 1);
            if (buckets.TryGetValue(bucket, out var existing) && existing.Gap && !sample.Gap)
                continue;
            buckets[bucket] = sample;
        }

        return new MonitoringQueryResult([.. buckets.Values], _log.DroppedRecords);
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

    private void AppendGap(DateTimeOffset at) =>
        _log.Append(new MonitoringSample(
            at,
            Gap: true,
            0,
            0,
            0,
            ImmutableArray<MonitoringInstanceSample>.Empty));

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
