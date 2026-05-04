using System;
using System.Collections.Generic;
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
        ActionResponse? ActionResponse,
        BinaryUploadResponse? BinaryUploadResponse)
    {
        public static ParsedInboundEnvelope Unknown { get; } = new(InboundEnvelopeType.Unknown, null, null, null);

        public static ParsedInboundEnvelope FromEvent(EventPacket packet)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Event, packet, null, null);
        }

        public static ParsedInboundEnvelope FromAction(ActionResponse response)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.Action, null, response, null);
        }

        public static ParsedInboundEnvelope FromBinaryUpload(BinaryUploadResponse response)
        {
            return new ParsedInboundEnvelope(InboundEnvelopeType.BinaryUpload, null, null, response);
        }
    }

    public record struct BinaryUploadResponse(Guid FileId, bool Done, long Received, string? Error);

    internal enum InboundEnvelopeType
    {
        Unknown,
        Event,
        Action,
        BinaryUpload
    }

    // TODO 中继包支持
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
                DispatchEvent(inbound.EventPacket!);
            else if (inbound.EnvelopeType == InboundEnvelopeType.Action)
                DispatchAction(inbound.ActionResponse!);
            else if (inbound.EnvelopeType == InboundEnvelopeType.BinaryUpload)
                DispatchBinaryUpload(inbound.BinaryUploadResponse!.Value);
        }

        await e.InvokeNext();
    }

    public event Action<EventType, long, IEventMeta?, IEventData?>? OnEventReceived;
    public event Action<ActionResponse>? OnActionResponseReceived;
    public event Action<BinaryUploadResponse>? OnBinaryUploadResponseReceived;

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

    private void DispatchBinaryUpload(BinaryUploadResponse response)
    {
        OnBinaryUploadResponseReceived?.Invoke(response);
    }

    private void DispatchAction(string received)
    {
        DispatchAction(ParseActionResponse(received));
    }
}
