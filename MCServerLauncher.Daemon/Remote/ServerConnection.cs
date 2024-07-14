using Fleck;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote
{
    internal class ServerConnection
    {
        private const int ProtocolVersion = 1;
        private IDictionary<Guid, IWebSocketConnection> _clients = new Dictionary<Guid, IWebSocketConnection>();
        private WebSocketServer _server;

        public ServerConnection(string ip, int port)
        {
            _server = new WebSocketServer($"ws://{ip}:{port}/api/v{ProtocolVersion}");
        }

        public void Open(string token)
        {
            _server.Start(socket =>
            {
                socket.OnOpen += () =>
                {
                    // get request header
                    var header = socket.ConnectionInfo.Headers;
                    header.TryGetValue("x-token", out var value);

                    if (!value?.Equals(token) ?? true)
                    {
                        socket.Close(400); // 鉴权失败
                    }

                    // add client
                    _clients.Add(socket.ConnectionInfo.Id, socket);
                    Console.WriteLine(
                        $"New connection({socket.ConnectionInfo.Id}) from {socket.ConnectionInfo.ClientIpAddress}:{socket.ConnectionInfo.ClientPort}\n");
                };

                socket.OnMessage += message =>
                {
                    Console.WriteLine($"Received message: \n{message}\n");

                    var parsed = ParseMessage(message);

                    Dictionary<string, object> data;
                    
                    switch (parsed.Item1)
                    {
                        case Action.Message:
                        {
                            data = null;
                            break;
                        }

                        case Action.Ping:
                        {
                            data = new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "retcode", 0 },
                                {
                                    "data", new Dictionary<string, object>
                                    {
                                        { "pong_time", DateTime.Now }
                                    }
                                }
                            };
                            break;
                        }
                        default: throw new ArgumentOutOfRangeException(parsed.Item1.ToString());
                    }

                    if (data == null) return;

                    if (parsed.Item2 != null)
                    {
                        data!["echo"] = parsed.Item2;
                    }

                    var text = JsonConvert.SerializeObject(data, Formatting.Indented);
                    Console.WriteLine($"Sending message: \n{text}\n");
                    socket.Send(text);
                };

                socket.OnError += exception => { Console.Error.WriteLine($"Exception encountered: \n{exception}\n"); };

                socket.OnClose += () =>
                {
                    _clients.Remove(socket.ConnectionInfo.Id);
                    Console.WriteLine($"Connection({socket.ConnectionInfo.Id}) closed");
                };
            });
        }

        public static Tuple<Action, string, JObject> ParseMessage(string message)
        {
            var data = JObject.Parse(message);
            return new Tuple<Action, string, JObject>(
                (Action)Enum.Parse(typeof(Action), data["action"]!.ToString(), true),
                data.ContainsKey("echo") ? data["echo"]!.ToString() : null,
                data["params"]! as JObject
            );
        }
        

        public static void Test()
        {
            Console.WriteLine("ws started.");
            var server = new ServerConnection("127.0.0.1", 11451);
            server.Open("123456");
            Console.ReadKey();
        }
    }
}