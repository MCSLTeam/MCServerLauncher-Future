using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DaemonOutboundBenchmarks
{
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ActionResponse _actionResponse = null!;
    private EventPacket _eventPacket = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var actionResponseFixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.ActionResponseDir,
            "success-typed-data.json");

        _actionResponse = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = actionResponseFixture.GetProperty("data").Clone(),
            Id = FixedResponseId
        };

        var eventPacketFixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.EventPacketDir,
            "null-meta-structured-data.json");

        _eventPacket = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = new JsonPayloadBuffer(eventPacketFixture.GetProperty("data").Clone()),
            Timestamp = eventPacketFixture.GetProperty("time").GetInt64()
        };

        if (SerializeActionResponseForTransport(_actionResponse).Length == 0)
            throw new InvalidOperationException("Daemon outbound action-response benchmark precheck serialized an empty payload.");

        if (SerializeEventPacketForTransport(_eventPacket).Length == 0)
            throw new InvalidOperationException("Daemon outbound event-packet benchmark precheck serialized an empty payload.");
    }

    [Benchmark]
    public int SerializeActionResponseForTransport()
    {
        return SerializeActionResponseForTransport(_actionResponse).Length;
    }

    [Benchmark]
    public int SerializeEventPacketForTransport()
    {
        return SerializeEventPacketForTransport(_eventPacket).Length;
    }

    private static byte[] SerializeActionResponseForTransport(ActionResponse response)
    {
        return StjJsonSerializer.SerializeToUtf8Bytes(response, DaemonRpcJsonBoundary.StjOptions);
    }

    private static byte[] SerializeEventPacketForTransport(EventPacket packet)
    {
        return StjJsonSerializer.SerializeToUtf8Bytes(packet, DaemonRpcJsonBoundary.StjOptions);
    }
}
