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
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings.Settings);
    private readonly IEventService _eventService;
    private readonly IInstanceManager _instanceManager;
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;

    public ActionHandler(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IInstanceManager instanceManager, IEventService eventService)
    {
        _javaScannerCache = javaScannerCache;
        _instanceManager = instanceManager;
        _eventService = eventService;
    }

    private async Task<JObject> FileUploadChunkHandler(Guid fileId, long offset, string data)
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

    private async Task<JObject> GetJavaListHandler()
    {
        return new JObject
        {
            ["java_list"] = JToken.FromObject(await _javaScannerCache.Value, Serializer)
        };
    }

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

    private JObject PingHandler()
    {
        return new JObject
        {
            ["time"] = JToken.FromObject(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(), Serializer)
        };
    }

    private JObject FileUploadCancelHandler(Guid fileId)
    {
        return FileManager.FileUploadCancel(fileId)
            ? new JObject()
            : throw new ActionExecutionException(1402, "Failed to cancel file upload");
    }

    private JObject FileDownloadCloseHandler(Guid fileId)
    {
        FileManager.FileDownloadClose(fileId);
        return new JObject();
    }

    private async Task<JObject> FileDownloadRangeHandler(Guid fileId, string range)
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

    private JObject GetFileInfoHandler(string path)
    {
        return new JObject
        {
            ["meta"] = JToken.FromObject(FileManager.GetFileInfo(path), Serializer)
        };
    }

    private async Task<JObject> FileDownloadRequestHandler(string path, long? timeout)
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

    private JObject TryStartInstanceHandler(Guid id)
    {
        var rv = _instanceManager.TryStartInstance(id, out var instance);
        if (instance == null) throw new ActionExecutionException(1400, "Instance not found");

        var logPrefix = $"{instance.Config.Name}({id})";

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

        return new JObject
        {
            ["done"] = JToken.FromObject(rv, Serializer)
        };
    }

    private JObject TryStopInstanceHandler(Guid id)
    {
        return new JObject
        {
            ["done"] = JToken.FromObject(_instanceManager.TryStopInstance(id), Serializer)
        };
    }

    private JObject SendToInstanceHandler(Guid id, string message)
    {
        _instanceManager.SendToInstance(id, message);
        return new JObject();
    }

    private JObject GetAllStatusHandler()
    {
        return new JObject
        {
            ["status"] = JToken.FromObject(_instanceManager.GetAllStatus(), Serializer)
        };
    }

    private async Task<JObject> GetSystemInfoHandler()
    {
        return new JObject
        {
            ["info"] = JToken.FromObject(await SystemInfo.Get(), Serializer)
        };
    }
}