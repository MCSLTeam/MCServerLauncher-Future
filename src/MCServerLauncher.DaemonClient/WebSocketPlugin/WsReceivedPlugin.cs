using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Notification;
using MCServerLauncher.Common.ProtoType.Relay;
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
        ActionResponse? ActionResponse,
        BinaryUploadResponse? BinaryUploadResponse,
        NotificationPacket? NotificationPacket,
        RelayPacket? RelayPacket)
    {
        public static ParsedInboundEnvelope Unknown { get; } = new(InboundEnvelopeType.Unknown, null, null, null, null, null);

        public static ParsedInboundEnvelope FromEvent(EventPacket packet)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Event, packet, null, null, null, null);
        }

        public static ParsedInboundEnvelope FromAction(ActionResponse response)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Action, null, response, null, null, null);
        }

        public static ParsedInboundEnvelope FromBinaryUpload(BinaryUploadResponse response)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.BinaryUpload, null, null, response, null, null);
        }

        public static ParsedInboundEnvelope FromNotification(NotificationPacket packet)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Notification, null, null, null, packet, null);
        }

        public static ParsedInboundEnvelope FromRelay(RelayPacket packet)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Relay, null, null, null, null, packet);
        }
    }

    public record struct BinaryUploadResponse(Guid FileId, bool Done, long Received, string? Error);

    internal enum InboundEnvelopeType
    {
        Unknown,
        Event,
        Action,
        BinaryUpload,
        Notification,
        Relay
    }

    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (!e.DataFrame.IsText)
        {
            await e.InvokeNext();
            return;
        }

        var payloadData = e.DataFrame.PayloadData;
        var inbounds = ParseInboundEnvelopesFromBytes(payloadData);

        foreach (var inbound in inbounds)
        {
            if (inbound.EnvelopeType == InboundEnvelopeType.Event)
                await DispatchEventAsync(inbound.EventPacket!);
            else if (inbound.EnvelopeType == InboundEnvelopeType.Action)
                await DispatchActionAsync(inbound.ActionResponse!);
            else if (inbound.EnvelopeType == InboundEnvelopeType.BinaryUpload)
                await DispatchBinaryUploadAsync(inbound.BinaryUploadResponse!.Value);
            else if (inbound.EnvelopeType == InboundEnvelopeType.Notification)
                await DispatchNotificationAsync(inbound.NotificationPacket!);
            else if (inbound.EnvelopeType == InboundEnvelopeType.Relay)
                await DispatchRelayAsync(inbound.RelayPacket!);
        }

        await e.InvokeNext();
    }

    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;
    public event Action<ActionResponse>? OnActionResponseReceived;
    public event Action<BinaryUploadResponse>? OnBinaryUploadResponseReceived;
    public event Action<NotificationPacket>? OnNotificationReceived;
    public event Action<RelayPacket>? OnRelayReceived;
    public event Func<EventType, long, IEventMeta?, IEventData?, Task>? OnEventReceivedAsync;
    public event Func<ActionResponse, Task>? OnActionResponseReceivedAsync;
    public event Func<BinaryUploadResponse, Task>? OnBinaryUploadResponseReceivedAsync;
    public event Func<NotificationPacket, Task>? OnNotificationReceivedAsync;
    public event Func<RelayPacket, Task>? OnRelayReceivedAsync;

    internal static InboundEnvelopeType DetectEnvelopeType(string received)
    {
        return DetectEnvelopeTypeFromBytes(Encoding.UTF8.GetBytes(received));
    }

    internal static InboundEnvelopeType DetectEnvelopeTypeFromBytes(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            // Batched events - check first element
            if (root.GetArrayLength() > 0 && root[0].TryGetProperty("event", out _))
                return InboundEnvelopeType.Event;
            return InboundEnvelopeType.Unknown;
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Inbound message root must be a JSON object or array.");

        if (root.TryGetProperty("event", out _)) return InboundEnvelopeType.Event;
        if (root.TryGetProperty("status", out _)) return InboundEnvelopeType.Action;
        if (root.TryGetProperty("file_id", out _)) return InboundEnvelopeType.BinaryUpload;
        if (root.TryGetProperty("notification", out _)) return InboundEnvelopeType.Notification;
        if (root.TryGetProperty("relay", out _)) return InboundEnvelopeType.Relay;
        return InboundEnvelopeType.Unknown;
    }

    internal static ParsedInboundEnvelope ParseInboundEnvelope(string received)
    {
        return ParseInboundEnvelopeFromBytes(Encoding.UTF8.GetBytes(received));
    }

    internal static ParsedInboundEnvelope ParseInboundEnvelopeFromBytes(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
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
                    Encoding.UTF8.GetString(utf8Json.Span));
                throw;
            }
        }

        if (root.TryGetProperty("status", out _))
            return ParsedInboundEnvelope.FromAction(ParseActionResponse(root));
        if (root.TryGetProperty("file_id", out _))
            return ParsedInboundEnvelope.FromBinaryUpload(ParseBinaryUploadResponse(root));
        if (root.TryGetProperty("notification", out _))
            return ParsedInboundEnvelope.FromNotification(ParseNotificationPacket(root));
        if (root.TryGetProperty("relay", out _))
            return ParsedInboundEnvelope.FromRelay(ParseRelayPacket(root));

        return ParsedInboundEnvelope.Unknown;
    }

    internal static List<ParsedInboundEnvelope> ParseInboundEnvelopesFromBytes(ReadOnlyMemory<byte> utf8Json)
    {
        var result = new List<ParsedInboundEnvelope>();
        using var document = JsonDocument.Parse(utf8Json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            // Batched events
            foreach (var element in root.EnumerateArray())
            {
                if (element.TryGetProperty("event", out _))
                {
                    try
                    {
                        result.Add(ParsedInboundEnvelope.FromEvent(ParseEventPacket(element)));
                    }
                    catch (JsonException)
                    {
                        Log.Fatal(
                            "[ClientConnection] [ReceiveLoop] Received unexpected event packet in batch: {0}",
                            element.GetRawText());
                        throw;
                    }
                }
            }
            return result;
        }

        // Single envelope (legacy path)
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Inbound message root must be a JSON object or array.");

        if (root.TryGetProperty("event", out _))
        {
            try
            {
                result.Add(ParsedInboundEnvelope.FromEvent(ParseEventPacket(root)));
            }
            catch (JsonException)
            {
                Log.Fatal(
                    "[ClientConnection] [ReceiveLoop] Received unexpected event packet: {0}\nmay be connected to a unofficial daemon?",
                    Encoding.UTF8.GetString(utf8Json.Span));
                throw;
            }
            return result;
        }

        if (root.TryGetProperty("status", out _))
        {
            result.Add(ParsedInboundEnvelope.FromAction(ParseActionResponse(root)));
            return result;
        }

        if (root.TryGetProperty("file_id", out _))
        {
            result.Add(ParsedInboundEnvelope.FromBinaryUpload(ParseBinaryUploadResponse(root)));
            return result;
        }

        if (root.TryGetProperty("notification", out _))
        {
            result.Add(ParsedInboundEnvelope.FromNotification(ParseNotificationPacket(root)));
            return result;
        }

        if (root.TryGetProperty("relay", out _))
        {
            result.Add(ParsedInboundEnvelope.FromRelay(ParseRelayPacket(root)));
            return result;
        }

        result.Add(ParsedInboundEnvelope.Unknown);
        return result;
    }

    internal static EventPacket ParseEventPacket(string received)
    {
        return ParseEventPacketFromBytes(Encoding.UTF8.GetBytes(received));
    }

    internal static EventPacket ParseEventPacketFromBytes(ReadOnlyMemory<byte> utf8Json)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return ParseInboundEnvelopeFromBytes(utf8Json).EventPacket!;

        var startTimestamp = Stopwatch.GetTimestamp();
        var packet = ParseInboundEnvelopeFromBytes(utf8Json).EventPacket!;
        instrumentation.OnInboundEventPacketParse(
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            utf8Json.Length);
        return packet;
    }

    internal static ActionResponse ParseActionResponse(string received)
    {
        return ParseActionResponseFromBytes(Encoding.UTF8.GetBytes(received));
    }

    internal static ActionResponse ParseActionResponseFromBytes(ReadOnlyMemory<byte> utf8Json)
    {
        if (!DaemonClientTransportInstrumentationScope.TryGetCurrent(out var instrumentation))
            return ParseInboundEnvelopeFromBytes(utf8Json).ActionResponse!;

        var startTimestamp = Stopwatch.GetTimestamp();
        var response = ParseInboundEnvelopeFromBytes(utf8Json).ActionResponse!;
        instrumentation.OnInboundActionResponseParse(
            DaemonClientTransportStopwatch.GetElapsedTime(startTimestamp),
            utf8Json.Length);
        return response;
    }

    internal static IEventMeta? MaterializeEventMeta(EventType eventType, JsonPayloadBuffer? metaToken)
    {
        return eventType switch
        {
            EventType.InstanceLog when metaToken is null || metaToken.Value.IsExplicitJsonNull => null,
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
            EventType.InstanceLog when dataToken is null || dataToken.Value.IsExplicitJsonNull => null,
            EventType.InstanceLog => System.Text.Json.JsonSerializer.Deserialize<InstanceLogEventData>(
                dataToken!.Value.Value,
                DaemonClientRpcJsonBoundary.StjOptions),
            EventType.DaemonReport when dataToken is null || dataToken.Value.IsExplicitJsonNull => null,
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

    private static BinaryUploadResponse ParseBinaryUploadResponse(JsonElement root)
    {
        var fileId = root.GetProperty("file_id").GetGuid();
        var done = root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean();
        var received = root.TryGetProperty("received", out var receivedElement) ? receivedElement.GetInt64() : 0L;
        var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;

        return new BinaryUploadResponse(fileId, done, received, error);
    }

    private static NotificationPacket ParseNotificationPacket(JsonElement root)
    {
        return root.Deserialize<NotificationPacket>(DaemonClientRpcJsonBoundary.StjOptions)
               ?? throw new JsonException("Received notification envelope could not be materialized.");
    }

    private static RelayPacket ParseRelayPacket(JsonElement root)
    {
        var packet = root.Deserialize<RelayPacket>(DaemonClientRpcJsonBoundary.StjOptions)
                     ?? throw new JsonException("Received relay envelope could not be materialized.");

        return packet.Data.HasValue
            ? packet with { Data = packet.Data.Value.Clone() }
            : packet;
    }

    private async Task DispatchEventAsync(EventPacket packet)
    {
        var eventType = packet.EventType;
        var meta = MaterializeEventMeta(eventType, packet.EventMeta);
        var data = MaterializeEventData(eventType, packet.EventData);

        OnEventReceived?.Invoke(
            eventType,
            packet.Timestamp,
            meta,
            data
        );

        if (OnEventReceivedAsync is { } handlers)
        {
            foreach (Func<EventType, long, IEventMeta?, IEventData?, Task> handler in handlers.GetInvocationList())
                await handler(eventType, packet.Timestamp, meta, data);
        }
    }

    private async Task DispatchActionAsync(ActionResponse response)
    {
        if (response.Id == Guid.Empty)
        {
            Log.Error("[ClientConnection] [ReceiveLoop] Received Id=Guid.Empty message: {0}.", response.Message);
            throw new ArgumentNullException(nameof(response.Id));
        }

        OnActionResponseReceived?.Invoke(response);
        await InvokeAsync(OnActionResponseReceivedAsync, response);
    }

    private async Task DispatchBinaryUploadAsync(BinaryUploadResponse response)
    {
        OnBinaryUploadResponseReceived?.Invoke(response);
        await InvokeAsync(OnBinaryUploadResponseReceivedAsync, response);
    }

    private async Task DispatchNotificationAsync(NotificationPacket packet)
    {
        OnNotificationReceived?.Invoke(packet);
        await InvokeAsync(OnNotificationReceivedAsync, packet);
    }

    private async Task DispatchRelayAsync(RelayPacket packet)
    {
        OnRelayReceived?.Invoke(packet);
        await InvokeAsync(OnRelayReceivedAsync, packet);
    }

    private void DispatchAction(string received)
    {
        DispatchActionAsync(ParseActionResponse(received)).GetAwaiter().GetResult();
    }

    private static async Task InvokeAsync<T>(Func<T, Task>? handlers, T arg)
    {
        if (handlers == null) return;

        foreach (Func<T, Task> handler in handlers.GetInvocationList())
            await handler(arg);
    }
}
