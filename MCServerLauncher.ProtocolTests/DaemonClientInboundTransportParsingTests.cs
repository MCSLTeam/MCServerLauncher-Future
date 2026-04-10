using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.DaemonClient.WebSocketPlugin;

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
    [Trait("Category", "ClientInbound")]
    [Trait("Category", "CleanupValidation")]
    public void DetectEnvelopeType_EventShapedPayloadMissingMetaAndData_ReturnsEventWithoutThrowing()
    {
        var incompleteEventEnvelope = "{\"event\":\"instance_log\"}";

        var detected = WsReceivedPlugin.DetectEnvelopeType(incompleteEventEnvelope);

        Assert.Equal(WsReceivedPlugin.InboundEnvelopeType.Event, detected);
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
    [Trait("Category", "CleanupValidation")]
    public void InboundReceivePath_UsesSingleParseInboundAdapterBeforeDispatch()
    {
        var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs"));

        Assert.Contains("var inbound = ParseInboundEnvelope(received);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var envelopeType = DetectEnvelopeType(received);", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ClientInbound")]
    [Trait("Category", "CleanupValidation")]
    public void InboundReceivePath_DoesNotUseNewtonsoftDomParsingApis()
    {
        var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "MCServerLauncher.DaemonClient/WebSocketPlugin/WsReceivedPlugin.cs"));

        Assert.DoesNotContain("JObject.Parse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("json.SelectToken(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToObject<EventPacket>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToObject<ActionResponse>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Create(", source, StringComparison.Ordinal);
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

    private static string ResolveRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MCServerLauncher.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
