using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace MCServerLauncher.UI.Remote
{
    internal class ClientConnection
    {

        public const int ProtocolVersion = 1;
        public const int BufferSize = 1024;

        protected readonly ClientWebSocket _ws = new();

        private readonly ConcurrentQueue<Tuple<Action, String, TaskCompletionSource<Dictionary<String, Object>>>> _pendingRequests = new();
        private CancellationTokenSource _cts;


        public static async Task OpenAsync(String address, int port, String token)
        {
            // create instance
            ClientConnection connection = new();
            connection._cts = new CancellationTokenSource();

            var uri = new Uri($"ws://{address}:{port}/api/v{ProtocolVersion}/{token}");
            await connection._ws.ConnectAsync(uri, CancellationToken.None);

            // start receive loop
            _ = Task.Run(() => connection.ReceiveLoop(connection._ws, token));
        }

        private async static Task SendAsync(ClientWebSocket ws, Action action, Dictionary<String, Object> args, String echo = null)
        {
            Dictionary<String, Object> data = new()
            {
                {"action", action.ToString().ToLower()},
                {"params",args},
            };

            if (!string.IsNullOrEmpty(echo))
            {
                data.Add("echo", echo);
            }

            var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task<Dictionary<String, Object>> RequestAsync(Action action, Dictionary<String, Object> args, String echo = null)
        {
            await SendAsync(_ws, action, args, echo);
            return await ExpectAsync(action, echo);
        }

        public async Task CloseAsync()
        {
            _cts.Cancel();
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        private async Task<Dictionary<String, Object>> ExpectAsync(Action action, String echo = null)
        {
            var tcs = new TaskCompletionSource<Dictionary<String, Object>>();

            // enqueue
            _pendingRequests.Enqueue(new Tuple<Action, String, TaskCompletionSource<Dictionary<String, Object>>>(action, echo, tcs));

            return await tcs.Task;
        }

        private async Task ReceiveLoop(ClientWebSocket ws, String echo = null)
        {
            String json;
            WebSocketReceiveResult result;

            var ms = new MemoryStream();
            var buffer = new byte[BufferSize];


            while (!_cts.IsCancellationRequested)
            {
                while (true)
                {

                    try
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
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

                json = Encoding.UTF8.GetString(ms.ToArray());

                Dispatch(json);

                ms.SetLength(0); // reset
            }
        }

        private void Dispatch(String json)
        {
            var data = JsonConvert.DeserializeObject<Dictionary<String, Object>>(json);
            var action = (Action)Enum.Parse(typeof(Action), data["action"].ToString(), true);
            var echo = data.ContainsKey("echo") ? data["echo"].ToString() : null;

            if (_pendingRequests.TryDequeue(out var pending))
            {
                if (action == pending.Item1 && echo == pending.Item2)
                {
                    pending.Item3.SetResult(data);
                }
                else throw new Exception("Received unexpect message: mismatched message.");
            }
            else
            {
                throw new Exception("Received unexpect message: redundant message.");
            }
        }
    }
}
