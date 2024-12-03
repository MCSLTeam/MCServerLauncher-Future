using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote;

public struct JavaInfo
{
    public string Path { get; set; }
    public string Version { get; set; }
    public string Architecture { get; set; }

    public override string ToString()
    {
        return $"Java({Version} - {Architecture}) @ {Path}";
    }
}

/// <summary>
///     Daemon Rpc Interface
/// </summary>
public interface IDaemon
{
    bool IsClosed { get; }
    bool PingLost { get; }
    DateTime LastPing { get; }
    ClientConnection? Connection { get; }

    /// <summary>
    ///     rpc: 上传文件
    /// </summary>
    /// <param name="path">目标上传文件的本地路径</param>
    /// <param name="dst">位于服务器上的上传目标路径</param>
    /// <param name="chunkSize">传输分块大小</param>
    /// <returns>上传上下文,用于查看进度,剩余时间,上传状态和取消上传等</returns>
    Task<UploadContext> UploadFileAsync(string path, string dst, int chunkSize);

    /// <summary>
    ///     rpc: 取消上传
    /// </summary>
    /// <param name="context">要取消的上传上下文</param>
    /// <returns></returns>
    Task UploadFileCancelAsync(UploadContext context);

    /// <summary>
    ///     rpc: 获取Daemon宿主机上的Java列表
    /// </summary>
    /// <returns></returns>
    Task<List<JavaInfo>> GetJavaListAsync();

    Task<JObject> GetSystemInfoAsync();

    /// <summary>
    ///     关闭连接
    /// </summary>
    /// <returns></returns>
    Task CloseAsync();

    Task<bool> PingAsync();
}