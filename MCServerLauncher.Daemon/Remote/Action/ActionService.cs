using System.Text.RegularExpressions;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
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
    private readonly IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> _javaScannerCache;

    // DI
    private readonly IWebJsonConverter _webJsonConverter;

    // DI constructor
    public ActionService(IAsyncTimedCacheable<List<JavaScanner.JavaInfo>> javaScannerCache,
        IWebJsonConverter webJsonConverter)
    {
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
        JObject? data
    )
    {
        try
        {
            return type switch
            {
                ActionType.FileUploadChunk => await FileUploadChunkHandler(Actions.FileUploadChunk.RequestOf(data)),
                ActionType.FileUploadRequest => FileUploadRequestHandler(Actions.FileUploadRequest.RequestOf(data)),
                ActionType.Ping => PingHandler(Actions.Empty.RequestOf()),
                ActionType.FileUploadCancel => FileUploadCancelHandler(Actions.FileUploadCancel.RequestOf(data)),
                ActionType.GetJavaList => await GetJavaListHandler(Actions.Empty.RequestOf()),
                ActionType.FileDownloadRequest => await FileDownloadRequestHandler(
                    Actions.FileDownloadRequest.RequestOf(data)),
                ActionType.FileDownloadClose => FileDownloadCloseHandler(Actions.FileDownloadClose.RequestOf(data)),
                ActionType.FileDownloadRange => await FileDownloadRangeHandler(
                    Actions.FileDownloadRange.RequestOf(data)),
                ActionType.GetDirectoryInfo => GetDirectoryInfoHandler(Actions.GetDirectoryInfo.RequestOf(data)),
                ActionType.GetFileInfo => GetFileInfoHandler(Actions.GetFileInfo.RequestOf(data)),
                _ => throw new NotImplementedException()
            };
        }
        catch (Exception e)
        {
            return Err(e.Message);
        }
    }

    public Dictionary<string, object> Err(string? message, int code = 1400)
    {
        return new Dictionary<string, object>
        {
            { "status", "error" },
            { "retcode", code },
            {
                "data", new Dictionary<string, string?>
                {
                    { "error_message", message }
                }
            }
        };
    }

    public Dictionary<string, object> Ok(Actions.IActionResponse? data = null)
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

    private Dictionary<string, object> PingHandler(Actions.Empty.Request data)
    {
        return Ok(Actions.Ping.ResponseOf(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()));
    }

    private Dictionary<string, object> FileUploadCancelHandler(Actions.FileUploadCancel.Request data)
    {
        return FileManager.FileUploadCancel(data.FileId)
            ? Ok()
            : Err("Failed to cancel file upload", ActionType.FileUploadCancel, 1402);
    }

    private Dictionary<string, object> FileDownloadCloseHandler(Actions.FileDownloadClose.Request data)
    {
        FileManager.FileDownloadClose(data.FileId);
        return Ok(Actions.FileDownloadClose.ResponseOf());
    }

    private async Task<Dictionary<string, object>> FileDownloadRangeHandler(Actions.FileDownloadRange.Request data)
    {
        var match = RangePattern.Match(data.Range);
        if (!match.Success) return Err("Invalid range format", ActionType.FileDownloadRange);

        var (from, to) = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        return Ok(Actions.FileDownloadRange.ResponseOf(await FileManager.FileDownloadRange(data.FileId, from, to)));
    }

    private Dictionary<string, object> GetDirectoryInfoHandler(Actions.GetDirectoryInfo.Request data)
    {
        var dirEntry = FileManager.GetDirectoryInfo(data.Path);
        return Ok(Actions.GetDirectoryInfo.ResponseOf(dirEntry.Parent, dirEntry.Files, dirEntry.Directories));
    }

    private Dictionary<string, object> GetFileInfoHandler(Actions.GetFileInfo.Request data)
    {
        return Ok(Actions.GetFileInfo.ResponseOf(FileManager.GetFileInfo(data.Path)));
    }

    private async Task<Dictionary<string, object>> FileDownloadRequestHandler(Actions.FileDownloadRequest.Request data)
    {
        var info = await FileManager.FileDownloadRequest(data.Path);
        return Ok(Actions.FileDownloadRequest.ResponseOf(info.Id, info.Size, info.Sha1));
    }

    private Dictionary<string, object> Err(string message, ActionType type, int code = 1400)
    {
        Log.Error("Error while handling Action {0}: {1}", type, message);
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