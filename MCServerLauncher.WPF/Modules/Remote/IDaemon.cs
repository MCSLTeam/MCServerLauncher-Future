using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote
{
    using UploadFileToken = String;

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
        DateTime LastPing { get; }
        Task<UploadFileToken> UploadFileRequestAsync(string path, string dst, int chunkSize);
        Task<(bool, long)> UploadFileChunkAsync(UploadFileToken token, long offset, byte[] data, int bytes);
        Task UploadFileCancelAsync(UploadFileToken token);
        Task<List<JavaInfo>> GetJavaListAsync();
        Task CloseAsync();
    }
}