using System.Text.RegularExpressions;
using MCServerLauncher.Common;
using MCServerLauncher.Common.System;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action(rpc)处理器。
///     Action是ws交互格式的一种，他代表的是一个远程过程调用: C->S,处理后返回json数据给C
/// </summary>
internal class ActionService : IActionService
{
    private static readonly Regex RangePattern = new(@"^(\d+)..(\d+)$");
    private readonly IEventService _eventService;
    private readonly IInstanceManager _instanceManager;
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;

    // DI
    private readonly IWebJsonConverter _webJsonConverter;

    // DI constructor
    public ActionService(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IWebJsonConverter webJsonConverter, IInstanceManager instanceManager, IEventService eventService)
    {
        _javaScannerCache = javaScannerCache;
        _webJsonConverter = webJsonConverter;
        _instanceManager = instanceManager;
        _eventService = eventService;
    }

    /// <summary>
    ///     Action(rpc)处理中枢
    /// </summary>
    /// <param name="type">Action类型</param>
    /// <param name="data">Action数据</param>
    /// <returns>Action响应</returns>
    /// <exception cref="NotImplementedException">未实现的Action</exception>
    public async Task<JObject> Routine(
        ActionType type,
        JObject? data
    )
    {
        try
        {
            return type switch
            {
                ActionType.FileUploadChunk => await FileUploadChunkHandler(FileUploadChunk.Of(data)),
                ActionType.FileUploadRequest => FileUploadRequestHandler(FileUploadRequest.Of(data)),
                ActionType.Ping => PingHandler(Ping.Of(data)),
                ActionType.FileUploadCancel => FileUploadCancelHandler(FileUploadCancel.Of(data)),
                ActionType.GetJavaList => await GetJavaListHandler(GetJavaList.Of(data)),
                ActionType.FileDownloadRequest => await FileDownloadRequestHandler(
                    FileDownloadRequest.Of(data)),
                ActionType.FileDownloadClose => FileDownloadCloseHandler(FileDownloadClose.Of(data)),
                ActionType.FileDownloadRange => await FileDownloadRangeHandler(
                    FileDownloadRange.Of(data)),
                ActionType.GetDirectoryInfo => GetDirectoryInfoHandler(GetDirectoryInfo.Of(data)),
                ActionType.GetFileInfo => GetFileInfoHandler(GetFileInfo.Of(data)),
                ActionType.TryStartInstance => TryStartInstanceHandler(TryStartInstance.Of(data)),
                ActionType.SendToInstance => SendToInstanceHandler(SendToInstance.Of(data)),
                ActionType.GetAllStatus => GetAllStatusHandler(GetAllStatus.Of(data)),
                ActionType.TryStopInstance => TryStopInstanceHandler(TryStopInstance.Of(data)),
                ActionType.GetSystemInfo => await GetSystemInfoHandler(GetSystemInfo.Of(data)),
                _ => throw new NotImplementedException()
            };
        }
        catch (Exception e)
        {
            return Err(e.Message, 1500);
        }
    }

    public JObject Err(string? message, int code = 1400)
    {
        return new JObject
        {
            ["status"] = "error",
            ["retcode"] = code,
            ["data"] = new JObject
            {
                ["error_message"] = message
            }
        };
    }

    public JObject Ok(JObject? data = null)
    {
        return new JObject
        {
            ["status"] = "ok",
            ["retcode"] = 0,
            ["data"] = data ?? new JObject()
        };
    }

    private async Task<JObject> FileUploadChunkHandler(FileUploadChunk data)
    {
        if (data.FileId == Guid.Empty) return Err("Invalid file id", ActionType.FileUploadChunk);

        var (done, received) = await FileManager.FileUploadChunk(data.FileId, data.Offset, data.Data);
        return Ok(FileUploadChunk.Response(done, received));
    }

    private async Task<JObject> GetJavaListHandler(GetJavaList data)
    {
        return Ok(GetJavaList.Response(await _javaScannerCache.Value));
    }

    private JObject FileUploadRequestHandler(FileUploadRequest data)
    {
        var fileId = FileManager.FileUploadRequest(
            data.Path,
            data.Size,
            data.Timeout.Map(t => TimeSpan.FromMilliseconds(t)),
            data.Sha1
        );

        return fileId == Guid.Empty
            ? Err("Failed to pre-allocate space", ActionType.FileUploadRequest, 1401)
            : Ok(FileUploadRequest.Response(fileId));
    }

    private JObject PingHandler(Ping data)
    {
        return Ok(Ping.Response(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()));
    }

    private JObject FileUploadCancelHandler(FileUploadCancel data)
    {
        return FileManager.FileUploadCancel(data.FileId)
            ? Ok()
            : Err("Failed to cancel file upload", ActionType.FileUploadCancel, 1402);
    }

    private JObject FileDownloadCloseHandler(FileDownloadClose data)
    {
        FileManager.FileDownloadClose(data.FileId);
        return Ok(FileDownloadClose.Response());
    }

    private async Task<JObject> FileDownloadRangeHandler(FileDownloadRange data)
    {
        var match = RangePattern.Match(data.Range);
        if (!match.Success) return Err("Invalid range format", ActionType.FileDownloadRange);

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        return Ok(FileDownloadRange.Response(await FileManager.FileDownloadRange(data.FileId, from, to)));
    }

    private JObject GetDirectoryInfoHandler(GetDirectoryInfo data)
    {
        var dirEntry = FileManager.GetDirectoryInfo(data.Path);
        return Ok(GetDirectoryInfo.Response(dirEntry.Parent, dirEntry.Files, dirEntry.Directories));
    }

    private JObject GetFileInfoHandler(GetFileInfo data)
    {
        return Ok(GetFileInfo.Response(FileManager.GetFileInfo(data.Path)));
    }

    private async Task<JObject> FileDownloadRequestHandler(FileDownloadRequest data)
    {
        var info = await FileManager.FileDownloadRequest(data.Path,
            data.Timeout.Map(t => TimeSpan.FromMilliseconds(t)));
        return Ok(FileDownloadRequest.Response(info.Id, info.Size, info.Sha1));
    }

    private JObject TryStartInstanceHandler(TryStartInstance data)
    {
        var rv = _instanceManager.TryStartInstance(data.Id, out var instance);
        if (instance == null) return Err("Instance not found", ActionType.TryStartInstance);

        var logPrefix = $"{instance.Config.Name}({data.Id})";

        if (rv)
        {
            Action<string?> handler = msg =>
            {
                if (msg != null)
                    _eventService.OnEvent(
                        EventType.InstanceLog,
                        new Events.InstanceLogEvent(
                            logPrefix,
                            msg
                        )
                    );
            };
            instance.OnLog -= handler;
            instance.OnLog += handler;
        }

        return Ok(TryStartInstance.Response(rv));
    }

    private JObject TryStopInstanceHandler(TryStopInstance data)
    {
        return Ok(TryStopInstance.Response(_instanceManager.TryStopInstance(data.Id)));
    }

    private JObject SendToInstanceHandler(SendToInstance data)
    {
        _instanceManager.SendToInstance(data.Id, data.Message);
        return Ok(SendToInstance.Response());
    }

    private JObject GetAllStatusHandler(GetAllStatus data)
    {
        return Ok(GetAllStatus.Response(_instanceManager.GetAllStatus()));
    }

    private async Task<JObject> GetSystemInfoHandler(GetSystemInfo data)
    {
        return Ok(GetSystemInfo.Response(await SystemInfo.Get()));
    }

    private JObject Err(string message, ActionType type, int code = 1400)
    {
        Log.Error("Error while handling Action {0}: {1}", type, message);
        return Err(message, code);
    }
}