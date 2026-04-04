using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class ActionSerializationBenchmarks
{
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ActionResponse _response = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.ActionResponseDir,
            "success-typed-data.json");

        _response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = fixture.GetProperty("data").Clone(),
            Id = FixedResponseId
        };
    }

    [Benchmark]
    public string SerializeActionResponse()
    {
        return StjJsonSerializer.Serialize(_response, DaemonRpcJsonBoundary.StjOptions);
    }
}
