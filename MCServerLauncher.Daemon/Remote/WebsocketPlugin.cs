using System.Collections.Concurrent;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

/// <summary>
///     线程安全的ws服务上下文
/// </summary>
public class WsServiceContext
{
    private readonly ConcurrentDictionary<EventType, HashSet<IEventMeta>> _subscribedEvents = new();

    public Permissions Permissions { get; set; } = Permissions.Never;

    public void SubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (!_subscribedEvents.TryGetValue(type, out var set))
        {
            set = new HashSet<IEventMeta>();
            _subscribedEvents.TryAdd(type, set);
        }

        if (meta != null) set.Add(meta);
    }

    public void UnsubscribeEvent(EventType type, IEventMeta? meta)
    {
        if (meta != null)
        {
            if (_subscribedEvents.TryGetValue(type, out var set))
            {
                if (set.Count > 1) set.Remove(meta);
                else _subscribedEvents.TryRemove(type, out _);
            }

            ;
        }
        else
        {
            _subscribedEvents.TryRemove(type, out _);
        }
    }

    public bool IsSubscribedEvent(EventType type, IEventMeta? meta)
    {
        return _subscribedEvents.TryGetValue(type, out var set) && (meta == null || set.Contains(meta));
    }

    public IEnumerable<IEventMeta> GetEventMetas(EventType type)
    {
        return _subscribedEvents.TryGetValue(type, out var set)
            ? new HashSet<IEventMeta>(set)
            : Enumerable.Empty<IEventMeta>();
    }

    public void UnsubscribeAllEvents()
    {
        _subscribedEvents.Clear();
    }
}

public class WebsocketPlugin : PluginBase, IWebSocketHandshakedPlugin, IWebSocketReceivedPlugin, IWebSocketClosedPlugin
{
    private readonly IActionService _actionService;

    private readonly WsServiceContext _context;
    private readonly IEventService _eventService;
    private readonly IHttpService _httpService;
    private readonly IWebJsonConverter _webJsonConverter;

    private string _id;

    public WebsocketPlugin(IActionService actionService, IEventService eventService,
        IWebJsonConverter webJsonConverter, IHttpService httpService)
    {
        _actionService = actionService;
        _eventService = eventService;
        _webJsonConverter = webJsonConverter;
        _httpService = httpService;

        _id = "";
        _context = new WsServiceContext();

        _eventService.Signal += OnEventReceived;
    }

    private IWebSocket? WebSocket => _httpService.TryGetClient(_id, out var client) ? client.WebSocket : null;

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        _context.UnsubscribeAllEvents();
        Log.Debug("[Remote] Websocket connection from {0} disconnected", webSocket.Client.GetIPPort());

        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        var token = e.Context.Request.Query["token"]!;
        // TODO normalization
        _context.Permissions =
            new Permissions(token == AppConfig.Get().MainToken ? "*" : JwtUtils.ExtractPermissions(token)!);

        if (webSocket.Client is TcpSessionClientBase tcpClientBase) _id = tcpClientBase.Id;

        // get peer ip
        Log.Debug("[Remote] Accept token: \"{0}...\" from {1} with Id={2}", token[..5], webSocket.Client.GetIPPort(),
            _id);

        await e.InvokeNext();
    }

    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var payload = e.DataFrame.ToText();

            if (!TryParseMessage(payload, out var result))
            {
                Log.Warning("[Remote] Parse message failed: \n{0}", payload);
                await webSocket.SendAsync(_webJsonConverter.Serialize(ResponseUtils.Err("Invalid action packet")));
                return;
            }

            Log.Debug("[Remote] Received message: \n{0}\n", payload);

            var (action, echo, parameters) = result;

            // TODO 并发度问题(限制与优化)&背压控制
            Task.Run(async () =>
            {
                var data = await _actionService.Execute(action, parameters, _context);

                if (echo != null) data["echo"] = echo;

                var text = _webJsonConverter.Serialize(data);
                Log.Debug("[Remote] Sending message: \n{0}", text);

                var ws = WebSocket;
                if (ws != null) await ws.SendAsync(text);
                else
                    Log.Warning("[Remote] Failed to respond action={0}, because websocket connection closed or lost.",
                        action);
            }).ConfigureFalseAwait();
        }

        if (e.DataFrame.IsClose) await webSocket.SafeCloseAsync();

        await e.InvokeNext();
    }

    private async void OnEventReceived(EventType type, IEventMeta? meta, object? data)
    {
        if (!_context.IsSubscribedEvent(type, meta)) return;


        var ws = WebSocket;
        if (ws == null)
        {
            Log.Warning("[Remote] Failed to send event={0}, because websocket connection closed or lost.",
                type);
            return;
        }

        if (meta != null)
        {
            await PrivateSendEvent(type, meta, data, ws); // 单一发送(只有带meta的事件类型才会触发)
        }
        else
        {
            var eventMetas = _context.GetEventMetas(type).ToArray();

            if (eventMetas.Length == 0) await PrivateSendEvent(type, meta, data, ws); // 单一事件(不带meta的事件类型)
            else // 批量发送(只有带meta的事件类型才会触发)
                // TODO 合并发送
                foreach (var eventMeta in eventMetas)
                    await PrivateSendEvent(type, eventMeta, data, ws);
        }
    }

    private async ValueTask PrivateSendEvent(EventType type, IEventMeta? meta, object? data, IWebSocket ws)
    {
        var text = _webJsonConverter.Serialize(new Dictionary<string, object?>
        {
            ["event"] = type,
            ["meta"] = meta,
            ["data"] = data,
            ["time"] = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()
        });
        await ws.SendAsync(text);
    }

    private static (string, string?, JObject?) ParseMessage(string message)
    {
        var data = JObject.Parse(message);
        return (
            data["action"]!.ToString(),
            data.TryGetValue("echo", out var echo) ? echo.ToString() : null,
            data["params"] as JObject
        );
    }

    private static bool TryParseMessage(string message, out (string, string?, JObject?) result)
    {
        try
        {
            result = ParseMessage(message);
            return true;
        }
        catch (Exception)
        {
            result = default;
            return false;
        }
    }
}