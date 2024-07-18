using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace MCServerLauncher.Daemon.Remote;

public class ServerBehavior : WebSocketBehavior
{
    internal static Config Config { get; private set; }
    private static string IpAddress { get; set; }
    private readonly ActionHandlers _handlers;

    public ServerBehavior()
    {
        _handlers = new ActionHandlers(Context, Log);
    }

    protected override void OnOpen()
    {
        var token = Context.QueryString["token"];

        IpAddress = Context.UserEndPoint.ToString();
        if (!ValidateToken(token))
        {
            Sessions.CloseSession(ID, CloseStatusCode.ProtocolError, "Invalid token");
            Log.Info($"Rejected connection from {IpAddress} for invalid token.");
            return;
        }
        
        Log.Info($"{IpAddress} connected.");
    }

    private static bool ValidateToken(string token)
    {
        return Config.PersistentToken == token || Config.ValidateTemporaryToken(token);
    }

    protected override async void OnMessage(MessageEventArgs e)
    {
        Log.Info($"Received message: \n{e.Data}\n");

        var (actionType, echo, parameters) = ParseMessage(e.Data);

        var data = await _handlers.Routine(actionType, parameters);

        if (data == null) return; // empty data will not be sent

        if (echo != null)
        {
            data["echo"] = echo;
        }

        // TODO 生产环境应把缩进取消
        var text = JsonConvert.SerializeObject(data, Formatting.Indented);
        Log.Info($"Sending message: \n{text}\n");
        Send(text);
    }

    protected override void OnError(ErrorEventArgs e)
    {
        Log.Error($"Exception encountered: \n{e.Exception}\n");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Log.Info($"Connection({IpAddress}) closed");
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


    public static void Test()
    {
        Config.TryLoadOrDefault("config.json", out var config);
        Config = config;

        var server = new WebSocketServer(config.Port);
        server.Log.Level = LogLevel.Info;

        server.AddWebSocketService<ServerBehavior>($"/api/v1");
        server.Start();

        Console.WriteLine($"Server started at ws://{server.Address}:{config.Port}/api/v1");
        Console.ReadKey();
        server.Stop();
    }
}