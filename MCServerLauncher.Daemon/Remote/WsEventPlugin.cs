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

    private async Task OnEventSignalReceived(EventType type, IEventFilter? filter, IEventData? data)
    {
        foreach (var (_, context) in Container)
        {
            var id = context.GetEventID(type, filter);
            if (id.IsSome(out var guid))
            {
                await PrivateSendEvent(guid, type, data, context.GetWebsocket());
            }
        }
    }

    private static async ValueTask PrivateSendEvent(
        Guid subscriber,
        EventType type,
        IEventData? data,
        IWebSocket ws
    )
    {
        var packet = new EventPacket
        {
            EventType = type,
            Subscriber = subscriber,
            EventData = data is null ? null : JToken.FromObject(data, JsonSerializer.Create(JsonSettings.Settings))
        };
        await ws.SendAsync(JsonConvert.SerializeObject(packet, DaemonJsonSettings.Settings));
    }
}