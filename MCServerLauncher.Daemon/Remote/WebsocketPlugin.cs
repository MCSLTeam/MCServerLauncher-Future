using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Event.Meta;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
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
    public string ClientId { get; set; } = null!;

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

    private readonly IEventService _eventService;
    private readonly IHttpService _httpService;
    private readonly IWebJsonConverter _webJsonConverter;

    public WebsocketPlugin(IActionService actionService, IEventService eventService,
        IWebJsonConverter webJsonConverter, IHttpService httpService)
    {
        _actionService = actionService;
        _eventService = eventService;
        _webJsonConverter = webJsonConverter;
        _httpService = httpService;

        _eventService.Signal += OnEventReceived;
    }

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        GetWsServiceContext(webSocket).UnsubscribeAllEvents();
        Log.Debug("[Remote] Websocket connection from {0} disconnected", webSocket.Client.GetIPPort());

        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        var token = e.Context.Request.Query["token"]!;
        var context = InitWsServiceContext(webSocket);

        // TODO normalization
        context.Permissions =
            new Permissions(token == AppConfig.Get().MainToken ? "*" : JwtUtils.ExtractPermissions(token)!);

        // get peer ip
        Log.Debug("[Remote] Accept token: \"{0}...\" from {1} with Id={2}", token[..5], webSocket.Client.GetIPPort(),
            context.ClientId);

        await e.InvokeNext();
    }

    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var actionString = e.DataFrame.ToText();
            JObject data;

            try
            {
                data = JObject.Parse(actionString);
            }
            catch (JsonException)
            {
                var err = ResponseUtils.Err("Could not parse json string", 1500, null);
                await webSocket.SendAsync(_webJsonConverter.Serialize(err));
                await e.InvokeNext();
                return;
            }

            // TODO 并发度问题(限制与优化)&背压控制
            var context = GetWsServiceContext(webSocket);
            var id = context.ClientId;
            var resolver = webSocket.Client.Resolver;
            Task.Run(async () =>
            {
                var result = await _actionService.ProcessAsync(data, resolver, CancellationToken.None);

                var text = _webJsonConverter.Serialize(result);
                Log.Debug("[Remote] Sending message: \n{0}", text);

                var ws = GetWebSocket(id);
                if (ws != null) await ws.SendAsync(text);
                else
                    Log.Warning("[Remote] Failed to respond action, because websocket connection closed or lost.");
            }).ConfigureFalseAwait();
        }

        if (e.DataFrame.IsClose) await webSocket.SafeCloseAsync();

        await e.InvokeNext();
    }

    private IWebSocket? GetWebSocket(string id)
    {
        return _httpService.TryGetClient(id, out var client) ? client.WebSocket : null;
    }

    private static string GetClientId(IWebSocket webSocket)
    {
        return webSocket.Client is TcpSessionClientBase tcpClientBase ? tcpClientBase.Id : "";
    }

    private static WsServiceContext GetWsServiceContext(IWebSocket webSocket)
    {
        return webSocket.Client.Resolver.GetRequiredService<WsServiceContext>();
    }

    private static WsServiceContext InitWsServiceContext(IWebSocket webSocket)
    {
        var ctx = GetWsServiceContext(webSocket);
        ctx.ClientId = GetClientId(webSocket);
        return ctx;
    }

    private void OnEventReceived(EventType type, IEventMeta? meta, object? data)
    {
        Task.Run(async () =>
        {
            foreach (var id in _httpService.GetIds()) await OnEventReceived(id, type, meta, data);
        }).ConfigureFalseAwait();
    }

    private async Task OnEventReceived(string clientId, EventType type, IEventMeta? meta, object? data)
    {
        var ws = GetWebSocket(clientId);
        if (ws == null)
        {
            Log.Warning("[Remote] Failed to send event={0}, because websocket connection closed or lost.",
                type);
            return;
        }

        var context = GetWsServiceContext(ws);
        if (!context.IsSubscribedEvent(type, meta)) return;

        if (meta != null)
        {
            await PrivateSendEvent(type, meta, data, ws); // 单一发送(只有带meta的事件类型才会触发)
        }
        else
        {
            var eventMetas = context.GetEventMetas(type).ToArray();

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