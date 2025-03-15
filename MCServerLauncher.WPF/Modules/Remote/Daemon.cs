using MCServerLauncher.Common;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace MCServerLauncher.WPF.Modules.Remote;

public class Daemon : IDaemon
{
    public WebSocketState WebsocketState => Connection.WebSocket.State;
    public ClientConnection? Connection { get; private set; }
    public bool IsClosed => Connection.Closed;

    /// <summary>
    ///     心跳包是否超时
    /// </summary>
    public bool PingLost => Connection.PingLost;

    /// <summary>
    ///     上次ping时间
    /// </summary>
    public DateTime LastPing => Connection.LastPong;

    public async Task<UploadContext> UploadFileAsync(string path, string dst, int chunkSize)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha1 = await Utils.FileSha1(fs);

        var size = new FileInfo(path).Length;

        var fileId = (await RequestAsync(ActionType.FileUploadRequest, new Dictionary<string, object>
        {
            { "path", dst },
            { "sha1", sha1 },
            { "chunk_size", chunkSize },
            { "size", size }
        }))["file_id"]!.ToString();
        var cts = new CancellationTokenSource();
        var uploadSpeed = new NetworkLoadSpeed
        {
            TotalBytes = size
        };

        var context = new UploadContext(fileId, cts, uploadSpeed, fs, this);

        // 后台异步的分块上传文件
        var uploadTask = Task.Run(async () =>
        {
            var buffer = new byte[chunkSize];
            var offset = 0;
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, chunkSize, cts.Token)) > 0 &&
                   !cts.IsCancellationRequested)
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
                    var response = await UploadFileChunk(fileId, offset, strData, cts.Token);
                    context.Done = response["done"]!.ToObject<bool>();
                    context.Received = response["received"]!.ToObject<long>();
                    uploadSpeed.Push(bytesRead);

                    if (context.Done) context.OnDone();
                }
                catch (Exception e)
                {
                    Log.Error($"[Daemon] Error occurred when uploading file chunk: {e}");
                }

                offset += bytesRead;
            }
        }, cts.Token);
        context.UploadTask = uploadTask;
        return context;
    }

    public async Task UploadFileCancelAsync(UploadContext context)
    {
        await (context.State switch
        {
            UploadContextState.Opening => context.Cancel(),
            UploadContextState.Cancelling => RequestAsync(ActionType.FileUploadCancel,
                new Dictionary<string, object>
                {
                    { "file_id", context.FileId }
                }),
            _ => Task.CompletedTask
        });
    }


    public async Task<List<JavaInfo>> GetJavaListAsync()
    {
        var rv = await RequestAsync(ActionType.GetJavaList, new Dictionary<string, object>());
        return rv["java_list"]!.ToObject<List<JavaInfo>>()!;
    }

    public async Task CloseAsync()
    {
#pragma warning disable CS8602
        await Connection?.CloseAsync();
#pragma warning restore CS8602
    }

    public async Task<bool> PingAsync()
    {
        var pingTask = RequestAsync(ActionType.Ping, new Dictionary<string, object>());
        return await Task.WhenAny(pingTask, Task.Delay(Connection?.Config.PingTimeout ?? 5000)) == pingTask;
    }

    private async Task<string> UploadFileRequestAsync(string path, string dst, int chunkSize)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha1 = await Utils.FileSha1(fs);

        var size = new FileInfo(path).Length;

        return (await RequestAsync(ActionType.FileUploadRequest, new Dictionary<string, object>
        {
            { "path", dst },
            { "sha1", sha1 },
            { "chunk_size", chunkSize },
            { "size", size }
        }))["file_id"]!.ToString();
    }

    private async Task<JObject> UploadFileChunk(string fileId, int offset, string strData,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            { "file_id", fileId },
            { "offset", offset },
            { "data", strData }
        };
        return await RequestAsync(ActionType.FileUploadChunk, data, cancellationToken: cancellationToken);
    }

    public static async Task<string?> LoginAsync(string address, int port, string usr, string token, bool isSecure,
        uint? expired)
    {
        var url = $"{(isSecure ? "https" : "http")}://{address}:{port}/login?usr={usr}&pwd={token}";
        if (expired.HasValue) url += $"&expired={expired}";
        return await Utils.HttpPost(url);
    }

    /// <summary>
    ///     连接远程服务器
    /// </summary>
    /// <param name="address">ip地址</param>
    /// <param name="port">端口</param>
    /// <param name="token">jwt token</param>
    /// <param name="isSecure">是否使用wss</param>
    /// <param name="config">Daemon连接配置</param>
    /// <param name="timeout">连接超时时间</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="WebSocketException">Daemon连接失败</exception>
    /// <exception cref="TimeoutException">连接超时</exception>
    /// <returns></returns>
    public static async Task<IDaemon> OpenAsync(string address, int port, string token,
        bool isSecure, ClientConnectionConfig config, int timeout = 5000, CancellationToken cancellationToken = default)
    {
        var connection = await ClientConnection.OpenAsync(address, port, token, isSecure, config, cancellationToken).TimeoutAfter(timeout);
        return new Daemon
        {
            Connection = connection
        };
    }

    /// <summary>
    ///     RPC中枢
    /// </summary>
    /// <param name="actionType"></param>
    /// <param name="args"></param>
    /// <param name="echo"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<JObject> RequestAsync(ActionType actionType, Dictionary<string, object> args,
        string? echo = null, int timeout = 5000, CancellationToken cancellationToken = default)
    {
#pragma warning disable CS8602
        return await Connection?.RequestAsync(actionType, args, echo, timeout, cancellationToken);
#pragma warning restore CS8602
    }

    public async Task<JObject> GetSystemInfoAsync()
    {
        return await RequestAsync(ActionType.GetSystemInfo, new Dictionary<string, object>());
    }

    //public static async Task RunTest()
    //{
    //    var usr = "admin";
    //    var pwd = "3a9eff1b-4723-407e-a072-30da28b00f4a";
    //    // var pwd1 = "Hqwd7H5WHLIgeyNu00jMlA==";
    //    var isSecure = false;
    //    var ip = "127.0.0.1";
    //    var port = 11451;

    //    // login
    //    var token = await LoginAsync(ip, port, usr, pwd, isSecure, 86400) ?? "token not found";
    //    Log.Debug($"Token got: {token}");

    //    try
    //    {
    //        var daemon = await OpenAsync(ip, port, token, isSecure, new ClientConnectionConfig
    //        {
    //            MaxPingPacketLost = 3,
    //            PendingRequestCapacity = 100,
    //            PingInterval = TimeSpan.FromSeconds(1),
    //            PingTimeout = 3000
    //        });
    //        Log.Information("[daemon] connected: {0}", await daemon.PingAsync());
    //        await Task.Delay(3000);
    //        var rv = await daemon.GetJavaListAsync();
    //        rv.ForEach(x => Log.Debug($"Java: {x.ToString()}"));
    //        await Task.Delay(3001);
    //        await daemon.CloseAsync();
    //    }
    //    catch (WebSocketException e)
    //    {
    //        Log.Error($"[Daemon] Websocket Error occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //    catch (TimeoutException e)
    //    {
    //        Log.Error($"[Daemon] Timeout occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //    catch (Exception e)
    //    {
    //        Log.Error($"[Daemon] Error occurred when connecting to daemon(ws://{ip}:{port}): {e}");
    //    }
    //}
}