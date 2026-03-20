using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Downloader;
using MCServerLauncher.Common.ProtoType.Files;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Serilog;
using Timer = System.Timers.Timer;

namespace MCServerLauncher.Daemon.Storage;

public record struct DownloadRequestInfo(Guid Id, long Size, string Sha1);

internal static class FileManager
{
    public static readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "daemon");
    public static readonly string InstancesRoot = Path.Combine(Root, "instances");
    public static readonly string CacheRoot = Path.Combine(Root, "caches");
    public static readonly string UploadRoot = Path.Combine(CacheRoot, "uploads");
    public static readonly string DownloadRoot = Path.Combine(CacheRoot, "downloads");
    public static readonly string CoreRoot = Path.Combine(Root, "cores");
    public static readonly string LogRoot = Path.Combine(Root, "logs");
    public static readonly string ContainedRoot = Path.Combine(Root, "contained");

    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMilliseconds(120000);

    private static readonly ConcurrentDictionary<Guid, FileUploadInfo> UploadSessions = new();

    private static readonly ConcurrentDictionary<Guid, FileDownloadInfo> DownloadSessions = new();
    private static readonly ConcurrentDictionary<Guid, IDownload> Downloading = new();
    private static Timer? _sessionCleanerTimer;

    public static void StartFileSessionsWatcher()
    {
        if (_sessionCleanerTimer is not null) return;

        _sessionCleanerTimer = new Timer();
        _sessionCleanerTimer.Interval = 5000;
        _sessionCleanerTimer.Elapsed += (sender, args) =>
        {
            WatchFileSessions(UploadSessions);
            WatchFileSessions(DownloadSessions);
        };
        _sessionCleanerTimer.AutoReset = true;
        _sessionCleanerTimer.Start();
    }

    private static void WatchFileSessions<TSessionInfo>(ConcurrentDictionary<Guid, TSessionInfo> sessions)
        where TSessionInfo : FileSessionInfo
    {
        foreach (var sessionId in sessions.Keys) // avoid concurrent modification exception
        {
            if (!sessions.TryGetValue(sessionId, out var session)) continue;
            if (!session.Timeout) continue;

            if (!sessions.TryRemove(sessionId, out _)) continue;

            Log.Warning("[FileManager] File session {0} timeout", sessionId);
            session.Close();
        }
    }

    #region File Upload Service

    /// <summary>
    ///     请求上传文件:首先检查是否有同名文件正在上传,若没有,则预分配空间并添加后缀.tmp,返回file_id
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="size">文件总大小</param>
    /// <param name="timeout">上传回话超时</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <exception cref="IOException">非法路径、已存在正在上传的文件等</exception>
    /// <returns>分配的file_id</returns>
    public static Guid FileUploadRequest(string? path, long size, TimeSpan? timeout, string? sha1 = null)
    {
        timeout ??= SessionTimeout;

        // Validate path
        if (path is not null)
        {
            path = ResolveAndValidatePath(path);
        }
        else
        {
            // path is null means upload to upload root
            path = UploadRoot;
        }


        // check if file is in upload session
        if (UploadSessions.Values.Any(info => info.Path == path)) throw new IOException("File is uploading");

        // pre-allocate file in disk
        try
        {
            // ensure directory exists
            Directory.CreateDirectory(UploadRoot);
            var tmpFile = path + ".tmp";
            // delete file if exists
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
            if (File.Exists(path)) File.Delete(path);

            // set file share to None, declined any access. For example: downloading, download session and uploading session.
            // this operation can raise various exceptions, attention.
            FileStream fs = new(tmpFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength(size);
            fs.Seek(0, SeekOrigin.Begin);
            var guid = Guid.NewGuid();

            UploadSessions.TryAdd(guid, new FileUploadInfo(path, size, sha1, fs, timeout.Value));
            Log.Debug("[FileUploadChunk] Uploading file {0}", Path.GetFileName(path));
            return guid;
        }
        catch (Exception)
        {
            return Guid.Empty;
        }
    }

    /// <summary>
    ///     写入文件分片
    /// </summary>
    /// <param name="id">file_id</param>
    /// <param name="offset">分片文件偏移量</param>
    /// <param name="strData">分片文件的字符串形式的数据</param>
    /// <returns>范围值为done</returns>
    /// <exception cref="IOException">文件操作相关</exception>
    public static async Task<(bool, long)> FileUploadChunk(Guid id, long offset, string strData)
    {
        if (!UploadSessions.TryGetValue(id, out var info))
            throw new IOException("File not found or timeout");

        info.Touch(); // update last access time

        if (offset < 0L || offset >= info.Size) throw new IOException("Offset out of range");

        var data = Encoding.BigEndianUnicode.GetBytes(strData);

        info.File.Seek(offset, SeekOrigin.Begin);
        await info.File.WriteAsync(data); // 可能为奇数长度

        // 更新文件状态
        info.Remain.Reduce(offset, offset + data.Length);

        if (info.RemainLength > 0) return (false, info.Size - info.RemainLength); // partial done

        // file upload complete
        var sha1 = await FileSha1(info.File);
        info.File.Close();

        // rename tmp file to its origin name
        File.Move(info.Path + ".tmp", info.Path, true);

        if (!string.IsNullOrEmpty(sha1))
        {
            if (sha1 != info.Sha1)
            {
                Log.Warning("[FileUploadChunk] Uploaded file {0} SHA1 mismatch!", Path.GetFileName(info.Path));
                File.Delete(info.Path);
                throw new IOException("File SHA1 mismatch");
            }
        }
        else
        {
            Log.Debug("[FileUploadChunk] Uploaded file {0} did not provide expected sha1, skipping check",
                Path.GetFileName(info.Path));
        }

        UploadSessions.TryRemove(id, out _);
        Log.Debug("[FileUploadChunk] File {0} upload complete", Path.GetFileName(info.Path));
        return (true, info.Size - info.RemainLength);
    }

    /// <summary>
    ///     取消上传文件
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static bool FileUploadCancel(Guid id)
    {
        if (!UploadSessions.TryRemove(id, out var info)) return false;

        info.File.Close();
        // delete tmp file 
        if (File.Exists(info.Path + ".tmp"))
            File.Delete(info.Path + ".tmp");
        Log.Debug("[FileUploadChunk] File {0} upload canceled", Path.GetFileName(info.Path));
        return true;
    }

    #endregion

    #region File Download Service

    /// <summary>
    ///     客户端请求下载服务端的文件
    /// </summary>
    /// <param name="path"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    /// <exception cref="IOException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public static async Task<DownloadRequestInfo?> FileDownloadRequest(string path, TimeSpan? timeout)
    {
        timeout ??= SessionTimeout;

        // Validate path
        path = ResolveAndValidatePath(path);
        if (!File.Exists(path)) throw new FileNotFoundException();

        // balance the concurrency of file downloads (limited number of download sessions for a single file)
        if (DownloadSessions.Count(kv => kv.Value.Path == path) >= AppConfig.Get().FileDownloadSessions)
            // Max download sessions of file reached
            return null;

        // set file share to None, declined any access. For example: downloading & upload session.
        // this operation can raise various exceptions, attention.
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var size = fs.Length;
        var sha1 = await FileSha1(fs);
        var id = Guid.NewGuid();

        DownloadSessions.TryAdd(id, new FileDownloadInfo(size, sha1, fs, path, timeout.Value));
        return new DownloadRequestInfo(id, size, sha1);
    }

    /// <summary>
    ///     客户端使用服务端分配的文件下载句柄请求文件分片
    /// </summary>
    /// <param name="id"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static async Task<string> FileDownloadRange(Guid id, int from, int to)
    {
        if (!DownloadSessions.TryGetValue(id, out var info))
            throw new ArgumentException("Invalid download session id or timeout");
            
        // 修正范围，防止超出文件大小
        if (to >= info.Size)
        {
            to = (int)info.Size;
        }
        
        if (from < 0 || to < 0 || from > to) throw new ArgumentException("Invalid range");
        info.Touch();

        var size = to - from;
        if (size == 0) return string.Empty; // 处理空文件或零长度请求
        
        var buffer = new byte[size % 2 == 0 ? size : size + 1];

        info.File.Seek(from, SeekOrigin.Begin);
        _ = await info.File.ReadAsync(buffer);
        info.Remain.Reduce(from, to);


        return Encoding.BigEndianUnicode.GetString(buffer, 0, buffer.Length);
    }

    /// <summary>
    ///     客户端请求关闭文件下载会话
    /// </summary>
    /// <param name="id"></param>
    /// <exception cref="ArgumentException"></exception>
    public static void FileDownloadClose(Guid id)
    {
        if (!DownloadSessions.TryRemove(id, out var info)) throw new ArgumentException("Invalid download session id");
        info.File.Close();
        Log.Debug("[FileDownloadClose] Download session {0} closed", id);
        if (!info.Remain.Done())
            Log.Warning("[FileDownloadClose] Download session {0} not completed, delete file at {1}", id, info.Path);
    }

    #endregion

    #region File System Info

    /// <summary>
    ///     获取文件基本信息
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    /// <exception cref="IOException"></exception>
    public static FileMetadata GetFileInfo(string path)
    {
        // validate path
        path = ResolveAndValidatePath(path);

        var fileInfo = new FileInfo(path);
        // if (!fileInfo.Exists) throw new IOException("File not found");

        return new FileMetadata(fileInfo);
    }

    /// <summary>
    ///     获取目录信息
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static DirectoryEntry GetDirectoryInfo(string path)
    {
        // validate path
        var fullPath = ResolveAndValidatePath(path);

        return new DirectoryEntry(fullPath, Root);
    }

    /// <summary>
    ///     计算文件 SHA1, 计算完成后恢复文件指针
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private static async Task<string> FileSha1(FileStream fs, CancellationToken ct = default)
    {
        // using var
        using var sha1 = SHA1.Create();
        var ptr = fs.Position;
        var hashBytes = await sha1.ComputeHashAsync(fs, ct);
        fs.Seek(ptr, SeekOrigin.Begin);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    public static async Task<string> FileSha1(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await FileSha1(fs, ct);
    }

    #endregion


    #region Download From Url

    /// <summary>
    ///     下载文件到<see cref="DownloadRoot" />
    /// </summary>
    /// <param name="targetDir">目标目录</param>
    /// <param name="filename">文件名称</param>
    /// <param name="url">下载URL</param>
    /// <param name="download">下载实例,可以订阅下载事件(进度更新/下载完成)</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <param name="maxThreads">下载最大线程数</param>
    /// <returns>分配的file_id</returns>
    public static Guid DownloadFromUrl(string? targetDir, string filename, string url,
        out IDownload download,
        string? sha1 = null, int maxThreads = 16)
    {
        if (targetDir is not null)
        {
            targetDir = ResolveAndValidatePath(targetDir);
        }

        // 确保存在
        Directory.CreateDirectory(DownloadRoot);

        var filePath = Path.Combine(targetDir ?? DownloadRoot, filename);
        var tmpFilePath = filePath + ".tmp";

        if (File.Exists(tmpFilePath)) File.Delete(tmpFilePath);
        if (File.Exists(filePath)) File.Delete(filePath);

        var fileId = Guid.NewGuid();
        Log.Debug("[FileManager] Downloading file {0} from {1}", filename, url);


        download = DownloadBuilder.New()
            .WithUrl(url)
            .WithFileLocation(tmpFilePath)
            .WithConfiguration(new DownloadConfiguration
            {
                ChunkCount = maxThreads,
                ParallelDownload = true
            })
            .Build();

        download.DownloadFileCompleted += async (_, args) =>
        {
            if (args.Error is not null)
            {
                File.Delete(tmpFilePath);
                Log.Warning("[FileManager] Failed to download file {0}: {1}", filename, args.Error.Message);
            }
            else if (!string.IsNullOrEmpty(sha1))
            {
                await using var fs = new FileStream(tmpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var fileSha1 = await FileSha1(fs);
                if (sha1 != fileSha1)
                {
                    Log.Warning("[FileManager] Downloaded file {0} SHA-1 mismatch!", filename);
                    File.Delete(tmpFilePath);
                    return;
                }
            }
            else
            {
                Log.Debug("[FileManager] Downloaded file {0} did not provide expected SHA-1, skipping check",
                    filename);
            }

            File.Move(tmpFilePath, filePath, true);

            // remove
            Downloading.TryRemove(fileId, out var _);
        };


        download.StartAsync();

        Downloading.TryAdd(fileId, download);

        return fileId;
    }

    /// <summary>
    ///     根据file_id获取文件路径，不存在为null
    /// </summary>
    /// <param name="downloadingFileId">file_id</param>
    /// <param name="download"></param>
    /// <returns>文件路径</returns>
    public static bool TryGetDownloading(Guid downloadingFileId, out IDownload? download)
    {
        return Downloading.TryGetValue(downloadingFileId, out download);
    }

    #endregion

    #region Validate Path

    /// <summary>
    ///     解析并验证路径。将客户端提供的路径（相对于 root）解析为绝对路径，并验证其是否在 root 范围内。
    /// </summary>
    /// <param name="path">客户端提供的路径，例如 "/instances/xxx"</param>
    /// <param name="root">根目录，默认为 Daemon Root</param>
    /// <returns>解析后的绝对路径</returns>
    /// <exception cref="IOException">如果路径无效或超出 root 范围</exception>
    public static string ResolveAndValidatePath(string path, string? root = null)
    {
        root ??= Root;

        // 统一将路径分隔符转换为当前系统的分隔符
        path = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        // 如果客户端传入的是绝对路径（例如 C:\xxx），且以 root 开头，则直接验证
        // 注意：在 Windows 上，"/xxx" 会被 IsPathRooted 识别为 true，但它实际上是相对于当前盘符的路径。
        // 我们需要区分真正的绝对路径（如 C:\xxx）和虚拟的绝对路径（如 /xxx）。
        if (Path.IsPathRooted(path) && path.Length >= 2 && path[1] == Path.VolumeSeparatorChar)
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root);
            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }
            throw new IOException("Invalid path: out of daemon root");
        }

        // 移除路径开头的斜杠，防止 Path.Combine 将其视为根目录
        var relativePath = path.TrimStart(Path.DirectorySeparatorChar);

        // 将相对路径与 root 结合
        var combinedPath = Path.Combine(root, relativePath);
        var resolvedPath = Path.GetFullPath(combinedPath);
        var resolvedRoot = Path.GetFullPath(root);

        // 验证解析后的路径是否在 root 范围内
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Invalid path: out of daemon root");
        }

        return resolvedPath;
    }

    #endregion

    #region IO Operation

    /// <summary>
    ///     读取文件。
    /// </summary>
    /// <param name="path">需要读取的文件路径</param>
    /// <returns></returns>
    private static string ReadText(string path)
    {
        var text = File.ReadAllText(path);
        return text;
    }

    /// <summary>
    ///     读取json。可能会抛出IO异常和Json异常
    /// </summary>
    /// <param name="path">需要读取的json文件路径</param>
    /// <typeparam name="T">json需转换为的类型</typeparam>
    /// <returns></returns>
    public static T? ReadJson<T>(string path)
    {
        return JsonConvert.DeserializeObject<T>(ReadText(path), DaemonJsonSettings.Settings);
    }

    /// <summary>
    ///     读取json。如果文件不存在，则调用defaultFactory, 并写入文件
    /// </summary>
    /// <param name="path">需要读取的json文件路径</param>
    /// <param name="defaultFactory">默认配置项</param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T ReadJsonOr<T>(string path, Func<T> defaultFactory)
    {
        try
        {
            return ReadJson<T>(path)!;
        }
        catch (FileNotFoundException)
        {
            var invoke = defaultFactory.Invoke();
            File.WriteAllText(path, JsonConvert.SerializeObject(
                invoke,
                typeof(T),
                Formatting.Indented,
                DaemonJsonSettings.Settings
            ));
            return invoke;
        }
    }

    private static void BackupAndWriteText(string path, string text, Func<string, bool>? validator = null)
    {
        if (File.Exists(path) && (validator?.Invoke(File.ReadAllText(path)) ?? true))
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        }
        else
        {
            Log.Warning("[FileManager] File({0})'s content is not valid, skip backup", path);
        }

        File.WriteAllText(path, text);
    }

    /// <summary>
    ///     写入json,并备份。通常是配置文件等json schema固定的json文件
    /// </summary>
    /// <param name="path"></param>
    /// <param name="obj"></param>
    /// <typeparam name="T"></typeparam>
    public static void WriteJsonAndBackup<T>(string path, T obj)
    {
        BackupAndWriteText(
            path,
            JsonConvert.SerializeObject(
                obj,
                typeof(T),
                Formatting.Indented,
                DaemonJsonSettings.Settings
            ),
            content =>
            {
                try
                {
                    JsonConvert.DeserializeObject<T>(content, DaemonJsonSettings.Settings);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        );
    }

    public static bool TryRemove(string path, bool recursive = true)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive);
            else
                File.Delete(path);

            return true;
        }
        catch (Exception e)
        {
            Log.Error("[FileManager] Failed to remove file {0}: {1}", path, e);
            return false;
        }
    }

    public static void DeleteFile(string path)
    {
        path = ResolveAndValidatePath(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else
        {
            throw new FileNotFoundException("File not found", path);
        }
    }

    public static void DeleteDirectory(string path, bool recursive)
    {
        path = ResolveAndValidatePath(path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
        else
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    public static void RenameFile(string path, string newName)
    {
        path = ResolveAndValidatePath(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is null) throw new IOException("Invalid path");
        
        var newPath = Path.Combine(directory, newName);
        newPath = ResolveAndValidatePath(newPath);

        if (File.Exists(path))
        {
            File.Move(path, newPath);
        }
        else
        {
            throw new FileNotFoundException("File not found", path);
        }
    }

    public static void RenameDirectory(string path, string newName)
    {
        path = ResolveAndValidatePath(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is null) throw new IOException("Invalid path");
        
        var newPath = Path.Combine(directory, newName);
        newPath = ResolveAndValidatePath(newPath);

        if (Directory.Exists(path))
        {
            Directory.Move(path, newPath);
        }
        else
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    public static void CreateDirectory(string path)
    {
        path = ResolveAndValidatePath(path);
        Directory.CreateDirectory(path);
    }

    public static void MoveFile(string sourcePath, string destinationPath)
    {
        sourcePath = ResolveAndValidatePath(sourcePath);
        destinationPath = ResolveAndValidatePath(destinationPath);

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath);
        }
        else
        {
            throw new FileNotFoundException("Source file not found", sourcePath);
        }
    }

    public static void MoveDirectory(string sourcePath, string destinationPath)
    {
        sourcePath = ResolveAndValidatePath(sourcePath);
        destinationPath = ResolveAndValidatePath(destinationPath);

        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
        }
    }

    public static void CopyFile(string sourcePath, string destinationPath)
    {
        sourcePath = ResolveAndValidatePath(sourcePath);
        destinationPath = ResolveAndValidatePath(destinationPath);

        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, true);
        }
        else
        {
            throw new FileNotFoundException("Source file not found", sourcePath);
        }
    }

    public static void CopyDirectory(string sourcePath, string destinationPath)
    {
        sourcePath = ResolveAndValidatePath(sourcePath);
        destinationPath = ResolveAndValidatePath(destinationPath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");
        }

        var dir = new DirectoryInfo(sourcePath);
        var dirs = dir.GetDirectories();

        Directory.CreateDirectory(destinationPath);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationPath, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dirs)
        {
            var newDestinationDir = Path.Combine(destinationPath, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    #endregion
}
