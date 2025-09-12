using MCServerLauncher.Common.ProtoType.Files;

namespace MCServerLauncher.Daemon.Storage;

public interface IFileManager
{
    /// <summary>
    ///     请求上传文件:首先检查是否有同名文件正在上传,若没有,则预分配空间并添加后缀.tmp,返回file_id
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="size">文件总大小</param>
    /// <param name="timeout">上传回话超时</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <exception cref="IOException">非法路径、已存在正在上传的文件等</exception>
    /// <returns>分配的file_id</returns>
    public Guid FileUploadRequest(string? path, long size, TimeSpan? timeout, string? sha1 = null);

    /// <summary>
    ///  获取文件上传的字节数
    /// </summary>
    /// <param name="fileId">文件ID</param>
    /// <returns></returns>
    public long FileUploadProgress(Guid fileId);
    
    /// <summary>
    ///  获取文件信息
    /// </summary>
    /// <param name="path">路径,需要校验</param>
    /// <returns></returns>
    public FileData? GetFileInfo(string path);
    
    /// <summary>
    ///  获取目录信息
    /// </summary>
    /// <param name="path">路径,需要校验</param>
    /// <returns></returns>
    public DirectoryEntry? GetDirectoryInfo(string path);

    /// <summary>
    ///     客户端请求下载服务端的文件
    /// </summary>
    /// <param name="path">本地文件路径</param>
    /// <param name="timeout">FileID过期时间</param>
    /// <returns></returns>
    /// <exception cref="IOException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public Task<DownloadRequestInfo?> FileDownloadRequest(string path, TimeSpan? timeout);
}