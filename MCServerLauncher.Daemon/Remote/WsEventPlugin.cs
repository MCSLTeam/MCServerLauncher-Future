using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsEventPlugin : PluginBase, IWsPlugin, IWebSocketClosingPlugin
{
    private readonly IEventService _eventService;

    private readonly record struct PreparedEventPayload(JsonPayloadBuffer? EventMeta, JsonPayloadBuffer? EventData);

    public WsEventPlugin(IEventService eventService, WsContextContainer container, IHttpService httpService)
    {
        _eventService = eventService;
        Container = container;
        HttpService = httpService;

        // TODO 建议使用批量收割方式，而不是1次事件就触发一次
        _eventService.Signal += async (e, m, d) => await OnEventSignalReceived(e, m, d);
    }

    public async Task OnWebSocketClosing(IWebSocket webSocket, ClosingEventArgs e)
    {
        this.GetWsContext(webSocket).UnsubscribeAllEvents();
        await e.InvokeNext();
    }

    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }

    private async Task OnEventSignalReceived(EventType type, IEventMeta? meta, IEventData? data)
    {
        if (meta is not null)
        {
            var preparedPayload = PreparePayload(meta, data);
            var wirePayload = BuildWirePayloadUtf8(type, preparedPayload.EventMeta, preparedPayload.EventData);

            foreach (var context in EnumerateSubscribedContexts(Container, type, meta))
            {
                await SendTextFrameAsync(context.GetWebsocket(), wirePayload);
            }

            return;
        }

        var eventDataBuffer = ToPayloadBuffer(data);
        var noMetaWirePayload = BuildWirePayloadUtf8(type, null, eventDataBuffer);

        foreach (var context in EnumerateSubscribedContexts(Container, type, meta))
        {
            var webSocket = context.GetWebsocket();
            var eventMetas = context.GetEventMetas(type).ToArray();

            if (eventMetas.Length == 0)
                await SendTextFrameAsync(webSocket, noMetaWirePayload); // 单一事件(不带meta的事件类型)
            else // 批量发送(只有带meta的事件类型才会触发)
                // TODO 合并发送
                foreach (var eventMeta in eventMetas)
                    await SendTextFrameAsync(webSocket, BuildWirePayloadUtf8(type, ToPayloadBuffer(eventMeta), eventDataBuffer));
        }
    }

    private static IEnumerable<WsContext> EnumerateSubscribedContexts(WsContextContainer container, EventType type, IEventMeta? meta)
    {
        foreach (var (_, context) in container)
        {
            if (!context.IsSubscribedEvent(type, meta))
                continue;

            yield return context;
        }
    }

    private static async ValueTask PrivateSendEvent(EventType type, IEventMeta? meta, IEventData? data, IWebSocket ws)
    {
        var preparedPayload = PreparePayload(meta, data);
        await PrivateSendPreparedEvent(type, preparedPayload.EventMeta, preparedPayload.EventData, ws);
    }

    private static async ValueTask PrivateSendPreparedEvent(
        EventType type,
        JsonPayloadBuffer? eventMeta,
        JsonPayloadBuffer? eventData,
        IWebSocket ws)
    {
        await SendTextFrameAsync(ws, BuildWirePayloadUtf8(type, eventMeta, eventData));
    }

    private static PreparedEventPayload PreparePayload(object? meta, object? data)
    {
        return new PreparedEventPayload(ToPayloadBuffer(meta), ToPayloadBuffer(data));
    }

    private static byte[] BuildWirePayloadUtf8(EventType type, JsonPayloadBuffer? eventMeta, JsonPayloadBuffer? eventData)
    {
        var packet = new EventPacket
        {
            EventType = type,
            EventMeta = eventMeta,
            EventData = eventData
        };
        return StjJsonSerializer.SerializeToUtf8Bytes(packet, DaemonRpcJsonBoundary.StjOptions);
    }

    private static async ValueTask SendTextFrameAsync(IWebSocket webSocket, byte[] utf8Payload)
    {
        var frame = new WSDataFrame(utf8Payload)
        {
            Opcode = WSDataType.Text,
            FIN = true
        };
        await webSocket.SendAsync(frame);
    }
    private static JsonPayloadBuffer? ToPayloadBuffer(object? payload)
    {
        if (payload is null)
            return null;

        var element = StjJsonSerializer.SerializeToElement(payload, DaemonRpcJsonBoundary.StjOptions);
        return new JsonPayloadBuffer(element);
    }
}
