using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.UI.Remote
{
    internal class ClientConnection
    {
        private const int ProtocolVersion = 1;
        private const int BufferSize = 1024;

        private readonly ClientWebSocket _ws = new();

        private readonly ConcurrentQueue<Tuple<string, TaskCompletionSource<JObject>>>
            _pendingRequests = new();

        private CancellationTokenSource _cts;


        public static async Task<ClientConnection> OpenAsync(string address, int port, string token)
        {
            // create instance
            ClientConnection connection = new();
            connection._cts = new CancellationTokenSource();

            // set http header
            connection._ws.Options.SetRequestHeader("x-token", token);

            // connect ws
            var uri = new Uri($"ws://{address}:{port}/api/v{ProtocolVersion}");
            await connection._ws.ConnectAsync(uri, CancellationToken.None);

            // start receive loop
            _ = Task.Run(connection.ReceiveLoop);
            return connection;
        }

        private static async Task SendAsync(ClientWebSocket ws, Action action, Dictionary<string, object> args,
            string echo = null)
        {
            Dictionary<string, object> data = new()
            {
                { "action", action.ToString().ToLower() },
                { "params", args },
            };

            if (!string.IsNullOrEmpty(echo))
            {
                data.Add("echo", echo);
            }

            var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task SendAsync(Action action, Dictionary<string, object> args, string echo = null)
        {
            await SendAsync(_ws, action, args, echo);
        }

        public async Task<JObject> RequestAsync(Action action, Dictionary<string, object> args,
            string echo = null)
        {
            await SendAsync(_ws, action, args, echo);
            return await ExpectAsync(echo);
        }
        

        public async Task CloseAsync()
        {
            try
            {
                _cts.Cancel();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        private async Task<JObject> ExpectAsync(String echo = null)
        {
            var tcs = new TaskCompletionSource<JObject>();

            // enqueue
            _pendingRequests.Enqueue(
                new Tuple<String, TaskCompletionSource<JObject>>(echo, tcs));

            return await tcs.Task;
        }

        private async Task ReceiveLoop()
        {
            var ms = new MemoryStream();
            var buffer = new byte[BufferSize];
            
            while (!_cts.Token.IsCancellationRequested)
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }
                    catch (WebSocketException e)
                    {
                        Console.WriteLine(e.ToString());
                        ms.Dispose();
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ms.Dispose();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                if (_cts.Token.IsCancellationRequested)
                {
                    ms.Dispose();
                    return;
                }
                
                Dispatch(Encoding.UTF8.GetString(ms.ToArray()));

                ms.SetLength(0); // reset
            }
        }

        private void Dispatch(string json)
        {
            var data = JObject.Parse(json);
            data.TryGetValue("echo", out var echo);
            
            
            
            if (_pendingRequests.TryDequeue(out var pending))
            {
                if (echo?.ToString() == pending.Item1)
                {
                    pending.Item2.SetResult(data["data"] as JObject);
                }
                else throw new Exception("Received unexpect message: mismatched echo.");
            }
            else
            {
                throw new Exception("Received unexpect message: redundant message.");
            }
        }

        public static void Test()
        {
            Task.Run(async () =>
            {
                var connection = await OpenAsync("127.0.0.1", 11451, "123456");
                // sleep
                await Task.Delay(1000);
                await connection.SendAsync(
                    Action.Message,
                    new Dictionary<string, object>
                    {
                        { "message", "Hello World!" },
                        { "time", DateTime.Now }
                    }
                );

                var rv = await connection.RequestAsync(
                    Action.Ping,
                    new Dictionary<string, object>
                    {
                        { "ping_time", DateTime.Now },
                    },
                    "halo"
                );
                var data = rv["pong_time"];
                Console.WriteLine($"Received Pong: {data}");
                await connection.CloseAsync();
            });
        }
    }
}