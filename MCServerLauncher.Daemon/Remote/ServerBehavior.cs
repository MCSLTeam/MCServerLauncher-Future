using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace MCServerLauncher.Daemon.Remote;

public class ServerBehavior : WebSocketBehavior
{
    internal static string Token { get; set; }
    private static string IpAddress { get; set; }

    protected override void OnOpen()
    {
        var token = Context.QueryString["token"];

        if (token != Token)
        {
            Context.WebSocket.Close(400, "Invalid token");
        }
        
        IpAddress = Context.UserEndPoint.ToString();
        Console.WriteLine($"{IpAddress} connected.");
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        Console.WriteLine($"Received message: \n{e.Data}\n");

        var parsed = ParseMessage(e.Data);

        var data = parsed.Item1 switch
        {
            ActionType.Message => null,
            ActionType.Ping => new Dictionary<string, object>
            {
                { "status", "ok" },
                { "retcode", 0 },
                {
                    "data", new Dictionary<string, object>
                    {
                        { "pong_time", DateTime.Now }
                    }
                }
            },
            _ => throw new NotImplementedException()
        };

        if (data == null) return;

        if (parsed.Item2 != null)
        {
            data["echo"] = parsed.Item2;
        }

        // 生产环境应把缩进取消
        var text = JsonConvert.SerializeObject(data, Formatting.Indented);
        Console.WriteLine($"Sending message: \n{text}\n");
        Send(text);
    }

    protected override void OnError(ErrorEventArgs e)
    {
        Console.Error.WriteLine($"Exception encountered: \n{e.Exception}\n");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        Console.WriteLine($"Connection({IpAddress}) closed");
    }

    private static Tuple<ActionType, string, JObject> ParseMessage(string message)
    {
        var data = JObject.Parse(message);
        return new Tuple<ActionType, string, JObject>(
            ActionTypeExtensions.FromSnakeCase(data["action"]!.ToString()),
            data.ContainsKey("echo") ? data["echo"]!.ToString() : null,
            data["params"]! as JObject
        );
    }

    public static void Test()
    {
        const int port = 11451;
        const int protocol = 1;
        var server = new WebSocketServer(port);
        Token = "123456";
        server.AddWebSocketService<ServerBehavior>($"/api/v{protocol}");
        server.Start();
        Console.WriteLine($"Server started at ws://{server.Address}:{port}/api/v{protocol}");
        Console.ReadKey();
        server.Stop();
    }
}