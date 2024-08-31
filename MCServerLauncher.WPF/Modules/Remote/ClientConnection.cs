using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.WPF.Modules.Remote
{
    internal class HeartBeatTimerState
    {
        public ClientConnection Connection { get; set; }
        public int PingPacketLost { get; set; }
        public SynchronizationContext ConnectionContext { get; set; }
    }

    internal class ClientConnection
    {
        private const int ProtocolVersion = 1;
        private const int BufferSize = 1024;
        private CancellationTokenSource _cts;
        private Timer _heartbeatTimer;

        private Channel<(string, TaskCompletionSource<JObject>)> _pendingRequests;
        private bool PingLost { get; set; }

        public bool Closed => WebSocket.State == WebSocketState.Closed;
        public DateTime LastPong { get; private set; } = DateTime.Now;
        public ClientWebSocket WebSocket { get; } = new();
        public ClientConnectionConfig Config { get; private set; }

        /// <summary>
        ///     建立连接
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="token"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static async Task<ClientConnection> OpenAsync(string address, int port, string token,
            ClientConnectionConfig config)
        {
            // create instance
            ClientConnection connection = new();
            connection._cts = new CancellationTokenSource();
            connection.Config = config;
            connection._pendingRequests =
                Channel.CreateBounded<(string, TaskCompletionSource<JObject>)>(config.PendingRequestCapacity);

            var timerState = new HeartBeatTimerState
            {
                Connection = connection,
                ConnectionContext = SynchronizationContext.Current
            };
            connection._heartbeatTimer =
                new Timer(OnHeartBeatTimer, timerState, config.PingInterval, config.PingInterval);


            // connect ws
            var uri = new Uri($"ws://{address}:{port}/api/v{ProtocolVersion}?token={token}");
            await connection.WebSocket.ConnectAsync(uri, CancellationToken.None);

            // start receive loop
            _ = Task.Run(connection.ReceiveLoop);
            return connection;
        }

        /// <summary>
        ///     心跳定时器超时逻辑: 根据连接情况设定ClientConnection的PingLost，根据config判断是否关闭连接
        /// </summary>
        /// <param name="timerState"></param>
        private static void OnHeartBeatTimer(object timerState)
        {
            var state = (HeartBeatTimerState)timerState;
            var conn = state.Connection;
            var pingTask = conn.RequestAsync(
                ActionType.Ping,
                new Dictionary<string, object>(),
                timeout: conn.Config.PingTimeout
            );

            pingTask.RunSynchronously();

            if (pingTask.Exception?.InnerException is TimeoutException)
            {
                state.PingPacketLost++;
                // 切换到ClientConnection所在线程,防止数据竞争
                state.ConnectionContext.Post(_ => { conn.PingLost = true; }, null);
            }
            else
            {
                state.PingPacketLost = 0;
                var timestamp = pingTask.Result["time"]!.ToObject<long>();

                // 切换到ClientConnection所在线程,防止数据竞争
                state.ConnectionContext.Post(
                    _ => { conn.LastPong = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime; }, null);
            }

            if (state.PingPacketLost >= conn.Config.MaxPingPacketLost)
            {
                Log.Error("Ping packet lost too many times, close connection.");
                // 关闭连接
                conn.CloseAsync().RunSynchronously();
            }
        }

        /// <summary>
        ///     包装action并发送,内部函数
        /// </summary>
        /// <param name="ws"></param>
        /// <param name="actionType"></param>
        /// <param name="args"></param>
        /// <param name="echo"></param>
        private static async Task SendAsync(ClientWebSocket ws, ActionType actionType, Dictionary<string, object> args,
            string echo = null)
        {
            Dictionary<string, object> data = new()
            {
                { "action", actionType.ToShakeCase() },
                { "params", args }
            };

            if (!string.IsNullOrEmpty(echo)) data.Add("echo", echo);

            var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        ///     发送一个action
        /// </summary>
        /// <param name="actionType"></param>
        /// <param name="args"></param>
        /// <param name="echo"></param>
        public async Task SendAsync(ActionType actionType, Dictionary<string, object> args, string echo = null)
        {
            await SendAsync(WebSocket, actionType, args, echo);
        }

        /// <summary>
        ///     一个RPC过程,包含了发送和等待回复
        /// </summary>
        /// <param name="actionType">action类型</param>
        /// <param name="args">该action参数</param>
        /// <param name="echo">echo</param>
        /// <param name="timeout">微秒</param>
        /// <returns></returns>
        public async Task<JObject> RequestAsync(ActionType actionType, Dictionary<string, object> args,
            string echo = null, int timeout = 5000)
        {
            await SendAsync(actionType, args, echo);
            return await ExpectAsync(timeout, echo);
        }

        /// <summary>
        ///     关闭连接
        /// </summary>
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

            await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        /// <summary>
        ///     期待一个回复
        /// </summary>
        /// <param name="timeout">回复超时</param>
        /// <param name="echo">echo校验</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="TimeoutException">期待超时</exception>
        private async Task<JObject> ExpectAsync(int timeout, string echo = null)
        {
            var tcs = new TaskCompletionSource<JObject>();

            // enqueue
            if (!_pendingRequests.Writer.TryWrite((echo, tcs)))
            {
                Log.Error("[ClientConnection] failed to enqueue request, queue is full");
                throw new Exception("failed to enqueue request, queue is full");
            }

            var task = await Task.WhenAny(tcs.Task, Task.Delay(timeout));

            if (task != tcs.Task) throw new TimeoutException();

            var received = tcs.Task.Result;

            // TODO 错误处理 (!)
            var status = received["status"]!.ToString();
            if (status == null) throw new Exception($"status is null: {received}");

            return status switch
            {
                "ok" => received["data"]! as JObject,
                "error" => throw new Exception(received["data"]!["message"]?.ToString() ?? "unknown error"),
                _ => throw new Exception($"unknown status: {status}")
            };
        }

        // private async Task<JObject> UploadFileChunk(string fileId, int offset, string strData)
        // {
        //     var data = new Dictionary<string, object>
        //     {
        //         { "file_id", fileId },
        //         { "offset", offset },
        //         { "data", strData }
        //     };
        //     await SendAsync(WebSocket, ActionType.FileUploadChunk, data);
        //     data = null; // 释放
        //     return await ExpectAsync(5000);
        // }
        //
        // public async Task<string> UploadFile(string path, string dst, int chunkSize)
        // {
        //     using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        //     var sha1 = await Utils.FileSha1(fs);
        //     var size = new FileInfo(path).Length;
        //     var fileId = (await RequestAsync(ActionType.FileUploadRequest, new Dictionary<string, object>
        //     {
        //         { "path", dst },
        //         { "sha1", sha1 },
        //         { "chunk_size", chunkSize },
        //         { "size", size }
        //     }))["file_id"]!.ToString();
        //
        //     var tasks = new List<Task<JObject>>();
        //
        //     var buffer = new byte[chunkSize];
        //     var offset = 0;
        //     int bytesRead;
        //     while ((bytesRead = fs.Read(buffer, 0, chunkSize)) > 0)
        //     {
        //         string strData;
        //         if (bytesRead == chunkSize)
        //         {
        //             strData = Encoding.BigEndianUnicode.GetString(buffer, 0, chunkSize);
        //         }
        //         else if (bytesRead % 2 != 0) // 末尾补0x00
        //         {
        //             buffer[bytesRead] = 0x00;
        //             strData = Encoding.BigEndianUnicode.GetString(buffer, 0, bytesRead + 1);
        //         }
        //         else
        //         {
        //             strData = Encoding.BigEndianUnicode.GetString(buffer, 0, bytesRead);
        //         }
        //
        //         try
        //         {
        //             Log.Information((await UploadFileChunk(fileId, offset, strData)).ToString());
        //         }
        //         catch (Exception e)
        //         {
        //             Log.Information(e.ToString());
        //         }
        //
        //         offset += bytesRead;
        //     }
        //
        //     return null;
        // }

        /// <summary>
        ///     消息接收循环, 被设计运行在某一个线程中
        /// </summary>
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
                        result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
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
                    if (result.EndOfMessage) break;
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

        /// <summary>
        ///     派发从服务器接收的应答并根据Action的规则解析包, 判断echo的一致性, 并为等待的任务设置相应的结果或异常
        /// </summary>
        /// <param name="json">从服务器接收的json</param>
        private void Dispatch(string json)
        {
            var data = JObject.Parse(json);
            data.TryGetValue("echo", out var echo);


            if (_pendingRequests.Reader.TryRead(out var pending))
            {
                if (echo?.ToString() == pending.Item1)
                {
                    pending.Item2.SetResult(data);
                }
                else
                {
                    var msg =
                        $"[ClientConnection] Received unexpect message: mismatched echo, expected: {pending.Item1}, received: {echo}";
                    Log.Error(msg);
                    pending.Item2.SetException(new Exception(msg));
                }
            }
            else
            {
                Log.Warning($"[ClientConnection] Received redundant message: redundant message: {json}");
            }
        }

        // public static void Test()
        // {
        //     Task.Run(async () =>
        //     {
        //         var connection = await OpenAsync("127.0.0.1", 11451, "8e648c37-677f-43a5-8cd1-792668fc7e29",
        //             new ClientConnectionConfig());
        //         // sleep
        //         await Task.Delay(1000);
        //
        //         #region Ping
        //
        //         var rv = await connection.RequestAsync(
        //             ActionType.Ping,
        //             new Dictionary<string, object>(),
        //             "halo"
        //         );
        //         var data = rv["time"];
        //         Console.WriteLine($@"Received Pong: {data}");
        //
        //         #endregion
        //
        //
        //         #region UploadFile
        //
        //         var err = await connection.UploadFile(
        //             "Newtonsoft.Json.xml", "Newtonsoft.Json.xml",
        //             1024);
        //         if (err == null)
        //             Console.WriteLine(@"Upload Success");
        //         else
        //             Console.WriteLine($@"Upload Failed:{err}");
        //
        //         #endregion
        //
        //
        //         await Task.Delay(5000);
        //         await connection.CloseAsync();
        //     });
        // }
    }
}