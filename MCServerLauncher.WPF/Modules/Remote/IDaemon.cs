using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote
{
    public struct JavaInfo
    {
        public string Path { get; set; }
        public string Version { get; set; }
        public string Architecture { get; set; }
    }

    /// <summary>
    ///     Daemon Rpc Interface
    /// </summary>
    public interface IDaemon
    {
        bool IsClosed { get; }
        bool PingLost { get; }
        DateTime LastPing { get; }
        ClientConnection Connection { get; }

        Task<UploadContext> UploadFileAsync(string path, string dst, int chunkSize);

        // Task<(bool, long)> UploadFileChunkAsync(UploadFileToken token, long offset, byte[] data, int bytes);
        Task UploadFileCancelAsync(UploadContext context);
        Task<List<JavaInfo>> GetJavaListAsync();
        Task CloseAsync();
    }
}