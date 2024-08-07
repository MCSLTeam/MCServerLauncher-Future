﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.WPF.Remote
{
    internal class ClientConnection
    {
        private const int ProtocolVersion = 1;
        private const int BufferSize = 1024;

        private readonly ConcurrentQueue<Tuple<string, TaskCompletionSource<JObject>>>
            _pendingRequests = new();

        private CancellationTokenSource _cts;
        public ClientWebSocket WebSocket { get; } = new();


        public static async Task<ClientConnection> OpenAsync(string address, int port, string token)
        {
            // create instance
            ClientConnection connection = new();
            connection._cts = new CancellationTokenSource();

            // connect ws
            var uri = new Uri($"ws://{address}:{port}/api/v{ProtocolVersion}?token={token}");
            await connection.WebSocket.ConnectAsync(uri, CancellationToken.None);

            // start receive loop
            _ = Task.Run(connection.ReceiveLoop);
            return connection;
        }

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

        public async Task SendAsync(ActionType actionType, Dictionary<string, object> args, string echo = null)
        {
            await SendAsync(WebSocket, actionType, args, echo);
        }

        public async Task<JObject> RequestAsync(ActionType actionType, Dictionary<string, object> args,
            string echo = null)
        {
            await SendAsync(WebSocket, actionType, args, echo);
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

            await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        private async Task<JObject> ExpectAsync(string echo = null)
        {
            var tcs = new TaskCompletionSource<JObject>();

            // enqueue
            _pendingRequests.Enqueue(
                new Tuple<string, TaskCompletionSource<JObject>>(echo, tcs));
            var received = await tcs.Task;

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

        private async Task<JObject> UploadFileChunk(string fileId, int offset, string strData)
        {
            var data = new Dictionary<string, object>
            {
                { "file_id", fileId },
                { "offset", offset },
                { "data", strData }
            };
            await SendAsync(WebSocket, ActionType.FileUploadChunk, data);
            data = null; // 释放
            return await ExpectAsync();
        }

        public async Task<string> UploadFile(string path, string dst, int chunkSize)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha1 = await Utils.FileSha1(fs);
            var size = new FileInfo(path).Length;
            var fileId = (await RequestAsync(ActionType.FileUploadRequest, new Dictionary<string, object>
            {
                { "path", dst },
                { "sha1", sha1 },
                { "chunk_size", chunkSize },
                { "size", size }
            }))["file_id"]!.ToString();

            var tasks = new List<Task<JObject>>();

            var buffer = new byte[chunkSize];
            var offset = 0;
            int bytesRead;
            while ((bytesRead = fs.Read(buffer, 0, chunkSize)) > 0)
            {
                string strData;
                if (bytesRead == chunkSize)
                {
                    strData = Encoding.BigEndianUnicode.GetString(buffer, 0, chunkSize);
                }
                else if (bytesRead % 2 != 0) // 末尾补0x00
                {
                    buffer[bytesRead] = 0x00;
                    strData = Encoding.BigEndianUnicode.GetString(buffer, 0, bytesRead + 1);
                }
                else
                {
                    strData = Encoding.BigEndianUnicode.GetString(buffer, 0, bytesRead);
                }

                try
                {
                    Log.Information((await UploadFileChunk(fileId, offset, strData)).ToString());
                }
                catch (Exception e)
                {
                    Log.Information(e.ToString());
                }

                offset += bytesRead;
            }

            return null;
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

        private void Dispatch(string json)
        {
            var data = JObject.Parse(json);
            data.TryGetValue("echo", out var echo);


            if (_pendingRequests.TryDequeue(out var pending))
            {
                if (echo?.ToString() == pending.Item1)
                    pending.Item2.SetResult(data);
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
                var connection = await OpenAsync("127.0.0.1", 11451, "8e648c37-677f-43a5-8cd1-792668fc7e29");
                // sleep
                await Task.Delay(1000);

                #region Message

                await connection.SendAsync(
                    ActionType.Message,
                    new Dictionary<string, object>
                    {
                        { "message", "Hello World!" },
                        { "time", DateTime.Now }
                    }
                );

                #endregion


                #region Ping

                var rv = await connection.RequestAsync(
                    ActionType.Ping,
                    new Dictionary<string, object>
                    {
                        { "ping_time", DateTime.Now }
                    },
                    "halo"
                );
                var data = rv["pong_time"];
                Console.WriteLine($@"Received Pong: {data}");

                #endregion


                #region UploadFile

                var err = await connection.UploadFile(
                    "Newtonsoft.Json.xml", "Newtonsoft.Json.xml",
                    1024);
                if (err == null)
                    Console.WriteLine(@"Upload Success");
                else
                    Console.WriteLine($@"Upload Failed:{err}");

                #endregion


                #region NewToken

                rv = await connection.RequestAsync(
                    ActionType.NewToken,
                    new Dictionary<string, object>
                    {
                        { "seconds", 5 },
                        { "type", "temporary" },
                        { "permissions", new bool[] { } }
                    }
                );
                var newToken = rv["token"]!.ToString();
                var expired = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1))
                    .AddSeconds(Convert.ToInt64(rv["expired"]!));
                Console.WriteLine($@"New Token: {newToken}, Expired: {expired}");

                var tempConn1 = await OpenAsync("127.0.0.1", 11451, newToken);
                Console.WriteLine(@"Temp Token conn state: " + tempConn1.WebSocket.State);
                await tempConn1.SendAsync(
                    ActionType.Message,
                    new Dictionary<string, object>
                    {
                        { "message", $"New temporary token: {newToken}" }
                    }
                );
                await tempConn1.CloseAsync();

                await Task.Delay(6000);
                Console.WriteLine(@"6s elapsed, new Token supposed to be expired.");
                var tempConn2 = await OpenAsync("127.0.0.1", 11451, newToken);
                await tempConn2.SendAsync(
                    ActionType.Message,
                    new Dictionary<string, object>
                    {
                        { "message", $"New temporary token: {newToken}" }
                    }
                );
                Console.WriteLine(@"Temp Token conn state: " + tempConn2.WebSocket.State);
                await tempConn2.CloseAsync();

                #endregion


                await Task.Delay(5000);
                await connection.CloseAsync();
            });
        }
    }
}