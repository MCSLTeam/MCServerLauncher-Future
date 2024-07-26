using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace MCServerLauncher.Daemon.Remote;

internal class ServerBehavior : WebSocketBehavior
{
    // Injected dependencies
    private readonly IActionService _actionService;
    private readonly IEventService _eventService;
    private readonly IJsonService _jsonService;
    private readonly ILogHelper _logHelper;
    private readonly IUserService _userService;
    private User User;

    // DI
    public ServerBehavior(
        IActionService actionService,
        IEventService eventService,
        IUserService userService,
        IJsonService jsonService,
        RemoteLogHelper logHelper
    )
    {
        _actionService = actionService;
        _eventService = eventService;
        _userService = userService;
        _logHelper = logHelper;
        _jsonService = jsonService;

        _eventService.Signal += (type, data) => Context.WebSocket.Send(_jsonService.Serialize(
            new Dictionary<string, object>
            {
                ["event_type"] = type,
                ["data"] = data,
                ["time"] = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()
            }
        ));
    }

    internal static Config Config => Config.Get();

    private static string IpAddress { get; set; }

    protected override void OnOpen()
    {
        var token = Context.QueryString["token"];

        IpAddress = Context.UserEndPoint.ToString();

        var authenticated = false;
        try
        {
            authenticated = _userService.Authenticate(token, out User);
        }
        catch (Exception e)
        {
            _logHelper.Error($"Failed to authenticate user: {e.Message}");
        }

#if DEBUG
        authenticated = true;
#endif

        if (!authenticated)
        {
            Sessions.CloseSession(ID, CloseStatusCode.ProtocolError, "Authentication failed");
            _logHelper.Info($"Rejected connection from {IpAddress} for invalid token.");
            return;
        }

        _logHelper.Info($"{IpAddress} connected:\n With info: {User}");
    }

    protected override async void OnMessage(MessageEventArgs e)
    {
        if (e.IsText)
        {
            // var (actionType, echo, parameters) = ParseMessage(e.Data);
            if (!TryParseMessage(e.Data, out var result))
            {
                _logHelper.Warn($"Parse message failed: \n{e.Data}");
                Send(_jsonService.Serialize(_actionService.Err("Invalid action packet")));
                return;
            }

            _logHelper.Info($"Received message: \n{e.Data}\n");

            var (actionType, echo, parameters) = result;

            var data = await _actionService.Routine(actionType, parameters);

            if (data == null) return; // empty data will not be sent

            if (echo != null) data["echo"] = echo;

            var text = _jsonService.Serialize(data);

            _logHelper.Info($"Sending message: \n{text}");
            Send(text);
        }
    }

    protected override void OnError(ErrorEventArgs e)
    {
        _logHelper.Error($"Exception encountered: \n{e.Exception}");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        _logHelper.Info($"Connection({IpAddress}) closed");
    }

    private static (ActionType, string, JObject) ParseMessage(string message)
    {
        var data = JObject.Parse(message);
        return (
            ActionTypeExtensions.FromSnakeCase(data["action"]!.ToString()),
            data.TryGetValue("echo", out var echo) ? echo.ToString() : null,
            data["params"]! as JObject
        );
    }

    private static bool TryParseMessage(string message, out (ActionType, string, JObject) result)
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