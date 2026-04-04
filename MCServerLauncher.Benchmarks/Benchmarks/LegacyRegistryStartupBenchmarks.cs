using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class LegacyRegistryStartupBenchmarks
{
    [Benchmark]
    public int BuildLegacyRegistry()
    {
        var snapshot = DaemonActionReflectionBridge.BuildLegacyRegistry();
        return snapshot.HandlerCount;
    }
}
