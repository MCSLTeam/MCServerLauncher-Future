using System.Text.RegularExpressions;
using MCServerLauncher.Common;
using MCServerLauncher.Common.System;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Cache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionHandler
{
    private static readonly Regex RangePattern = new(@"^(\d+)..(\d+)$");
    private readonly IEventService _eventService;
    private readonly IInstanceManager _instanceManager;
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings.Settings);

    public ActionHandler(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IInstanceManager instanceManager, IEventService eventService)
    {
        _javaScannerCache = javaScannerCache;
        _instanceManager = instanceManager;
        _eventService = eventService;
    }

    private async Task<JObject> FileUploadChunkHandler(JObject data)
    {
        if (data["file_id"]!.ToObject<Guid>() == Guid.Empty)
            throw new ActionExecutionException(1400, "Invalid file id");

        var (done, received) = await FileManager.FileUploadChunk(data["file_id"]!.ToObject<Guid>(),
            data["offset"]!.ToObject<long>(), data["data"]!.ToString());
        return ResponseUtils.Ok(new JObject
        {
            ["done"] = JToken.FromObject(done, Serializer),
            ["received"] = JToken.FromObject(received, Serializer)
        });
    }

    private async Task<JObject> GetJavaListHandler()
    {
        return ResponseUtils.Ok(new JObject
        {
            ["java_list"] = JToken.FromObject(await _javaScannerCache.Value, Serializer)
        });
    }

    private JObject FileUploadRequestHandler(JObject data)
    {
        var fileId = FileManager.FileUploadRequest(
            data["path"]!.ToString(),
            data["size"]!.ToObject<long>(),
            data["timeout"]?.ToObject<long?>().Map(t => TimeSpan.FromMilliseconds(t)),
            data["sha1"]!.ToString()
        );

        return fileId == Guid.Empty
            ? throw new ActionExecutionException(1401, "Failed to pre-allocate space")
            : ResponseUtils.Ok(new JObject
            {
                ["file_id"] = JToken.FromObject(fileId, Serializer)
            });
    }

    private JObject PingHandler()
    {
        return ResponseUtils.Ok(new JObject
        {
            ["time"] = JToken.FromObject(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(), Serializer)
        });
    }

    private JObject FileUploadCancelHandler(JObject data)
    {
        return FileManager.FileUploadCancel(data["file_id"]!.ToObject<Guid>())
            ? ResponseUtils.Ok()
            : throw new ActionExecutionException(1402, "Failed to cancel file upload");
    }

    private JObject FileDownloadCloseHandler(JObject data)
    {
        FileManager.FileDownloadClose(data["file_id"]!.ToObject<Guid>());
        return ResponseUtils.Ok();
    }

    private async Task<JObject> FileDownloadRangeHandler(JObject data)
    {
        var match = RangePattern.Match(data["range"]!.ToString());
        if (!match.Success) throw new ActionExecutionException(1400, "Invalid range format");

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        return ResponseUtils.Ok(new JObject
        {
            ["content"] =
                JToken.FromObject(await FileManager.FileDownloadRange(data["file_id"]!.ToObject<Guid>(), from, to),
                    Serializer)
        });
    }

    private JObject GetDirectoryInfoHandler(JObject data)
    {
        var dirEntry = FileManager.GetDirectoryInfo(data["path"]!.ToString());
        return ResponseUtils.Ok(new JObject
        {
            ["parent"] = JToken.FromObject(dirEntry.Parent, Serializer),
            ["files"] = JToken.FromObject(dirEntry.Files, Serializer),
            ["directories"] = JToken.FromObject(dirEntry.Directories, Serializer)
        });
    }

    private JObject GetFileInfoHandler(JObject data)
    {
        return ResponseUtils.Ok(new JObject
        {
            ["meta"] = JToken.FromObject(FileManager.GetFileInfo(data["path"]!.ToString()), Serializer)
        });
    }

    private async Task<JObject> FileDownloadRequestHandler(JObject data)
    {
        var info = await FileManager.FileDownloadRequest(data["path"]!.ToString(),
            data["timeout"]?.ToObject<long?>().Map(t => TimeSpan.FromMilliseconds(t)));
        return ResponseUtils.Ok(new JObject
        {
            ["file_id"] = JToken.FromObject(info.Id, Serializer),
            ["size"] = JToken.FromObject(info.Size, Serializer),
            ["sha1"] = JToken.FromObject(info.Sha1, Serializer)
        });
    }

    private JObject TryStartInstanceHandler(JObject data)
    {
        var rv = _instanceManager.TryStartInstance(data["id"]!.ToObject<Guid>(), out var instance);
        if (instance == null) throw new ActionExecutionException(1400, "Instance not found");

        var logPrefix = $"{instance.Config.Name}({data["id"]!.ToObject<Guid>()})";

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

        return ResponseUtils.Ok(new JObject
        {
            ["done"] = JToken.FromObject(rv, Serializer)
        });
    }

    private JObject TryStopInstanceHandler(JObject data)
    {
        return ResponseUtils.Ok(new JObject
        {
            ["done"] = JToken.FromObject(_instanceManager.TryStopInstance(data["id"]!.ToObject<Guid>()), Serializer)
        });
    }

    private JObject SendToInstanceHandler(JObject data)
    {
        _instanceManager.SendToInstance(data["id"]!.ToObject<Guid>(), data["message"]!.ToString());
        return ResponseUtils.Ok();
    }

    private JObject GetAllStatusHandler()
    {
        return ResponseUtils.Ok(new JObject
        {
            ["status"] = JToken.FromObject(_instanceManager.GetAllStatus(), Serializer),
        });
    }

    private async Task<JObject> GetSystemInfoHandler()
    {
        return ResponseUtils.Ok(new JObject()
        {
            ["info"] = JToken.FromObject(await SystemInfo.Get(), Serializer)
        });
    }
}