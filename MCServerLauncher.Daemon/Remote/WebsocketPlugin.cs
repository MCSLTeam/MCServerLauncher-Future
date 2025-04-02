using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WebsocketPlugin : PluginBase, IWebSocketHandshakedPlugin, IWebSocketReceivedPlugin, IWebSocketClosedPlugin
{
    private readonly IActionService _actionService;

    private readonly IEventService _eventService;
    private readonly IHttpService _httpService;
    private readonly JsonSerializer _jsonSerializer;

    public WebsocketPlugin(IActionService actionService, IEventService eventService,
        JsonSerializer jsonSerializer, IHttpService httpService)
    {
        _actionService = actionService;
        _eventService = eventService;
        _jsonSerializer = jsonSerializer;
        _httpService = httpService;

        _eventService.Signal += OnEventSignalReceived;
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
            ActionRequest request;

            try
            {
                request = JsonConvert.DeserializeObject<ActionRequest>(actionString, DaemonJsonSettings.Settings)!;
                Log.Debug("[Remote] Received message:{0}", request);
            }
            catch (Exception exception) when (exception is JsonException or NullReferenceException)
            {
                var err = ResponseUtils.Err("Could not parse action json", ActionReturnCode.InternalError);
                await webSocket.SendAsync(JsonConvert.SerializeObject(err, DaemonJsonSettings.Settings));
                await e.InvokeNext();
                return;
            }

            // TODO 并发度问题(限制与优化)&背压控制
            var context = GetWsServiceContext(webSocket);
            var id = context.ClientId;
            var resolver = webSocket.Client.Resolver;
            Task.Run(async () =>
            {
                var result = await _actionService.ProcessAsync(request, resolver, CancellationToken.None);

                var text = JsonConvert.SerializeObject(result, DaemonJsonSettings.Settings);
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

    private void OnEventSignalReceived(EventType type, IEventMeta? meta, IEventData? data)
    {
        Task.Run(async () =>
        {
            foreach (var id in _httpService.GetIds()) await OnEventSignalReceived(id, type, meta, data);
        }).ConfigureFalseAwait();
    }

    private async Task OnEventSignalReceived(string clientId, EventType type, IEventMeta? meta, IEventData? data)
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

    private async ValueTask PrivateSendEvent(EventType type, IEventMeta? meta, IEventData? data, IWebSocket ws)
    {
        var packet = new EventPacket
        {
            EventType = type,
            EventMeta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings)),
            EventData = data is null ? null : JToken.FromObject(data, JsonSerializer.Create(JsonSettings.Settings))
        };
        await ws.SendAsync(JsonConvert.SerializeObject(packet, DaemonJsonSettings.Settings));
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
}