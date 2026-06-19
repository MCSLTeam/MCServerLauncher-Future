using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Daemon.Remote.Action;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class ActionParsingBenchmarks
{
    private string _actionRequestJson = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _actionRequestJson = BenchmarkFixtureLoader.LoadText(
            BenchmarkFixturePaths.ActionRequestDir,
            "save-event-rules-nested-parameter.json");

        var precheck = ActionExecutorExtensions.ParseRequest(null!, _actionRequestJson);
        if (!precheck.IsOk(out _))
            throw new InvalidOperationException("Action parsing benchmark fixture failed the precheck parse.");
    }

    [Benchmark]
    public void ParseActionRequest()
    {
        var result = ActionExecutorExtensions.ParseRequest(null!, _actionRequestJson);
        if (!result.IsOk(out _))
            throw new InvalidOperationException("Action parsing benchmark iteration failed unexpectedly.");
    }
}
