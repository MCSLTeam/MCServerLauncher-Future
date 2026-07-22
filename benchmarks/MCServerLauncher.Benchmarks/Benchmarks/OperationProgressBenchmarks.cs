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
/// Measures in-memory operation progress updates while time remains inside the persistence
/// coalescing window. The returned write count makes accidental per-callback index rewrites
/// observable in benchmark output.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class OperationProgressBenchmarks
{
    private OperationCoordinator _coordinator = null!;
    private IOperationContext _context = null!;
    private TaskCompletionSource _release = null!;
    private string _root = null!;
    private long _reportSequence;

    [Params(1, 100, 1_000)]
    public int ReportsPerBurst { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _root = Directory.CreateTempSubdirectory("mcsl-operation-benchmark-").FullName;
        _coordinator = new OperationCoordinator(
            timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z")),
            rootDirectory: _root);
        var contextReady = new TaskCompletionSource<IOperationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = await _coordinator.StartAsync(
            kind: "benchmark.progress",
            target: null,
            ownerPrincipal: "benchmark",
            executor: async (_, context, cancellationToken) =>
            {
                contextReady.TrySetResult(context);
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return Result.Ok<string, DaemonError>("benchmark-complete");
            },
            cancellationToken: CancellationToken.None);
        if (started.IsErr(out var error))
            throw new InvalidOperationException($"Operation benchmark setup failed: {error!.Code} {error.Message}");

        _context = await contextReady.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Benchmark]
    public long ReportProgressBurst()
    {
        for (var index = 0; index < ReportsPerBurst; index++)
        {
            var sequence = Interlocked.Increment(ref _reportSequence);
            _context.ReportProgress(new OperationProgress(
                Indeterminate: false,
                Completed: sequence % 10_000,
                Total: 10_000,
                Unit: "callbacks",
                BytesTransferred: null,
                BytesTotal: null,
                Rate: null));
        }

        return _coordinator.PersistenceWriteCount;
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _release.TrySetResult();
        await _coordinator.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
