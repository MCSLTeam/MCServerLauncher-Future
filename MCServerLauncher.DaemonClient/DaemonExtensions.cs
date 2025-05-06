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
using MCServerLauncher.Common.ProtoType.Status;
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
    public static async Task<JavaInfo[]> GetJavaListAsync(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var result = await daemon.RequestAsync<GetJavaListResult>(ActionType.GetJavaList, null, timeout, ct);
        return result.JavaList;
    }

    #endregion

    #region Event

    /// <summary>
    ///     Action: 订阅事件
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="type"></param>
    /// <param name="meta"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static Task SubscribeEvent(this IDaemon daemon, EventType type, IEventMeta? meta, int timeout = -1,
        CancellationToken ct = default)
    {
        return InternalSubscribeEvent(daemon, type, meta, true, timeout, ct);
    }

    internal static async Task InternalSubscribeEvent(this IDaemon daemon, EventType type, IEventMeta? meta,
        bool persistent, int timeout = -1,
        CancellationToken ct = default)
    {
        if (persistent) daemon.SubscribedEvents.EventSet.Add((type, meta));

        await daemon.RequestAsync(ActionType.SubscribeEvent, new SubscribeEventParameter
        {
            Type = type,
            Meta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings))
        }, timeout, ct);
    }

    /// <summary>
    ///     Action: 取消订阅事件
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="type"></param>
    /// <param name="meta"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    public static async Task UnSubscribeEvent(this IDaemon daemon, EventType type, IEventMeta? meta, int timeout = -1,
        CancellationToken ct = default)
    {
        daemon.SubscribedEvents.EventSet.Remove((type, meta));
        await daemon.RequestAsync(ActionType.UnsubscribeEvent, new UnsubscribeEventParameter
        {
            Type = type,
            Meta = meta is null ? null : JToken.FromObject(meta, JsonSerializer.Create(JsonSettings.Settings))
        }, timeout, ct);
    }

    #endregion

    #region File Upload / Download

    /// <summary>
    ///     action: 上传文件
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

    /// <summary>
    ///     action: 下载文件
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="path">本地路径</param>
    /// <param name="dst">daemon上的目标路径</param>
    /// <param name="chunkSize">分块上传大小</param>
    /// <param name="timeout">文件块下载间隔超时时间</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<DownloadContext> DownloadFileAsync(this IDaemon daemon, string path, string dst,
        int chunkSize,
        int timeout = -1, CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<FileDownloadRequestResult>(
            ActionType.FileDownloadRequest,
            new FileDownloadRequestParameter
            {
                Path = path,
                Timeout = null // TODO 可配置的文件块下载间隔的超时时间
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

    /// <summary>
    ///     Action: 获取目录信息
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="path"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
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

    /// <summary>
    ///     Action: 获取文件信息
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="path"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
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

    /// <summary>
    ///     Action: 启动实例
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    public static async Task StartInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(
            ActionType.StartInstance,
            new StartInstanceParameter
            {
                Id = id
            }, timeout, ct);
    }

    /// <summary>
    ///     Action: 停止实例
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    public static async Task StopInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(
            ActionType.StopInstance,
            new StopInstanceParameter
            {
                Id = id
            }, timeout, ct);
    }

    /// <summary>
    ///     Action: 向实例发送消息
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="message"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
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

    /// <summary>
    ///     Action: 杀死实例
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
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

    /// <summary>
    ///     Action: 添加实例
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="setting"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<InstanceConfig> AddInstanceAsync(this IDaemon daemon, InstanceFactorySetting setting,
        int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<AddInstanceResult>(
            ActionType.AddInstance,
            new AddInstanceParameter
            {
                Setting = setting
            }, timeout, ct);
        return resp.Config;
    }

    /// <summary>
    ///     Action: 删除实例
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    public static async Task RemoveInstanceAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        await daemon.RequestAsync(
            ActionType.RemoveInstance,
            new RemoveInstanceParameter
            {
                Id = id
            }, timeout, ct);
    }

    /// <summary>
    ///     Action: 获取实例报告
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="id"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<InstanceReport> GetInstanceReportAsync(this IDaemon daemon, Guid id, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetInstanceReportResult>(
            ActionType.GetInstanceReport,
            new GetInstanceReportParameter
            {
                Id = id
            }, timeout, ct);
        return resp.Report;
    }

    /// <summary>
    ///     Action: 获取所有实例报告
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="timeout"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<Dictionary<Guid, InstanceReport>> GetAllReportsAsync(this IDaemon daemon, int timeout = -1,
        CancellationToken ct = default)
    {
        var resp = await daemon.RequestAsync<GetAllReportsResult>(
            ActionType.GetAllReports,
            new EmptyActionParameter(), timeout, ct);
        return resp.Reports;
    }

    #endregion
}