using System;
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
    internal enum InboundEnvelopeType
    {
        Unknown,
        Event,
        Action
    }

    private readonly ConnectionPendingRequests _pendingRequests;

    internal WsReceivedPlugin(ConnectionPendingRequests requests)
    {
        _pendingRequests = requests;
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
        var envelopeType = DetectEnvelopeType(received);
        if (envelopeType == InboundEnvelopeType.Event)
            DispatchEvent(received);
        else if (envelopeType == InboundEnvelopeType.Action)
            DispatchAction(received);

        await e.InvokeNext();
    }

    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;

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

    internal static EventPacket ParseEventPacket(string received)
    {
        using var document = JsonDocument.Parse(received);
        var root = document.RootElement;
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

    internal static ActionResponse ParseActionResponse(string received)
    {
        var response = System.Text.Json.JsonSerializer.Deserialize<ActionResponse>(received, DaemonClientRpcJsonBoundary.StjOptions)
                       ?? throw new JsonException("Received action envelope could not be materialized.");

        return response.Data.HasValue
            ? response with { Data = response.Data.Value.Clone() }
            : response;
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
        return eventType switch
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
    }

    private void DispatchEvent(string received)
    {
        try
        {
            var packet = ParseEventPacket(received);
            var eventType = packet.EventType;

            // TODO 改为异步? BeginInvoke?
            OnEventReceived?.Invoke(
                eventType,
                packet.Timestamp,
                MaterializeEventMeta(eventType, packet.EventMeta),
                MaterializeEventData(eventType, packet.EventData)
            );
        }
        catch (JsonException)
        {
            Log.Fatal(
                "[ClientConnection] [ReceiveLoop] Received unexpected event packet: {0}\nmay be connected to a unofficial daemon?",
                received);
            throw;
        }
    }

    private void DispatchAction(string received)
    {
        var response = ParseActionResponse(received);
        if (response.Id == Guid.Empty)
        {
            Log.Error("[ClientConnection] [ReceiveLoop] Received Id=Guid.Empty message: {0}.", response.Message);
            throw new ArgumentNullException(nameof(response.Id));
        }

        if (_pendingRequests.TryRemovePending(response.Id, out var pending))
            pending.SetResult(response);
        else
            Log.Warning($"[ClientConnection] Received canceled action's result: {received},\nignore it.");
    }
}
