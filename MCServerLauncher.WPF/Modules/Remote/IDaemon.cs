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
    ///   Daemon Rpc Interface
    /// </summary>
    public interface IDaemon
    {
        Task<UploadFileToken> UploadFileOpenAsync(string path, int chunkSize);
        Task<(bool,long)> UploadFileWriteAsync(UploadFileToken token,long offset ,byte[] data);
        Task UploadFileCancelAsync(UploadFileToken token);
        
        Task<List<JavaInfo>> GetJavaListAsync();
    }
}