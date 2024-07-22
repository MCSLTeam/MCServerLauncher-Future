using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace MCServerLauncher.Daemon.Remote;

public class ServerBehavior : WebSocketBehavior
{
    internal static Config Config => Config.Get();

    private static string IpAddress { get; set; }
    private readonly ActionHandlers _handlers;
    private User User;

    
    public ServerBehavior()
    {
        _handlers = new ActionHandlers(Context);
    }

    protected override void OnOpen()
    {
        var token = Context.QueryString["token"];

        IpAddress = Context.UserEndPoint.ToString();

        var authenticated = false;
        try
        {
            authenticated = Users.Authenticate(token, out User);
        }
        catch (Exception e)
        {
            LogHelper.Error($"Failed to authenticate user: {e.Message}");
        }

        if (!authenticated)
        {
            Sessions.CloseSession(ID, CloseStatusCode.ProtocolError, "Authentication failed");
            LogHelper.Info($"Rejected connection from {IpAddress} for invalid token.");
            return;
        }

        LogHelper.Info($"{IpAddress} connected:\n With info: {User}");
    }

    private static bool ValidateToken(string token)
    {
        // TODO 
        return false;
    }

    protected override async void OnMessage(MessageEventArgs e)
    {
        if (e.IsText)
        {
            LogHelper.Info($"Received message: \n{e.Data}\n");

            var (actionType, echo, parameters) = ParseMessage(e.Data);

            var data = await _handlers.Routine(actionType, parameters);

            if (data == null) return; // empty data will not be sent

            if (echo != null)
            {
                data["echo"] = echo;
            }

            // TODO:生产环境应把缩进取消
            var text = JsonConvert.SerializeObject(data, Formatting.Indented);
            LogHelper.Info($"Sending message: \n{text}\n");
            Send(text);
        }
    }

    protected override void OnError(ErrorEventArgs e)
    {
        LogHelper.Error($"Exception encountered: \n{e.Exception}\n");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        LogHelper.Info($"Connection({IpAddress}) closed");
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

    // public static void Broadcast(string message)
    // {
    //     _server?.WebSocketServices.Broadcast(message);
    //     _server.Log.Info($"Broadcasting message: \n{message}\n");
    // }
    //
    // public static void BroadcastEvent(string eventType, JObject data)
    // {
    //     BroadcastEvent(DateTime.Now, eventType, data);
    // }
    //
    // public static void BroadcastEvent(DateTime dateTime, string eventType, JObject data)
    // {
    //     var messageJson = new JObject();
    //     messageJson["time"] = new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
    //     messageJson["event_type"] = eventType;
    //     messageJson["data"] = data;
    //
    //     // TODO 生产环境应把缩进取消
    //     var message = JsonConvert.SerializeObject(messageJson, Formatting.Indented);
    //     _server?.WebSocketServices.Broadcast(message);
    // }

    // private class HeartbeatSender : ITickingCallback
    // {
    //     private static DateTime _lastHeartbeat = DateTime.Now;
    //     private static readonly int HeartbeatInterval = 60;
    //
    //     public void Tick()
    //     {
    //         var now = DateTime.Now;
    //         if (now - _lastHeartbeat > TimeSpan.FromSeconds(HeartbeatInterval))
    //         {
    //             _lastHeartbeat = now;
    //             JObject data = new JObject();
    //             data["interval"] = HeartbeatInterval;
    //             BroadcastEvent(now, "meta_heartbeat", data);
    //         }
    //     }
    // }
}