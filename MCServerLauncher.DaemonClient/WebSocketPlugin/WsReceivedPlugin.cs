using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.DaemonClient.Serialization;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.WebSocketPlugin;

public class WsReceivedPlugin : PluginBase, IWebSocketReceivedPlugin
{
    internal readonly record struct ParsedInboundEnvelope(
        InboundEnvelopeType EnvelopeType,
        EventPacket? EventPacket,
        ActionResponse? ActionResponse)
    {
        public static ParsedInboundEnvelope Unknown { get; } = new(InboundEnvelopeType.Unknown, null, null);

        public static ParsedInboundEnvelope FromEvent(EventPacket packet)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Event, packet, null);
        }

        public static ParsedInboundEnvelope FromAction(ActionResponse response)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Action, null, response);
        }
    }

    internal enum InboundEnvelopeType
    {
        Unknown,
        Event,
        Action
    }

    // TODO 中继包支持
    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (!e.DataFrame.IsText)
        {
            await e.InvokeNext();
            return;
        }

        var received = e.DataFrame.ToText();
        var inbound = ParseInboundEnvelope(received);
        if (inbound.EnvelopeType == InboundEnvelopeType.Event)
            DispatchEvent(inbound.EventPacket!);
        else if (inbound.EnvelopeType == InboundEnvelopeType.Action)
            DispatchAction(inbound.ActionResponse!);

        await e.InvokeNext();
    }

    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;
    public event Action<ActionResponse>? OnActionResponseReceived;

    internal static InboundEnvelopeType DetectEnvelopeType(string received)
    {
        using var document = JsonDocument.Parse(received);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Inbound message root must be a JSON object.");

        if (root.TryGetProperty("event", out _)) return InboundEnvelopeType.Event;
        if (root.TryGetProperty("status", out _)) return InboundEnvelopeType.Action;
        return InboundEnvelopeType.Unknown;
    }

    internal static ParsedInboundEnvelope ParseInboundEnvelope(string received)
    {
        using var document = JsonDocument.Parse(received);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Inbound message root must be a JSON object.");

        if (root.TryGetProperty("event", out _))
        {
            try
            {
                return ParsedInboundEnvelope.FromEvent(ParseEventPacket(root));
            }
            catch (JsonException)
            {
                Log.Fatal(
                    "[ClientConnection] [ReceiveLoop] Received unexpected event packet: {0}\nmay be connected to a unofficial daemon?",
                    received);
                throw;
            }
        }

        if (root.TryGetProperty("status", out _))
            return ParsedInboundEnvelope.FromAction(ParseActionResponse(root));

        return ParsedInboundEnvelope.Unknown;
    }

    internal static EventPacket ParseEventPacket(string received)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return ParseInboundEnvelope(received).EventPacket!;

        var startTimestamp = Stopwatch.GetTimestamp();
        var packet = ParseInboundEnvelope(received).EventPacket!;
        instrumentation.OnInboundEventPacketParse(
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            Encoding.UTF8.GetByteCount(received));
        return packet;
    }

    internal static ActionResponse ParseActionResponse(string received)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return ParseInboundEnvelope(received).ActionResponse!;

        var startTimestamp = Stopwatch.GetTimestamp();
        var response = ParseInboundEnvelope(received).ActionResponse!;
        instrumentation.OnInboundActionResponseParse(
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            Encoding.UTF8.GetByteCount(received));
        return response;
    }

    internal static IEventMeta? MaterializeEventMeta(EventType eventType, JsonPayloadBuffer? metaToken)
    {
        return eventType switch
        {
            EventType.InstanceLog when metaToken is null => null,
            EventType.InstanceLog when metaToken.Value.IsExplicitJsonNull => throw new ArgumentException("event meta payload is explicit json null"),
            EventType.InstanceLog => System.Text.Json.JsonSerializer.Deserialize<InstanceLogEventMeta>(
                metaToken!.Value.Value,
                DaemonClientRpcJsonBoundary.StjOptions),
            _ => null
        };
    }

    internal static IEventData? MaterializeEventData(EventType eventType, JsonPayloadBuffer? dataToken)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return MaterializeEventDataCore(eventType, dataToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        var data = MaterializeEventDataCore(eventType, dataToken);
        instrumentation.OnEventDataMaterialized(
            eventType,
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            data is not null);

        return data;
    }

    private static IEventData? MaterializeEventDataCore(EventType eventType, JsonPayloadBuffer? dataToken)
    {
        IEventData? data = eventType switch
        {
            EventType.InstanceLog when dataToken is null => null,
            EventType.InstanceLog when dataToken.Value.IsExplicitJsonNull => throw new ArgumentException("event data payload is explicit json null"),
            EventType.InstanceLog => System.Text.Json.JsonSerializer.Deserialize<InstanceLogEventData>(
                dataToken!.Value.Value,
                DaemonClientRpcJsonBoundary.StjOptions),
            EventType.DaemonReport when dataToken is null => null,
            EventType.DaemonReport when dataToken.Value.IsExplicitJsonNull => throw new ArgumentException("event data payload is explicit json null"),
            EventType.DaemonReport => System.Text.Json.JsonSerializer.Deserialize<DaemonReportEventData>(
                dataToken!.Value.Value,
                DaemonClientRpcJsonBoundary.StjOptions),
            _ => null
        };

        return data;
    }

    private static EventPacket ParseEventPacket(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Inbound event message root must be a JSON object.");

        if (!root.TryGetProperty("event", out var eventTypeElement))
            throw new JsonException("Missing required property 'event'.");
        if (!root.TryGetProperty("meta", out var metaElement))
            throw new JsonException("Missing required property 'meta'.");
        if (!root.TryGetProperty("data", out var dataElement))
            throw new JsonException("Missing required property 'data'.");

        var eventType = eventTypeElement.Deserialize<EventType>(DaemonClientRpcJsonBoundary.StjOptions);
        var timestamp = root.TryGetProperty("time", out var timeElement)
            ? timeElement.Deserialize<long>(DaemonClientRpcJsonBoundary.StjOptions)
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return new EventPacket
        {
            EventType = eventType,
            EventMeta = new JsonPayloadBuffer(metaElement.Clone()),
            EventData = new JsonPayloadBuffer(dataElement.Clone()),
            Timestamp = timestamp
        };
    }

    private static ActionResponse ParseActionResponse(JsonElement root)
    {
        var response = root.Deserialize<ActionResponse>(DaemonClientRpcJsonBoundary.StjOptions)
                       ?? throw new JsonException("Received action envelope could not be materialized.");

        return response.Data.HasValue
            ? response with { Data = response.Data.Value.Clone() }
            : response;
    }

    private void DispatchEvent(EventPacket packet)
    {
        var eventType = packet.EventType;

        // TODO 改为异步? BeginInvoke?
        OnEventReceived?.Invoke(
            eventType,
            packet.Timestamp,
            MaterializeEventMeta(eventType, packet.EventMeta),
            MaterializeEventData(eventType, packet.EventData)
        );
    }

    private void DispatchAction(ActionResponse response)
    {
        if (response.Id == Guid.Empty)
        {
            Log.Error("[ClientConnection] [ReceiveLoop] Received Id=Guid.Empty message: {0}.", response.Message);
            throw new ArgumentNullException(nameof(response.Id));
        }

        OnActionResponseReceived?.Invoke(response);
    }

    private void DispatchAction(string received)
    {
        DispatchAction(ParseActionResponse(received));
    }
}
