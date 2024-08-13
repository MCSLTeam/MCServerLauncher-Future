using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace MCServerLauncher.Daemon.Remote;

/// <summary>
///     ws服务类,继承自websocketsharp的WebSocketBehavior。
///     ws处理的消息格式为json，分为两个大类Action,Event。
///     Action(远程过程调用): C->S,处理后返回json数据给C。Event(服务端事件): S->C,由服务端主动发送，C不会响应数据。
/// </summary>
internal class ServerBehavior : WebSocketBehavior
{
    // DI
    public ServerBehavior(
        IActionService actionService,
        IEventService eventService,
        IUserService userService,
        IWebJsonConverter WebJsonConverter,
        RemoteLogHelper logHelper
    )
    {
        _actionService = actionService;
        _eventService = eventService;
        _userService = userService;
        _logHelper = logHelper;
        _webJsonConverter = WebJsonConverter;

        _eventService.Signal += (type, data) => Context.WebSocket.Send(_webJsonConverter.Serialize(
            new Dictionary<string, object>
            {
                ["event_type"] = type,
                ["data"] = data,
                ["time"] = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()
            }
        ));
    }

    internal static AppConfig AppConfig => AppConfig.Get();

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
                Send(_webJsonConverter.Serialize(_actionService.Err("Invalid action packet")));
                return;
            }

            _logHelper.Debug($"Received message: \n{e.Data}\n");

            var (actionType, echo, parameters) = result;

            var data = await _actionService.Routine(actionType, parameters);

            if (data == null) return; // empty data will not be sent

            if (echo != null) data["echo"] = echo;

            var text = _webJsonConverter.Serialize(data);

            _logHelper.Debug($"Sending message: \n{text}");
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

    #region DenpendencyInject

    /// <summary>
    ///     Action处理服务
    /// </summary>
    private readonly IActionService _actionService;

    /// <summary>
    ///     Event处理服务
    /// </summary>
    private readonly IEventService _eventService;

    /// <summary>
    ///     专属日志，(至于为什么不用WebSocketBehavior自带的Log,自己去看#8)
    /// </summary>
    private readonly ILogHelper _logHelper;

    /// <summary>
    ///     用户服务，用于验证token，获取用户权限
    /// </summary>
    private readonly IUserService _userService;

    /// <summary>
    ///     ws消息格式Json的序列化器
    /// </summary>
    private readonly IWebJsonConverter _webJsonConverter;

    /// <summary>
    ///     保存的该连接的用户信息
    /// </summary>
    private User User;

    #endregion
}