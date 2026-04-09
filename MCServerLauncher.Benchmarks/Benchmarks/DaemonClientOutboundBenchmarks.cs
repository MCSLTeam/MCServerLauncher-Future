using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient.Connection;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DaemonClientOutboundBenchmarks
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private ActionRequest _pingRequest = null!;
    private ActionRequest _saveEventRulesRequest = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _pingRequest = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = BenchmarkFixtureLoader.ParseElement("{}"),
            Id = FixedRequestId
        };

        _saveEventRulesRequest = new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = BenchmarkFixtureLoader.LoadJson(
                BenchmarkFixturePaths.ActionRequestDir,
                "save-event-rules-nested-parameter.json").GetProperty("params").Clone(),
            Id = FixedRequestId
        };

        if (SerializeActionRequestForTransport(_pingRequest).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound ping benchmark precheck serialized an empty payload.");

        if (SerializeActionRequestForTransport(_saveEventRulesRequest).Length == 0)
            throw new InvalidOperationException("DaemonClient outbound save-event-rules benchmark precheck serialized an empty payload.");
    }

    [Benchmark]
    public int SerializePingActionRequestForTransport()
    {
        return SerializeActionRequestForTransport(_pingRequest).Length;
    }

    [Benchmark]
    public int SerializeSaveEventRulesActionRequestForTransport()
    {
        return SerializeActionRequestForTransport(_saveEventRulesRequest).Length;
    }

    private static byte[] SerializeActionRequestForTransport(ActionRequest request)
    {
        return ClientConnection.SerializeActionRequestForTransport(request);
    }
}
