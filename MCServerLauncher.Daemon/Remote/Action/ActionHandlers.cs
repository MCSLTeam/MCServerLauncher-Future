using MCServerLauncher.Daemon.FileManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Action
{
    internal class ActionHandlers
    {
        private readonly Logger _log;
        private readonly WebSocketContext _ctx;

        public ActionHandlers(WebSocketContext ctx, Logger logger)
        {
            _log = logger;
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
                    ActionType.FileUploadChunk => await FileUploadChunk(
                        Deserialize<ActionRequestTemplate.FileUploadChunk>(data)),
                    ActionType.FileUploadRequest => FileUploadRequest(
                        Deserialize<ActionRequestTemplate.FileUploadRequest>(data)),
                    ActionType.Message => Message(Deserialize<ActionRequestTemplate.Empty>(data)),
                    ActionType.Ping => Ping(Deserialize<ActionRequestTemplate.Empty>(data)),
                    ActionType.NewToken => NewToken(Deserialize<ActionRequestTemplate.NewToken>(data)),

                    _ => throw new NotImplementedException()
                };
            }
            catch (Exception e)
            {
                return Error(e.Message);
            }
        }

        private async Task<Dictionary<string, object>> FileUploadChunk(ActionRequestTemplate.FileUploadChunk data)
        {
            if (data.FileId == Guid.Empty) return Error("Invalid file id", ActionType.FileUploadChunk);

            var (done, received) = await FileManager.FileUploadChunk(data.FileId,data.Offset ,data.Data);
            return Ok(new Dictionary<string, object>
            {
                { "done", done },
                { "received", received }
            });
        }

        private Dictionary<string, object> NewToken(ActionRequestTemplate.NewToken data)
        {
            // TODO 实现data.Permission
            return data.Type switch
            {
                TokenType.Temporary =>
                    ServerBehavior.Config.TryCreateTemporaryToken(data.Seconds, out var token, out var expired)
                        ? Ok(new Dictionary<string, object>
                        {
                            { "token", token },
                            { "expired", new DateTimeOffset(expired).ToUnixTimeSeconds() }
                        })
                        : Error("Failed to create temporary token", ActionType.NewToken, 1402),
                _ => Error($"Token Type {data.Type} is not implemented", ActionType.NewToken, 1403)
            };
        }

        private Dictionary<string, object> FileUploadRequest(ActionRequestTemplate.FileUploadRequest data)
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

        private Dictionary<string, object> Message(ActionRequestTemplate.Empty data)
        {
            return null;
        }

        private Dictionary<string, object> Ping(ActionRequestTemplate.Empty data)
        {
            return Ok(new Dictionary<string, object> { { "pong_time", DateTime.Now } });
        }

        private Dictionary<string, object> Error(string message, ActionType type, int code = 1400)
        {
            _log.Error($"Error while handling Action {type}: {message}");
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
        
        private Dictionary<string, object> Error(string message,int code = 1400)
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

        private static T Deserialize<T>(JObject data)
        {
            var settings = ActionRequestTemplate.GetJsonSerializerSettings();
            return JsonConvert.DeserializeObject<T>(data.ToString(), settings);
        }
    }
}