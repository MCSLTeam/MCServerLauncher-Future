using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Cache;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action(rpc)处理器。
///     Action是ws交互格式的一种，他代表的是一个远程过程调用: C->S,处理后返回json数据给C
/// </summary>
internal class ActionService : IActionService
{
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;

    // DI
    private readonly ILogHelper _logger;
    private readonly IWebJsonConverter _webJsonConverter;

    // DI constructor
    public ActionService(RemoteLogHelper logger, IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IWebJsonConverter webJsonConverter)
    {
        _logger = logger;
        _javaScannerCache = javaScannerCache;
        _webJsonConverter = webJsonConverter;
    }

    /// <summary>
    ///     Action(rpc)处理中枢
    /// </summary>
    /// <param name="type">Action类型</param>
    /// <param name="data">Action数据</param>
    /// <returns>Action响应</returns>
    /// <exception cref="NotImplementedException">未实现的Action</exception>
    public async Task<Dictionary<string, object>> Routine(
        ActionType type,
        JObject data
    )
    {
        try
        {
            return type switch
            {
                ActionType.FileUploadChunk => await FileUploadChunkHandler(Actions.FileUploadChunk.RequestOf(data)),
                ActionType.FileUploadRequest => FileUploadRequestHandler(Actions.FileUploadRequest.RequestOf(data)),
                ActionType.Ping => HeartBeatHandler(Actions.Empty.RequestOf()),
                ActionType.FileUploadCancel => FileUploadCancelHandler(Actions.FileUploadCancel.RequestOf(data)),
                ActionType.GetJavaList => await GetJavaListHandler(Actions.Empty.RequestOf()),
                _ => throw new NotImplementedException()
            };
        }
        catch (Exception e)
        {
            return Err(e.Message);
        }
    }

    public Dictionary<string, object> Err(string message, int code = 1400)
    {
        return new Dictionary<string, object>
        {
            { "status", "error" },
            { "retcode", code },
            {
                "data", new Dictionary<string, string>
                {
                    { "error_message", message }
                }
            }
        };
    }

    public Dictionary<string, object> Ok(Actions.ActionResponse data = null)
    {
        return new Dictionary<string, object>
        {
            { "status", "ok" },
            { "retcode", 0 },
            { "data", data?.Into(_webJsonConverter.getSerializer()) ?? new JObject() }
        };
    }

    private async Task<Dictionary<string, object>> FileUploadChunkHandler(Actions.FileUploadChunk.Request data)
    {
        if (data.FileId == Guid.Empty) return Err("Invalid file id", ActionType.FileUploadChunk);

        var (done, received) = await FileManager.FileUploadChunk(data.FileId, data.Offset, data.Data);
        return Ok(Actions.FileUploadChunk.ResponseOf(done, received));
    }

    private async Task<Dictionary<string, object>> GetJavaListHandler(Actions.Empty.Request data)
    {
        return Ok(Actions.GetJavaList.ResponseOf(await _javaScannerCache.Value));
    }

    private Dictionary<string, object> FileUploadRequestHandler(Actions.FileUploadRequest.Request data)
    {
        var fileId = FileManager.FileUploadRequest(
            data.Path,
            data.Size,
            data.ChunkSize,
            data.Sha1
        );

        return fileId == Guid.Empty
            ? Err("Failed to pre-allocate space", ActionType.FileUploadRequest, 1401)
            : Ok(Actions.FileUploadRequest.ResponseOf(fileId));
    }

    private Dictionary<string, object> HeartBeatHandler(Actions.Empty.Request data)
    {
        return Ok(Actions.HeartBeat.ResponseOf(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()));
    }

    private Dictionary<string, object> FileUploadCancelHandler(Actions.FileUploadCancel.Request data)
    {
        return FileManager.FileUploadCancel(data.FileId)
            ? Ok()
            : Err("Failed to cancel file upload", ActionType.FileUploadCancel, 1402);
    }

    private Dictionary<string, object> Err(string message, ActionType type, int code = 1400)
    {
        _logger.Error($"Error while handling Action {type}: {message}");
        return new Dictionary<string, object>
        {
            { "status", "error" },
            { "retcode", code },
            {
                "data", new Dictionary<string, string>
                {
                    { "error_message", message }
                }
            }
        };
    }
}