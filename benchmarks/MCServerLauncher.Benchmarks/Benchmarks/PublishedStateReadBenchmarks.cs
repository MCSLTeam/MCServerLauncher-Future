using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.State;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Establishes the Phase 6 baseline for steady-state published catalog reads.
/// Primitive return values keep BenchmarkDotNet's consumer from creating a
/// measurement-only allocation for the returned reference object.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class PublishedStateReadBenchmarks
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private StatePublisher<InstanceCatalogSnapshot> _publisher = null!;
    private IInstanceSnapshotSource _snapshotSource = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var snapshot = new InstanceSnapshot(
            InstanceId,
            "Benchmark instance",
            InstanceType.MCJava,
            "1.21.8",
            InstanceStatus.Running);
        var catalog = new InstanceCatalogSnapshot(
            ImmutableDictionary<Guid, InstanceSnapshot>.Empty.Add(InstanceId, snapshot));

        _publisher = new StatePublisher<InstanceCatalogSnapshot>(catalog);
        _snapshotSource = new BenchmarkSnapshotSource(_publisher);

        if (!_snapshotSource.TryGet(InstanceId, out var precheck) || precheck.Id != InstanceId)
            throw new InvalidOperationException("Published-state benchmark precheck could not retrieve the fixture snapshot.");
    }

    [Benchmark]
    public long ReadCurrentVersion() => _publisher.Current.Version;

    [Benchmark]
    public bool TryGetSnapshot() => _snapshotSource.TryGet(InstanceId, out _);

    private sealed class BenchmarkSnapshotSource(StatePublisher<InstanceCatalogSnapshot> publisher) : IInstanceSnapshotSource
    {
        public PublishedState<InstanceCatalogSnapshot> Current => publisher.Current;

        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot)
        {
            var current = publisher.Current;
            return current.Value.TryGet(instanceId, out snapshot);
        }
    }
}
