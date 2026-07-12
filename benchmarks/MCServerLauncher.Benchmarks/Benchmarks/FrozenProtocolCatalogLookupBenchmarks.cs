using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Measures only steady-state frozen lookup. The full built-in catalog is composed before timing.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class FrozenProtocolCatalogLookupBenchmarks
{
    private FrozenProtocolCatalog _catalog = null!;
    private RpcMethod _hitMethod = null!;
    private RpcMethod _missMethod = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _catalog = BenchmarkProtocolCatalogFactory.Create();
        if (_catalog.Rpcs.Count != BuiltInProtocolDefinitions.Rpcs.Length ||
            _catalog.Events.Count != BuiltInProtocolDefinitions.Events.Length)
        {
            throw new InvalidOperationException("The benchmark catalog does not match the built-in definition inventory.");
        }

        _hitMethod = new RpcMethod(BuiltInProtocolDefinitions.Rpcs[0].Method.Value);
        _missMethod = new RpcMethod("mcsl.benchmark.missing");
        if (!_catalog.TryGetRpc(_hitMethod, out _) || _catalog.TryGetRpc(_missMethod, out _))
            throw new InvalidOperationException("Frozen catalog lookup benchmark precheck failed.");
    }

    [Benchmark]
    public int LookupHit() =>
        _catalog.TryGetRpc(_hitMethod, out var binding) ? binding.Descriptor.Method.Value.Length : 0;

    [Benchmark]
    public int LookupMiss() => _catalog.TryGetRpc(_missMethod, out _) ? -1 : 0;
}
