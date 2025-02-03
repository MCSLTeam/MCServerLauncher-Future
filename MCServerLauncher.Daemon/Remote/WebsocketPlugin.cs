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

public class WebsocketPlugin : PluginBase, IWebSocketHandshakedPlugin, IWebSocketReceivedPlugin, IWebSocketClosedPlugin
{
    private readonly IActionService _actionService;
    private readonly IEventService _eventService;
    private readonly IWebJsonConverter _webJsonConverter;

    private Permissions permissions;

    private WeakReference? websocketRef;

    public WebsocketPlugin(IActionService actionService, IEventService eventService,
        IWebJsonConverter webJsonConverter)
    {
        _actionService = actionService;
        _eventService = eventService;
        _webJsonConverter = webJsonConverter;

        _eventService.Signal += async (type, data) =>
        {
            var ws = GetWebSocket();
            if (ws == null) return;

            var text = _webJsonConverter.Serialize(new Dictionary<string, object>
            {
                ["event"] = type,
                ["data"] = data,
                ["time"] = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()
            });
            await ws.SendAsync(text);
        };
    }

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        Log.Debug("[Remote] Websocket connection from {0} disconnected", webSocket.Client.GetIPPort());

        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        var name = new Permissions(JwtUtils.ExtractPermissions(e.Context.Request.Query["token"])!);

        // get peer ip
        Log.Debug("[Remote] Accept user: {0} from {1}", name, webSocket.Client.GetIPPort());

        // set websocket's weak reference
        websocketRef = new WeakReference(webSocket);

        await e.InvokeNext();
    }

    public async Task OnWebSocketReceived(IWebSocket webSocket, WSDataFrameEventArgs e)
    {
        if (e.DataFrame.IsText)
        {
            var payload = e.DataFrame.ToText();

            if (!TryParseMessage(payload, out var result))
            {
                Log.Warning("Parse message failed: \n{0}", payload);
                await webSocket.SendAsync(_webJsonConverter.Serialize(ResponseUtils.Err("Invalid action packet")));
                return;
            }

            Log.Debug("Received message: \n{0}\n", payload);

            var (action, echo, parameters) = result;

            Task.Run(async () =>
            {
                var data = await _actionService.Execute(action, parameters, permissions);

                if (echo != null) data["echo"] = echo;

                var text = _webJsonConverter.Serialize(data);
                Log.Debug("Sending message: \n{0}", text);

                var ws = GetWebSocket();
                if (ws != null) await ws.SendAsync(text);
            }).ConfigureFalseAwait();
        }

        if (e.DataFrame.IsClose) await webSocket.SafeCloseAsync();

        await e.InvokeNext();
    }

    private IWebSocket? GetWebSocket()
    {
        return websocketRef?.Target as IWebSocket;
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