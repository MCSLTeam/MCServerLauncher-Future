using MCServerLauncher.Daemon.FileManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Action
{
    internal class ActionHandlers
    {
        private readonly WebSocketContext _ctx;

        public ActionHandlers(WebSocketContext ctx)
        {
            _ctx = ctx;
        }

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
                    ActionType.Message => MessageHandler(Actions.Empty.RequestOf(data)),
                    ActionType.Ping => PingHandler(Actions.Empty.RequestOf(data)),
                    _ => throw new NotImplementedException()
                };
            }
            catch (Exception e)
            {
                return Error(e.Message);
            }
        }

        private async Task<Dictionary<string, object>> FileUploadChunkHandler(Actions.FileUploadChunk.Request data)
        {
            if (data.FileId == Guid.Empty) return Error("Invalid file id", ActionType.FileUploadChunk);

            var (done, received) = await FileManager.FileUploadChunk(data.FileId, data.Offset, data.Data);
            return Ok(new Dictionary<string, object>
            {
                { "done", done },
                { "received", received }
            });
        }

        private Dictionary<string, object> FileUploadRequestHandler(Actions.FileUploadRequest.Request data)
        {
            var fileId = FileManager.FileUploadRequest(
                data.Path,
                data.Size,
                data.ChunkSize,
                data.Sha1
            );

            return (fileId == Guid.Empty)
                ? Error("Failed to pre-allocate space", ActionType.FileUploadRequest, 1401)
                : Ok(new Dictionary<string, object> { { "file_id", fileId } });
        }

        private Dictionary<string, object> MessageHandler(Actions.Empty.Request data)
        {
            return null;
        }

        private Dictionary<string, object> PingHandler(Actions.Empty.Request data)
        {
            return Ok(new Dictionary<string, object> { { "pong_time", DateTime.Now } });
        }

        private Dictionary<string, object> Error(string message, ActionType type, int code = 1400)
        {
            LogHelper.Error($"Error while handling Action {type}: {message}");
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

        private Dictionary<string, object> Error(string message, int code = 1400)
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

        private Dictionary<string, object> Ok(Dictionary<string, object> data)
        {
            return new Dictionary<string, object>
            {
                { "status", "ok" },
                { "retcode", 0 },
                { "data", data }
            };
        }
    }
}