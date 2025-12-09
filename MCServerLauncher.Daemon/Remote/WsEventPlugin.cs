using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsEventPlugin : PluginBase, IWsPlugin, IWebSocketClosingPlugin
{
    private readonly IEventService _eventService;

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
        foreach (var (_, context) in Container)
        {
            if (!context.IsSubscribedEvent(type, meta)) return;

            if (meta != null)
            {
                await PrivateSendEvent(type, meta, data, context.GetWebsocket()); // 单一发送(只有带meta的事件类型才会触发)
            }
            else
            {
                var eventMetas = context.GetEventMetas(type).ToArray();

                if (eventMetas.Length == 0)
                    await PrivateSendEvent(type, meta, data, context.GetWebsocket()); // 单一事件(不带meta的事件类型)
                else // 批量发送(只有带meta的事件类型才会触发)
                    // TODO 合并发送
                    foreach (var eventMeta in eventMetas)
                        await PrivateSendEvent(type, eventMeta, data, context.GetWebsocket());
            }
        }
    }

    private static async ValueTask PrivateSendEvent(EventType type, IEventMeta? meta, IEventData? data, IWebSocket ws)
    {
        var packet = new EventPacket
        {
            EventType = type,
            EventMeta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings)),
            EventData = data is null ? null : JToken.FromObject(data, JsonSerializer.Create(JsonSettings.Settings))
        };
        await ws.SendAsync(JsonConvert.SerializeObject(packet, DaemonJsonSettings.Settings));
    }
}