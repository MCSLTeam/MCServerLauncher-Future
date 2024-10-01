using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
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
    private readonly IUserService _userService;
    private readonly IWebJsonConverter _webJsonConverter;


    private User? _user;

    public WebsocketPlugin(IUserService userService, IActionService actionService, IWebJsonConverter webJsonConverter)
    {
        _userService = userService;
        _actionService = actionService;
        _webJsonConverter = webJsonConverter;
    }

    public async Task OnWebSocketClosed(IWebSocket webSocket, ClosedEventArgs e)
    {
        Log.Debug("[Remote] Websocket connection from {0} disconnected", webSocket.Client.GetIPPort());

        await e.InvokeNext();
    }

    public async Task OnWebSocketHandshaked(IWebSocket webSocket, HttpContextEventArgs e)
    {
        var name = e.Context.Request.Headers.Get("user");

        // get peer ip
        Log.Debug("[Remote] Accept user: {0} from {1}", name, webSocket.Client.GetIPPort());

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
                await webSocket.SendAsync(_webJsonConverter.Serialize(_actionService.Err("Invalid action packet")));
                return;
            }

            Log.Debug("Received message: \n{0}\n", payload);

            var (actionType, echo, parameters) = result;
            var data = await _actionService.Routine(actionType, parameters);

            if (echo != null) data["echo"] = echo;

            var text = _webJsonConverter.Serialize(data);

            Log.Debug("Sending message: \n{0}", text);
            await webSocket.SendAsync(text);
        }

        if (e.DataFrame.IsClose) await webSocket.SafeCloseAsync();

        await e.InvokeNext();
    }

    private static (ActionType, string?, JObject?) ParseMessage(string message)
    {
        var data = JObject.Parse(message);
        return (
            ActionTypeExtensions.FromSnakeCase(data["action"]!.ToString()),
            data.TryGetValue("echo", out var echo) ? echo.ToString() : null,
            data["params"] as JObject
        );
    }

    private static bool TryParseMessage(string message, out (ActionType, string?, JObject?) result)
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