using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.Utils;
using Serilog;

namespace MCServerLauncher.DaemonClient;

public static class DaemonExtensions
{
    /// <summary>
    ///     rpc: ping
    /// </summary>
    /// <param name="daemon">daemon</param>
    /// <param name="timeout">超时时间,单位毫秒,-1表示无限制</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>延迟</returns>
    public static async Task<long> PingAsync(this IDaemon daemon, int timeout = -1, CancellationToken ct = default)
    {
        var before = DateTime.UtcNow.ToUnixTimeMilliSeconds();
        var pingTask = await daemon.RequestAsync<PingResult>(ActionType.Ping, null, timeout, ct);
        var now = DateTime.UtcNow.ToUnixTimeMilliSeconds();

        return (now - pingTask.Time + (pingTask.Time - before)) / 2;
    }
    /// <summary>
    ///     rpc: 获取系统信息
    /// </summary>
    /// <param name="daemon">daemon</param>
    /// <param name="timeout">超时时间,单位毫秒,-1表示无限制</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>系统信息</returns>
    public static async Task<SystemInfo> GetSystemInfoAsync(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetSystemInfoResult>(ActionType.GetSystemInfo, null, timeout, ct);
        return resp.Info;
    }

    /// <summary>
    ///     rpc: 获取java列表
    /// </summary>
    /// <param name="daemon">daemon</param>
    /// <param name="timeout">超时时间,单位毫秒,-1表示无限制</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>Java列表</returns>
    public static async Task<List<JavaInfo>> GetJavaListAsync(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var result = await daemon.RequestAsync<GetJavaListResult>(ActionType.GetJavaList, null, timeout, ct);
        return result.JavaList;
    }

    /// <summary>
    ///     rpc: 上传文件
    /// </summary>
    /// <param name="daemon">daemon</param>
    /// <param name="path">目标上传文件的本地路径</param>
    /// <param name="dst">位于服务器上的上传目标路径</param>
    /// <param name="chunkSize">传输分块大小</param>
    /// <param name="timeout">超时时间,单位毫秒,-1表示无限制</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>上传上下文,用于查看进度,剩余时间,上传状态和取消上传等</returns>
    public static async Task<UploadContext> UploadFileAsync(this IDaemon daemon, string path, string dst, int chunkSize,
        int timeout = -1, CancellationToken ct = default)
    {
        string sha1;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            sha1 = await Utils.FileSha1(fs);
        }

        var size = new FileInfo(path).Length;

        var fileId = (
            await daemon.RequestAsync<FileUploadRequestResult>(
                ActionType.FileUploadRequest,
                new FileUploadRequestParameter
                {
                    Path = dst,
                    Sha1 = sha1,
                    Timeout = null,
                    Size = size
                },
                timeout,
                ct)
        ).FileId;
        var cts = new CancellationTokenSource();

        var uploadSpeed = new NetworkLoadSpeed
        {
            TotalBytes = size
        };

        var context = new UploadContext(fileId, cts, uploadSpeed, daemon);

        // 后台异步的分块上传文件
        var uploadTask = Task.Run(async () =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

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
                    var response = await daemon.RequestAsync<FileUploadChunkResult>(
                        ActionType.FileUploadChunk,
                        new FileUploadChunkParameter
                        {
                            Data = strData,
                            FileId = fileId,
                            Offset = offset
                        }, cancellationToken: cts.Token);
                    context.Done = response.Done;
                    context.Received = response.Received;
                    uploadSpeed.Push(bytesRead);

                    if (context.Done) context.OnDone();
                }
                catch (Exception e)
                {
                    Log.Error($"[Daemon] Error occurred when uploading file chunk: {e}");
                    throw;
                }

                offset += bytesRead;
            }

            if (cts.IsCancellationRequested) await context.Cancel();
        }, cts.Token);
        context.UploadTask = uploadTask;
        return context;
    }

    /// <summary>
    ///     rpc: 取消上传
    /// </summary>
    /// <param name="daemon">daemon</param>
    /// <param name="context">要取消的上传上下文</param>
    /// <param name="timeout">超时时间,单位毫秒,-1表示无限制</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public static async Task UploadFileCancelAsync(this IDaemon daemon, UploadContext context, int timeout = -1,
        CancellationToken ct = default)
    {
        await (context.State switch
        {
            UploadContextState.Opening => context.Cancel(),
            UploadContextState.Cancelling => daemon.RequestAsync(ActionType.FileUploadCancel,
                new FileUploadCancelParameter
                {
                    FileId = context.FileId
                }, timeout, ct),
            _ => Task.CompletedTask
        });
    }
}