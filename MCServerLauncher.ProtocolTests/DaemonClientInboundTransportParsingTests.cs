using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.ProtocolTests;

public class DaemonClientInboundTransportParsingTests
{
    [Fact]
    [Trait("Category", "ClientInbound")]
    public void DetectEnvelopeType_EventEnvelope_ReturnsEvent()
    {
        var envelope =
            """
            {
              "event": "instance_log",
              "meta": { "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" },
              "data": { "log": "hello" },
              "time": 1717171717000
            }
            """;

        var detected = WsReceivedPlugin.DetectEnvelopeType(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Event, detected);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void DetectEnvelopeType_ActionEnvelope_ReturnsAction()
    {
        var envelope =
            """
            {
              "status": "ok",
              "retcode": 0,
              "data": { "time": 1717171717171 },
              "message": "OK",
              "id": "22222222-2222-2222-2222-222222222222"
            }
            """;

        var detected = WsReceivedPlugin.DetectEnvelopeType(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Action, detected);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void DetectEnvelopeType_UnknownEnvelope_ReturnsUnknown()
    {
        var envelope =
            """
            {
              "ping": "pong"
            }
            """;

        var detected = WsReceivedPlugin.DetectEnvelopeType(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Unknown, detected);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void ParseInboundEnvelope_EventEnvelope_ReturnsEventPacket_AndMatchesWrapperPath()
    {
        var envelope =
            """
            {
              "event": "instance_log",
              "meta": { "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" },
              "data": { "log": "[12:00:00] hello" },
              "time": 1717171717000
            }
            """;

        var parsed = WsReceivedPlugin.ParseInboundEnvelope(envelope);
        var wrapperPacket = WsReceivedPlugin.ParseEventPacket(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Event, parsed.EnvelopeType);
        Assert.NotNull(parsed.EventPacket);
        Assert.Null(parsed.ActionResponse);

        var parsedPacket = parsed.EventPacket!;
        Assert.Equal(wrapperPacket.EventType, parsedPacket.EventType);
        Assert.Equal(wrapperPacket.Timestamp, parsedPacket.Timestamp);

        var parsedMeta = Assert.IsType<InstanceLogEventMeta>(
            WsReceivedPlugin.MaterializeEventMeta(parsedPacket.EventType, parsedPacket.EventMeta));
        var wrapperMeta = Assert.IsType<InstanceLogEventMeta>(
            WsReceivedPlugin.MaterializeEventMeta(wrapperPacket.EventType, wrapperPacket.EventMeta));
        var parsedData = Assert.IsType<InstanceLogEventData>(
            WsReceivedPlugin.MaterializeEventData(parsedPacket.EventType, parsedPacket.EventData));
        var wrapperData = Assert.IsType<InstanceLogEventData>(
            WsReceivedPlugin.MaterializeEventData(wrapperPacket.EventType, wrapperPacket.EventData));

        Assert.Equal(wrapperMeta.InstanceId, parsedMeta.InstanceId);
        Assert.Equal(wrapperData.Log, parsedData.Log);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void ParseInboundEnvelope_ActionEnvelope_ReturnsActionResponse_AndMatchesWrapperPath()
    {
        var envelope =
            """
            {
              "status": "ok",
              "retcode": 0,
              "data": { "time": 1717171717171 },
              "message": "OK",
              "id": "22222222-2222-2222-2222-222222222222"
            }
            """;

        var parsed = WsReceivedPlugin.ParseInboundEnvelope(envelope);
        var wrapperResponse = WsReceivedPlugin.ParseActionResponse(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Action, parsed.EnvelopeType);
        Assert.Null(parsed.EventPacket);
        Assert.NotNull(parsed.ActionResponse);

        var parsedResponse = parsed.ActionResponse!;
        Assert.Equal(wrapperResponse.RequestStatus, parsedResponse.RequestStatus);
        Assert.Equal(wrapperResponse.Retcode, parsedResponse.Retcode);
        Assert.Equal(wrapperResponse.Message, parsedResponse.Message);
        Assert.Equal(wrapperResponse.Id, parsedResponse.Id);
        Assert.True(parsedResponse.Data.HasValue);
        Assert.Equal(JsonValueKind.Object, parsedResponse.Data.Value.ValueKind);
        Assert.Equal(wrapperResponse.Data!.Value.GetProperty("time").GetInt64(), parsedResponse.Data.Value.GetProperty("time").GetInt64());
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void ParseInboundEnvelope_UnknownEnvelope_ReturnsUnknown()
    {
        var envelope =
            """
            {
              "ping": "pong"
            }
            """;

        var parsed = WsReceivedPlugin.ParseInboundEnvelope(envelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Unknown, parsed.EnvelopeType);
        Assert.Null(parsed.EventPacket);
        Assert.Null(parsed.ActionResponse);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseInboundEnvelope_MalformedJson_ThrowsJsonException()
    {
        var malformed = "{\"event\":\"instance_log\"";

        Assert.ThrowsAny<JsonException>(() => WsReceivedPlugin.ParseInboundEnvelope(malformed));
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseInboundEnvelope_NonObjectRoot_ThrowsJsonException()
    {
        var malformed = "[]";

        Assert.Throws<JsonException>(() => WsReceivedPlugin.ParseInboundEnvelope(malformed));
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void ParseEventPacket_ValidInstanceLogPayload_MaterializesTypedMetaAndData()
    {
        var envelope =
            """
            {
              "event": "instance_log",
              "meta": { "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" },
              "data": { "log": "[12:00:00] hello" },
              "time": 1717171717000
            }
            """;

        var packet = WsReceivedPlugin.ParseEventPacket(envelope);
        var meta = WsReceivedPlugin.MaterializeEventMeta(packet.EventType, packet.EventMeta);
        var data = WsReceivedPlugin.MaterializeEventData(packet.EventType, packet.EventData);

        Assert.Equal(EventType.InstanceLog, packet.EventType);
        Assert.Equal(1717171717000L, packet.Timestamp);

        var typedMeta = Assert.IsType<InstanceLogEventMeta>(meta);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), typedMeta.InstanceId);

        var typedData = Assert.IsType<InstanceLogEventData>(data);
        Assert.Equal("[12:00:00] hello", typedData.Log);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void ParseActionResponse_ValidPayload_ParsesStatusIdAndClonesData()
    {
        var envelope =
            """
            {
              "status": "ok",
              "retcode": 0,
              "data": { "time": 1717171717171 },
              "message": "OK",
              "id": "22222222-2222-2222-2222-222222222222"
            }
            """;

        var response = WsReceivedPlugin.ParseActionResponse(envelope);

        Assert.Equal(ActionRequestStatus.Ok, response.RequestStatus);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), response.Id);
        Assert.True(response.Data.HasValue);
        Assert.Equal(JsonValueKind.Object, response.Data.Value.ValueKind);
        Assert.Equal(1717171717171L, response.Data.Value.GetProperty("time").GetInt64());
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public async Task OnWebSocketReceived_NonTextFrame_IgnoresAndDoesNotDispatch()
    {
        var plugin = new WsReceivedPlugin();
        var eventCount = 0;
        var actionCount = 0;

        plugin.OnEventReceived += (_, _, _, _) => eventCount++;
        plugin.OnActionResponseReceived += _ => actionCount++;

        var frame = new WSDataFrame(Encoding.UTF8.GetBytes(
            """
            {"event":"instance_log","meta":{"instance_id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},"data":{"log":"ignored"},"time":1717171717000}
            """))
        {
            Opcode = WSDataType.Binary,
            FIN = true
        };

        await plugin.OnWebSocketReceived(null!, new WSDataFrameEventArgs(frame));

        Assert.Equal(0, eventCount);
        Assert.Equal(0, actionCount);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public async Task OnWebSocketReceived_TextFrame_LargeInstanceLogPayload_DispatchesFullLog()
    {
        var plugin = new WsReceivedPlugin();
        var longLog = new string('x', 100_000);
        InstanceLogEventData? receivedData = null;

        plugin.OnEventReceived += (_, _, _, data) => receivedData = Assert.IsType<InstanceLogEventData>(data);

        var envelope = JsonSerializer.Serialize(new
        {
            @event = "instance_log",
            meta = new { instance_id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" },
            data = new { log = longLog },
            time = 1717171717000L
        });

        var frame = new WSDataFrame(Encoding.UTF8.GetBytes(envelope))
        {
            Opcode = WSDataType.Text,
            FIN = true
        };

        await plugin.OnWebSocketReceived(null!, new WSDataFrameEventArgs(frame));

        Assert.NotNull(receivedData);
        Assert.Equal(longLog, receivedData!.Log);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public async Task ClientInboundErrors_OnWebSocketReceived_MalformedTextEnvelope_ThrowsJsonException()
    {
        var plugin = new WsReceivedPlugin();
        var eventCount = 0;
        var actionCount = 0;

        plugin.OnEventReceived += (_, _, _, _) => eventCount++;
        plugin.OnActionResponseReceived += _ => actionCount++;

        var frame = new WSDataFrame(Encoding.UTF8.GetBytes("{\"event\":\"instance_log\""))
        {
            Opcode = WSDataType.Text,
            FIN = true
        };

        await Assert.ThrowsAnyAsync<JsonException>(async () =>
            await plugin.OnWebSocketReceived(null!, new WSDataFrameEventArgs(frame)));

        Assert.Equal(0, eventCount);
        Assert.Equal(0, actionCount);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_DetectEnvelopeType_MalformedJson_ThrowsJsonException()
    {
        var malformed = "{\"event\":\"instance_log\"";

        Assert.ThrowsAny<JsonException>(() => WsReceivedPlugin.DetectEnvelopeType(malformed));
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_DetectEnvelopeType_NonObjectRoot_ThrowsJsonException()
    {
        var malformed = "[]";

        Assert.Throws<JsonException>(() => WsReceivedPlugin.DetectEnvelopeType(malformed));
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseEventPacket_ExplicitNullMeta_PreservesExplicitNullBufferForDownstreamHandling()
    {
        var envelope =
            """
            {
              "event": "instance_log",
              "meta": null,
              "data": { "log": "hello" },
              "time": 1717171717000
            }
            """;

        var packet = WsReceivedPlugin.ParseEventPacket(envelope);

        Assert.True(packet.EventMeta.HasValue);
        Assert.True(packet.EventMeta.Value.IsExplicitJsonNull);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseEventPacket_ExplicitNullData_PreservesExplicitNullBufferForDownstreamHandling()
    {
        var envelope =
            """
            {
              "event": "daemon_report",
              "meta": null,
              "data": null,
              "time": 1717171717000
            }
            """;

        var packet = WsReceivedPlugin.ParseEventPacket(envelope);

        Assert.True(packet.EventData.HasValue);
        Assert.True(packet.EventData.Value.IsExplicitJsonNull);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseActionResponse_InvalidGuid_ParsesToGuidEmpty()
    {
        var envelope =
            """
            {
              "status": "ok",
              "retcode": 0,
              "data": {},
              "message": "OK",
              "id": "not-a-guid"
            }
            """;

        var response = WsReceivedPlugin.ParseActionResponse(envelope);

        Assert.Equal(Guid.Empty, response.Id);
    }

    [Fact]
    [Trait("Category", "ClientInboundErrors")]
    public void ClientInboundErrors_ParseActionResponse_MalformedJson_ThrowsJsonException()
    {
        var malformed = "{\"status\":\"ok\"";

        Assert.ThrowsAny<JsonException>(() => WsReceivedPlugin.ParseActionResponse(malformed));
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    public void MaterializeEventData_MissingMetaAndStructuredData_ForDaemonReport_ParsesDataAndLeavesMetaNull()
    {
        var envelope =
            """
            {
              "event": "daemon_report",
              "meta": null,
              "data": {
                "report": {
                  "os": { "name": "Windows", "arch": "x64" },
                  "cpu": { "vendor": "GenuineIntel", "name": "Intel(R)", "count": 16, "usage": 0.25 },
                  "mem": { "total": 1024, "free": 512 },
                  "drive": { "drive_format": "NTFS", "total": 1000, "free": 500 },
                  "start_time_stamp": 1717171717000
                }
              },
              "time": 1717171717999
            }
            """;

        var packet = WsReceivedPlugin.ParseEventPacket(envelope);

        Assert.True(packet.EventMeta.HasValue);
        Assert.True(packet.EventMeta.Value.IsExplicitJsonNull);
        var data = WsReceivedPlugin.MaterializeEventData(packet.EventType, packet.EventData);

        var typed = Assert.IsType<DaemonReportEventData>(data);
        Assert.Equal("Windows", typed.Report.Os.Name);
    }

}
