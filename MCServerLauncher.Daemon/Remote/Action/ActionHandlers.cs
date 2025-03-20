using System.Text.RegularExpressions;
using MCServerLauncher.Common;
using MCServerLauncher.Common.System;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action处理脚本: 以{SomeActionName}Handler命名的private函数将作为some_action_name命名的action的处理函数
///     函数签名中的各种形参对应action jsonrpc中的data中的各种字段(WsServiceContext类型的形参除外, 它是自动注入的),
///     函数签名中的各种形参是由data中的各种字段经过IWebJsonSerializer反序列化后得到的, 同时也可以将类型设定为JToken自行反序列化
///     Action handler，返回值只能是JObject, JObject?或<![CDATA[ValueTask<JObject>]]>
/// </summary>
public class ActionHandlers
{
    private static readonly Regex RangePattern = new(@"^(\d+)..(\d+)$");
    private readonly IEventService _eventService;
    private readonly IInstanceManager _instanceManager;
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;

    public ActionHandlers(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IInstanceManager instanceManager, IEventService eventService, IWebJsonConverter webJsonConverter)
    {
        _javaScannerCache = javaScannerCache;
        _instanceManager = instanceManager;
        _eventService = eventService;

        Serializer = webJsonConverter.GetSerializer();
    }

    private JsonSerializer Serializer { get; }

    #region Event System

    [Permission("always")]
    private JObject? SubscribeEventHandler(EventType type, JToken meta, WsServiceContext ctx)
    {
        ctx.SubscribeEvent(type, type.GetEventMeta(meta, Serializer));
        return default;
    }

    [Permission("always")]
    private JObject? UnsubscribeEventHandler(EventType type, JToken meta, WsServiceContext ctx)
    {
        ctx.UnsubscribeEvent(type, type.GetEventMeta(meta, Serializer));
        return default;
    }

    #endregion

    #region MISC

    [Permission("always")]
    private JObject PingHandler()
    {
        return new JObject
        {
            ["time"] = JToken.FromObject(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(), Serializer)
        };
    }

    [Permission("always")]
    private async ValueTask<JObject> GetSystemInfoHandler()
    {
        return new JObject
        {
            ["info"] = JToken.FromObject(await SystemInfo.Get(), Serializer)
        };
    }

    [Permission("always")]
    private JObject GetPermissionsHandler(WsServiceContext ctx)
    {
        return new JObject
        {
            ["permissions"] = JToken.FromObject(ctx.Permissions.PermissionList, Serializer)
        };
    }

    [SimplePermission("mcsl.daemon.java_list")]
    private async ValueTask<JObject> GetJavaListHandler()
    {
        return new JObject
        {
            ["java_list"] = JToken.FromObject(await _javaScannerCache.Value, Serializer)
        };
    }

    #endregion

    #region File Upload

    [SimplePermission("mcsl.daemon.file.upload")]
    private async ValueTask<JObject> FileUploadChunkHandler(Guid fileId, long offset, string data)
    {
        if (fileId == Guid.Empty)
            throw new ActionExecutionException(1400, "Invalid file id");

        var (done, received) = await FileManager.FileUploadChunk(fileId,
            offset, data);
        return new JObject
        {
            ["done"] = JToken.FromObject(done, Serializer),
            ["received"] = JToken.FromObject(received, Serializer)
        };
    }

    [SimplePermission("mcsl.daemon.file.upload")]
    private JObject FileUploadRequestHandler(string path, long size, long? timeout, string sha1)
    {
        var fileId = FileManager.FileUploadRequest(
            path,
            size,
            timeout.Map(t => TimeSpan.FromMilliseconds(t)),
            sha1
        );

        return fileId == Guid.Empty
            ? throw new ActionExecutionException(1401, "Failed to pre-allocate space")
            : new JObject
            {
                ["file_id"] = JToken.FromObject(fileId, Serializer)
            };
    }


    [SimplePermission("mcsl.daemon.file.upload")]
    private JObject? FileUploadCancelHandler(Guid fileId)
    {
        return FileManager.FileUploadCancel(fileId)
            ? default
            : throw new ActionExecutionException(1402, "Failed to cancel file upload");
    }

    #endregion

    #region File Download

    [SimplePermission("mcsl.daemon.file.download")]
    private async ValueTask<JObject> FileDownloadRequestHandler(string path, long? timeout)
    {
        var info = await FileManager.FileDownloadRequest(path,
            timeout.Map(t => TimeSpan.FromMilliseconds(t)));
        return new JObject
        {
            ["file_id"] = JToken.FromObject(info.Id, Serializer),
            ["size"] = JToken.FromObject(info.Size, Serializer),
            ["sha1"] = JToken.FromObject(info.Sha1, Serializer)
        };
    }

    [SimplePermission("mcsl.daemon.file.download")]
    private JObject? FileDownloadCloseHandler(Guid fileId)
    {
        FileManager.FileDownloadClose(fileId);
        return default;
    }

    [SimplePermission("mcsl.daemon.file.download")]
    private async ValueTask<JObject> FileDownloadChunkHandler(Guid fileId, string range)
    {
        var match = RangePattern.Match(range);
        if (!match.Success) throw new ActionExecutionException(1400, "Invalid range format");

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        return new JObject
        {
            ["content"] =
                JToken.FromObject(await FileManager.FileDownloadRange(fileId, from, to),
                    Serializer)
        };
    }

    #endregion

    #region File Info

    [SimplePermission("mcsl.daemon.file.info.directory")]
    private JObject GetDirectoryInfoHandler(string path)
    {
        var dirEntry = FileManager.GetDirectoryInfo(path);
        return new JObject
        {
            ["parent"] = JToken.FromObject(dirEntry.Parent ?? "", Serializer),
            ["files"] = JToken.FromObject(dirEntry.Files, Serializer),
            ["directories"] = JToken.FromObject(dirEntry.Directories, Serializer)
        };
    }

    [SimplePermission("mcsl.daemon.file.info.file")]
    private JObject GetFileInfoHandler(string path)
    {
        return new JObject
        {
            ["meta"] = JToken.FromObject(FileManager.GetFileInfo(path), Serializer)
        };
    }

    #endregion

    #region Minecraft Instance

    [Permission("always")]
    private JObject TryStartInstanceHandler(Guid id)
    {
        var rv = _instanceManager.TryStartInstance(id, out var instance);
        if (instance == null) throw new ActionExecutionException(1400, "Instance not found");

        if (rv)
        {
            Action<string?> handler = msg =>
            {
                if (msg != null)
                    _eventService.OnInstanceLog(id, msg);
            };
            instance.OnLog -= handler;
            instance.OnLog += handler;
        }

        return new JObject
        {
            ["done"] = JToken.FromObject(rv, Serializer)
        };
    }

    [Permission("always")]
    private JObject TryStopInstanceHandler(Guid id)
    {
        return new JObject
        {
            ["done"] = JToken.FromObject(_instanceManager.TryStopInstance(id), Serializer)
        };
    }

    [Permission("always")]
    private JObject? SendToInstanceHandler(Guid id, string message)
    {
        _instanceManager.SendToInstance(id, message);
        return default;
    }

    [Permission("always")]
    private JObject GetAllStatusHandler()
    {
        return new JObject
        {
            ["status"] = JToken.FromObject(_instanceManager.GetAllStatus(), Serializer)
        };
    }

    #endregion
}