using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Downloader;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon.Storage;

public struct DownloadRequestInfo
{
    public Guid Id;
    public long Size;
    public string Sha1;

    public DownloadRequestInfo(Guid id, long size, string sha1)
    {
        Id = id;
        Size = size;
        Sha1 = sha1;
    }
}

public record FileSystemMetadata
{
    public DateTime CreationTime;
    public bool Hidden;
    public DateTime LastAccessTime;
    public DateTime LastWriteTime;
    public string? LinkTarget;

    protected FileSystemMetadata(FileSystemInfo info)
    {
        CreationTime = info.CreationTime;
        LastAccessTime = info.LastAccessTime;
        LastWriteTime = info.LastWriteTime;
        LinkTarget = info.LinkTarget;
        Hidden = info.Attributes.HasFlag(FileAttributes.Hidden);
    }
}

public record FileMetadata : FileSystemMetadata
{
    public bool ReadOnly;
    public long Size;


    public FileMetadata(FileInfo info) : base(info)
    {
        Size = info.Length;
        ReadOnly = info.IsReadOnly;
    }
}

public record DirectoryMetadata : FileSystemMetadata
{
    public DirectoryMetadata(DirectoryInfo info) : base(info)
    {
    }
}

public record DirectoryEntry
{
    public DirectoryInformation[] Directories;
    public FileInformation[] Files;
    public string? Parent;

    public DirectoryEntry(string path) : this(new DirectoryInfo(path))
    {
    }

    private DirectoryEntry(DirectoryInfo info)
    {
        Files = info.GetFiles().Select(x => new FileInformation(x)).ToArray();
        Directories = info.GetDirectories().Select(x => new DirectoryInformation(x)).ToArray();
        Parent = Path.GetRelativePath(FileManager.Root, info.FullName);
    }

    public record FileInformation
    {
        public FileMetadata Meta;
        public string Name;

        public FileInformation(FileInfo info)
        {
            Name = info.Name;
            Meta = new FileMetadata(info);
        }
    }

    public record DirectoryInformation
    {
        public DirectoryMetadata Meta;
        public string Name;

        public DirectoryInformation(DirectoryInfo info)
        {
            Name = info.Name;
            Meta = new DirectoryMetadata(info);
        }
    }
}

internal static class FileManager
{
    public const string Root = "daemon";
    public static readonly string InstancesRoot = Path.Combine(Root, "instances");
    public static readonly string CacheRoot = Path.Combine(Root, "caches");
    public static readonly string UploadRoot = Path.Combine(CacheRoot, "uploads");
    public static readonly string DownloadRoot = Path.Combine(CacheRoot, "downloads");
    public static readonly string CoreRoot = Path.Combine(Root, "cores");
    public static readonly string LogRoot = Path.Combine(Root, "logs");

    private static readonly ConcurrentDictionary<Guid, FileUploadInfo> UploadSessions = new();

    private static readonly ConcurrentDictionary<Guid, FileDownloadInfo> DownloadSessions = new();
    private static readonly ConcurrentDictionary<Guid, IDownload> Downloading = new();

    #region File Upload Service

    /// <summary>
    ///     请求上传文件:首先检查是否有同名文件正在上传,若没有,则预分配空间并添加后缀.tmp,返回file_id
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="size">文件总大小</param>
    /// <param name="chunkSize">文件分片传输大小</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <exception cref="IOException">非法路径、已存在正在上传的文件等</exception>
    /// <returns>分配的file_id</returns>
    public static Guid FileUploadRequest(string? path, long size, long chunkSize, string? sha1 = null)
    {
        // Validate path
        if (path != null && !ValidatePath(path)) throw new IOException("Invalid path: out of daemon root");

        // path is null means upload to upload root
        path ??= UploadRoot;

        // 由于FileStream.WriteAsync只支持int,故提前检查,若大于2G,则返回空
        if ((int)size != size || (int)chunkSize != chunkSize || size < 0 || chunkSize < 0) return Guid.Empty;

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

            UploadSessions.TryAdd(guid, new FileUploadInfo(path, size, chunkSize, sha1, fs));
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
            throw new IOException("File not found");

        if (offset < 0L || offset >= info.Size) throw new IOException("Offset out of range");

        var data = Encoding.BigEndianUnicode.GetBytes(strData);
        int remain;
        var count = (remain = (int)(info.Size - offset)) < info.ChunkSize ? remain : (int)info.ChunkSize;

        info.File.Seek(offset, SeekOrigin.Begin);
        info.File.Write(
            data,
            0,
            count
        ); // 可能为奇数长度

        // 更新文件状态
        info.RemainLength -= count;
        info.Remain.Reduce(offset, offset + count);

        if (info.RemainLength > 0)
            // partial done
            return (false, info.Size - info.RemainLength);

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
    /// <returns></returns>
    /// <exception cref="IOException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task<DownloadRequestInfo> FileDownloadRequest(string path)
    {
        // Validate path
        if (!ValidatePath(path)) throw new IOException("Invalid path: out of daemon root");
        if (!File.Exists(path)) throw new IOException("File not found");

        // do not check if file in download session
        // balance the concurrency of file downloads (limited number of download sessions for a single file)
        if (DownloadSessions.Count(kv => kv.Value.Path == path) >= AppConfig.Get().SingleFileMaxDownloadSessions)
            throw new InvalidOperationException($"Max download sessions of file '{path}' reached");

        // set file share to None, declined any access. For example: downloading & upload session.
        // this operation can raise various exceptions, attention.
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var size = fs.Length;
        var sha1 = await FileSha1(fs);
        return new DownloadRequestInfo(Guid.NewGuid(), size, sha1);
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
        if (!DownloadSessions.TryGetValue(id, out var info)) throw new ArgumentException("Invalid download session id");
        if (from < 0 || to < 0 || from > to || to >= info.Size) throw new ArgumentException("Invalid range");

        var size = to - from;
        var buffer = new byte[size % 2 == 0 ? size : size + 1];

        info.File.Seek(from, SeekOrigin.Begin);
        var _ = await info.File.ReadAsync(buffer, 0, size);
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
        if (!ValidatePath(path)) throw new IOException("Invalid path: out of daemon root");

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists) throw new IOException("File not found");

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
        if (!ValidatePath(path)) throw new IOException("Invalid path: out of daemon root");
        
        return new DirectoryEntry(path);
    }

    /// <summary>
    ///     计算文件 SHA1, 计算完成后恢复文件指针
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="bufferSize"></param>
    /// <returns></returns>
    private static Task<string> FileSha1(FileStream fs, uint bufferSize = 16384)
    {
        return Task.Run(() =>
        {
            // using var
            using var sha1 = SHA1.Create();

            var ptr = fs.Position;
            fs.Seek(0, SeekOrigin.Begin);

            var buffer = new byte[bufferSize];
            int bytesRead;
            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);

            sha1.TransformFinalBlock(buffer, 0, 0);

            var hashBytes = sha1.Hash!;

            fs.Seek(ptr, SeekOrigin.Begin);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        });
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
        if (targetDir != null && ValidatePath(targetDir)) throw new IOException("Invalid path: out of daemon root");

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
            if (args.Error != null)
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
    ///     检查path是否在root范围下,若root在./之外,也返回false (不访问磁盘)
    /// </summary>
    /// <param name="path"></param>
    /// <param name="root"></param>
    /// <returns></returns>
    public static bool ValidatePath(string path, string root = Root)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root);

        return !normalizedPath.StartsWith(".") && normalizedPath.StartsWith(normalizedRoot);
    }

    /// <summary>
    ///     从算法层面，将包含..和.的相对路径，转化为绝对路径
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static string NormalizePath(string path)
    {
        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        var normalized = new Stack<string>();
        foreach (var part in parts)
            switch (part)
            {
                case ".": continue;
                case "..":
                    if (normalized.Count > 0)
                        normalized.Pop();
                    break;
                default:
                    normalized.Push(part);
                    break;
            }

        return string.Join("/", normalized.Reverse());
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
        return JsonConvert.DeserializeObject<T>(ReadText(path));
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
            File.WriteAllText(path, JsonConvert.SerializeObject(invoke));
            return invoke;
        }
    }

    private static void BackupAndWriteText(string path, string text)
    {
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.WriteAllText(path, text);
    }

    public static void WriteJsonAndBackup(string path, object obj)
    {
        BackupAndWriteText(path, JsonConvert.SerializeObject(obj));
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

    #endregion
}