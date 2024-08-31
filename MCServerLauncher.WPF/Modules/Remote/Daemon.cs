using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UploadFileToken = System.String;

namespace MCServerLauncher.WPF.Modules.Remote
{
    public class Daemon : IDaemon
    {
        private SynchronizationContext _context = SynchronizationContext.Current;

        private ClientConnection Connection { get; set; }

        public bool IsClosed { get; private set; }
        public DateTime LastPing { get; } = DateTime.Now;

        public async Task<string> UploadFileRequestAsync(string path, string dst, int chunkSize)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha1 = await Utils.FileSha1(fs);

            var size = new FileInfo(path).Length;

            return (await RequestAsync(ActionType.FileUploadRequest, new Dictionary<string, object>
            {
                { "path", dst },
                { "sha1", sha1 },
                { "chunk_size", chunkSize },
                { "size", size }
            }))["file_id"]!.ToString();
        }

        public async Task<(bool, long)> UploadFileChunkAsync(UploadFileToken token, long offset, byte[] data, int bytes)
        {
            var strData = bytes % 2 != 0
                ? Encoding.BigEndianUnicode.GetString(data, 0, bytes + 1) // 末尾补0x00
                : Encoding.BigEndianUnicode.GetString(data, 0, bytes);
            var rv = await RequestAsync(ActionType.FileUploadChunk, new Dictionary<string, object>
            {
                { "file_id", token },
                { "offset", offset },
                { "data", strData }
            });
            return (rv["done"]!.ToObject<bool>(), rv["received"]!.ToObject<long>());
        }

        public async Task UploadFileCancelAsync(UploadFileToken token)
        {
            await RequestAsync(ActionType.FileUploadCancel, new Dictionary<string, object>
            {
                { "file_id", token }
            });
        }

        public async Task<List<JavaInfo>> GetJavaListAsync()
        {
            return (await RequestAsync(ActionType.GetJavaList, new Dictionary<string, object>()))
                .ToObject<List<JavaInfo>>();
        }

        public async Task CloseAsync()
        {
            IsClosed = true;
            await Connection.CloseAsync();
        }

        public async Task<IDaemon> OpenAsync(string address, int port, string token, ClientConnectionConfig cfg)
        {
            var connection = await ClientConnection.OpenAsync(address, port, token, cfg);
            return new Daemon
            {
                Connection = connection
            };
        }

        private async Task<JObject> RequestAsync(ActionType actionType, Dictionary<string, object> args,
            string echo = null)
        {
            return await Connection.RequestAsync(actionType, args, echo);
        }
    }
}