using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DaemonClientInboundBenchmarks
{
    private string _actionResponseJson = string.Empty;
    private string _eventPacketJson = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _actionResponseJson = BenchmarkFixtureLoader.LoadText(
            BenchmarkFixturePaths.ActionResponseDir,
            "success-typed-data.json");

        _eventPacketJson = BenchmarkFixtureLoader.LoadText(
            BenchmarkFixturePaths.EventPacketDir,
            "with-meta-and-data.json");

        var actionPrecheck = ParseActionResponse(_actionResponseJson);
        if (actionPrecheck.Id == Guid.Empty)
            throw new InvalidOperationException("DaemonClient inbound action-response benchmark precheck produced an empty correlation id.");

        var packetPrecheck = ParseEventPacket(_eventPacketJson);
        var dataPrecheck = MaterializeEventData(packetPrecheck.EventType, packetPrecheck.EventData);
        if (dataPrecheck is not InstanceLogEventData)
            throw new InvalidOperationException("DaemonClient inbound event benchmark precheck failed to materialize the instance-log payload.");
    }

    [Benchmark]
    public Guid ParseActionResponseEnvelope()
    {
        return ParseActionResponse(_actionResponseJson).Id;
    }

    [Benchmark]
    public string ParseAndMaterializeInstanceLogEventEnvelope()
    {
        var packet = ParseEventPacket(_eventPacketJson);
        var data = MaterializeEventData(packet.EventType, packet.EventData);
        if (data is not InstanceLogEventData instanceLog)
            throw new InvalidOperationException("DaemonClient inbound event benchmark failed to materialize an instance-log payload.");

        return instanceLog.Log;
    }

    private static MCServerLauncher.Common.ProtoType.Action.ActionResponse ParseActionResponse(string received)
    {
        return WsReceivedPlugin.ParseActionResponse(received);
    }

    private static EventPacket ParseEventPacket(string received)
    {
        return WsReceivedPlugin.ParseEventPacket(received);
    }

    private static IEventData? MaterializeEventData(EventType eventType, JsonPayloadBuffer? data)
    {
        return WsReceivedPlugin.MaterializeEventData(eventType, data);
    }
}
