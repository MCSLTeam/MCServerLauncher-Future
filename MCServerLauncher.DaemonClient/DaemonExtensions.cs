using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.DaemonClient;

public static class DaemonExtensions
{
    #region MISC

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

    // TODO 返回Permissions类
    public static async Task<string[]> GetPermissions(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetPermissionsResult>(ActionType.GetPermissions, null, timeout, ct);
        return resp.Permissions;
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

    #endregion

    #region Event

    public static async Task SubscribeEvent(this IDaemon daemon, EventType type, IEventMeta? meta, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(ActionType.SubscribeEvent, new SubscribeEventParameter
        {
            Type = type,
            Meta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings))
        }, timeout, ct);
    }

    public static async Task UnSubscribeEvent(this IDaemon daemon, EventType type, IEventMeta? meta, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(ActionType.UnsubscribeEvent, new UnsubscribeEventParameter
        {
            Type = type,
            Meta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings))
        }, timeout, ct);
    }

    #endregion

    #region File Upload / Download

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
                    Timeout = null, // TODO 可配置的文件块上传间隔的超时时间
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

                    // 更新context
                    context.Done = response.Done;
                    context.LoadedBytes = response.Received;
                    uploadSpeed.Push(bytesRead);

                    if (context.Done) context.OnDone();
                }
                catch (DaemonRequestException e)
                {
                    Log.Error($"[Daemon] Error occurred when uploading file chunk: {e}");

                    throw;
                }

                offset += bytesRead;
            }

            if (cts.IsCancellationRequested)
                await context.CancelAsync().Suppress(typeof(DaemonRequestException)); // 不传入cancellationToken
        }, cts.Token);
        context.NetworkLoadTask = uploadTask;
        return context;
    }

    public static async Task<DownloadContext> DownloadFileAsync(this IDaemon daemon, string path, string dst,
        int chunkSize,
        int timeout = -1, CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<FileDownloadRequestResult>(
            ActionType.FileDownloadRequest,
            new FileDownloadRequestParameter
            {
                Path = path,
                Timeout = null, // TODO 可配置的文件块下载间隔的超时时间
            },
            timeout,
            ct
        );

        var cts = new CancellationTokenSource();
        var context =
            new DownloadContext(resp.FileId, cts, new NetworkLoadSpeed { TotalBytes = resp.Size }, daemon);

        try
        {
            // 预分配空间
            using var fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.SetLength(resp.Size);
        }
        catch (IOException e)
        {
            Log.Error($"[Daemon] Error occurred when starting download file: {e}");
            await context.CancelAsync().Suppress(typeof(DaemonRequestException));
            throw;
        }


        // 后台异步的分块下载文件
        var downloadTask = Task.Run(async () =>
        {
            using var fs = new FileStream(dst, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            long downloadedBytes = 0;
            while (!cts.IsCancellationRequested)
            {
                // 写入数据
                var count = Math.Min(chunkSize, resp.Size - downloadedBytes);

                // TODO DaemonRequestException处理
                var data = await daemon.RequestAsync<FileDownloadRangeResult>(
                    ActionType.FileDownloadRange,
                    new FileDownloadRangeParameter
                    {
                        FileId = resp.FileId,
                        Range =
                            $"{downloadedBytes}..{downloadedBytes + count}"
                    },
                    timeout,
                    ct
                );

                // TODO IOException处理
                await fs.WriteAsync(Encoding.BigEndianUnicode.GetBytes(data.Content), 0, (int)count, ct);
                downloadedBytes += count;

                // 更新context
                context.Done = downloadedBytes >= resp.Size;
                context.LoadedBytes = downloadedBytes;
                if (context.Done)
                {
                    context.OnDone();
                    break;
                }
            }
        }, cts.Token);

        // 更新context
        context.NetworkLoadTask = downloadTask;

        return context;
    }

    #endregion

    #region File Info

    public static async Task<(
            DirectoryEntry.DirectoryInformation[],
            DirectoryEntry.FileInformation[],
            string?
            )>
        GetDirectoryInfoAsync(this IDaemon daemon, string path, int timeout = -1,
            CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetDirectoryInfoResult>(
            ActionType.GetDirectoryInfo,
            new GetDirectoryInfoParameter
            {
                Path = path
            },
            timeout,
            ct
        );

        return (resp.Directories, resp.Files, resp.Parent);
    }

    public static async Task<FileMetadata> GetFileInfoAsync(this IDaemon daemon, string path, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetFileInfoResult>(
            ActionType.GetFileInfo,
            new GetFileInfoParameter
            {
                Path = path
            },
            timeout,
            ct
        );

        return resp.Meta;
    }

    #endregion

    #region Instances

    public static async Task<bool> StartInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<StartInstanceResult>(
            ActionType.StartInstance,
            new StartInstanceParameter
            {
                Id = id
            }, timeout, ct);
        return resp.Done;
    }

    public static async Task<bool> StopInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<StopInstanceResult>(
            ActionType.StopInstance,
            new StopInstanceParameter
            {
                Id = id
            }, timeout, ct);
        return resp.Done;
    }

    public static async Task SentToInstanceAsync(this IDaemon daemon, Guid id, string message, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(
            ActionType.SendToInstance,
            new SendToInstanceParameter
            {
                Id = id,
                Message = message
            }, timeout, ct);
    }

    public static async Task KillInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(
            ActionType.KillInstance,
            new KillInstanceParameter
            {
                Id = id
            }, timeout, ct);
    }

    public static async Task<bool> TryAddInstanceAsync(this IDaemon daemon, InstanceFactorySetting setting,
        int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<AddInstanceResult>(
            ActionType.AddInstance,
            new AddInstanceParameter
            {
                Setting = setting
            }, timeout, ct);
        return resp.Done;
    }

    public static async Task<bool> TryRemoveInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<RemoveInstanceResult>(
            ActionType.RemoveInstance,
            new RemoveInstanceParameter
            {
                Id = id
            }, timeout, ct);
        return resp.Done;
    }

    public static async Task<InstanceStatus> GetInstanceStatusAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetInstanceStatusResult>(
            ActionType.GetInstanceStatus,
            new GetInstanceStatusParameter
            {
                Id = id
            }, timeout, ct);
        return resp.Status;
    }

    public static async Task<Dictionary<Guid, InstanceStatus>> GetAllStatusAsync(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetAllStatusResult>(
            ActionType.GetAllStatus,
            new EmptyActionParameter(), timeout, ct);
        return resp.Status;
    }

    #endregion
}