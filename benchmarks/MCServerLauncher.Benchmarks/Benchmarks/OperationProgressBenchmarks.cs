using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using RustyOptions;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Measures operation progress bursts across different index sizes and reporter counts.
/// Every invocation verifies its persistent-write delta so the in-memory coalescing path
/// cannot be mistaken for the real cadence-triggered index rewrite path.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class OperationProgressBenchmarks
{
    private static readonly TimeSpan ProgressPersistenceCadence = TimeSpan.FromMilliseconds(200);

    private OperationCoordinator _coordinator = null!;
    private IOperationContext[] _contexts = null!;
    private ParallelOptions _parallelOptions = null!;
    private AdvancingTimeProvider _timeProvider = null!;
    private TaskCompletionSource _release = null!;
    private string _root = null!;
    private long _reportSequence;

    [Params(1, 64)]
    public int ActiveOperations { get; set; }

    [Params(1, 4)]
    public int ConcurrentReporters { get; set; }

    [Params(128, 2_048)]
    public int ReportsPerBurst { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _root = Directory.CreateTempSubdirectory("mcsl-operation-benchmark-").FullName;
        _timeProvider = new AdvancingTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
        _coordinator = new OperationCoordinator(
            timeProvider: _timeProvider,
            rootDirectory: _root);
        _contexts = new IOperationContext[ActiveOperations];
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = ConcurrentReporters };
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var index = 0; index < ActiveOperations; index++)
        {
            var contextReady = new TaskCompletionSource<IOperationContext>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await _coordinator.StartAsync(
                kind: "benchmark.progress",
                target: $"benchmark-target-{index}",
                ownerPrincipal: "benchmark",
                executor: async (_, context, cancellationToken) =>
                {
                    contextReady.TrySetResult(context);
                    await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return Result.Ok<string, DaemonError>("benchmark-complete");
                },
                cancellationToken: CancellationToken.None);
            if (started.IsErr(out var error))
            {
                throw new InvalidOperationException(
                    $"Operation benchmark setup failed: {error!.Code} {error.Message}");
            }

            _contexts[index] = await contextReady.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        if (!File.Exists(Path.Combine(_root, "index.json")))
            throw new InvalidOperationException("Operation benchmark setup did not persist its index.");
    }

    /// <summary>
    /// Progress callback/update cost while the 200 ms persistence cadence has not elapsed.
    /// The measured burst must perform no index write.
    /// </summary>
    [Benchmark(Baseline = true)]
    public long ReportProgressInsideCoalescingWindow() =>
        ReportProgressBurst(advancePersistenceCadence: false, expectedWrites: 0);

    /// <summary>
    /// Progress callback/update cost plus one real, cadence-triggered rewrite of the complete
    /// operation index. Concurrent callbacks in the same burst must still coalesce to one write.
    /// </summary>
    [Benchmark]
    public long ReportProgressAtPersistenceCadence()
    {
        _timeProvider.Advance(ProgressPersistenceCadence);
        return ReportProgressBurst(advancePersistenceCadence: true, expectedWrites: 1);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _release.TrySetResult();
        await _coordinator.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(_root, recursive: true);
    }

    private long ReportProgressBurst(bool advancePersistenceCadence, long expectedWrites)
    {
        var writesBefore = _coordinator.PersistenceWriteCount;

        if (ConcurrentReporters == 1)
        {
            for (var index = 0; index < ReportsPerBurst; index++)
                ReportProgress(index);
        }
        else
        {
            Parallel.For(0, ReportsPerBurst, _parallelOptions, ReportProgress);
        }

        var writes = _coordinator.PersistenceWriteCount - writesBefore;
        if (writes != expectedWrites)
        {
            var cadenceDescription = advancePersistenceCadence ? "elapsed" : "not elapsed";
            throw new InvalidOperationException(
                $"Progress burst persisted {writes} indexes while the cadence had {cadenceDescription}; " +
                $"expected {expectedWrites}.");
        }

        return writes;
    }

    private void ReportProgress(int index)
    {
        var sequence = Interlocked.Increment(ref _reportSequence);
        _contexts[index % _contexts.Length].ReportProgress(new OperationProgress(
            Indeterminate: false,
            Completed: sequence,
            Total: null,
            Unit: "callbacks",
            BytesTransferred: sequence,
            BytesTotal: null,
            Rate: null));
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private long _utcTicks = initialTime.ToUniversalTime().Ticks;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        internal void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
}
