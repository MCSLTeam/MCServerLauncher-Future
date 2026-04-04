using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class GeneratedRegistryStartupBenchmarks
{
    [Benchmark]
    public int BuildGeneratedRegistry()
    {
        return DaemonActionReflectionBridge.BuildGeneratedRegistry();
    }
}
