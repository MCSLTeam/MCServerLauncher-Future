using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Benchmarks.Infrastructure;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DaemonClientRoundTripBenchmarks
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ActionRequest _pingRequest = null!;
    private ActionResponse _pingResponse = null!;
    private EventPacket _instanceLogPacket = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _pingRequest = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = BenchmarkFixtureLoader.ParseElement("{}"),
            Id = FixedRequestId
        };

        var actionResponseFixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.ActionResponseDir,
            "success-typed-data.json");

        _pingResponse = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = actionResponseFixture.GetProperty("data").Clone(),
            Id = FixedResponseId
        };

        var eventFixture = BenchmarkFixtureLoader.LoadJson(
            BenchmarkFixturePaths.EventPacketDir,
            "with-meta-and-data.json");

        _instanceLogPacket = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = new JsonPayloadBuffer(eventFixture.GetProperty("meta").Clone()),
            EventData = new JsonPayloadBuffer(eventFixture.GetProperty("data").Clone()),
            Timestamp = eventFixture.GetProperty("time").GetInt64()
        };

        if (ClientToDaemonToClientActionRoundTripCore() == Guid.Empty)
            throw new InvalidOperationException("DaemonClient action round-trip benchmark precheck produced an empty response id.");

        if (DaemonToClientEventRoundTripCore().Length == 0)
            throw new InvalidOperationException("DaemonClient event round-trip benchmark precheck produced an empty log payload.");
    }

    [Benchmark]
    public Guid ClientToDaemonToClientActionRoundTrip()
    {
        return ClientToDaemonToClientActionRoundTripCore();
    }

    [Benchmark]
    public string DaemonToClientEventRoundTrip()
    {
        return DaemonToClientEventRoundTripCore();
    }

    private Guid ClientToDaemonToClientActionRoundTripCore()
    {
        var requestWireBytes = SerializeActionRequestForTransport(_pingRequest);
        var requestWireJson = System.Text.Encoding.UTF8.GetString(requestWireBytes);

        var parsedRequest = ActionExecutorExtensions.ParseRequest(null!, requestWireJson);
        if (!parsedRequest.IsOk(out var request) || request.ActionType != ActionType.Ping)
            throw new InvalidOperationException("DaemonClient action round-trip benchmark failed to parse the ping request at the daemon seam.");

        var responseWireJson = StjJsonSerializer.Serialize(_pingResponse, DaemonRpcJsonBoundary.StjOptions);
        var parsedResponse = ParseActionResponse(responseWireJson);
        if (parsedResponse.RequestStatus != ActionRequestStatus.Ok)
            throw new InvalidOperationException("DaemonClient action round-trip benchmark failed to parse the response at the client seam.");

        return parsedResponse.Id;
    }

    private string DaemonToClientEventRoundTripCore()
    {
        var wireJson = StjJsonSerializer.Serialize(_instanceLogPacket, DaemonRpcJsonBoundary.StjOptions);
        var parsedPacket = ParseEventPacket(wireJson);
        var data = MaterializeEventData(parsedPacket.EventType, parsedPacket.EventData);
        if (data is not InstanceLogEventData instanceLog)
            throw new InvalidOperationException("DaemonClient event round-trip benchmark failed to materialize an instance-log payload.");

        return instanceLog.Log;
    }

    private static byte[] SerializeActionRequestForTransport(ActionRequest request)
    {
        return ClientConnection.SerializeActionRequestForTransport(request);
    }

    private static ActionResponse ParseActionResponse(string received)
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
