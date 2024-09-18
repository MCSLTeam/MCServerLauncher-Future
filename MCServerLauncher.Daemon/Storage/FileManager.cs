using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Downloader;
using Newtonsoft.Json;
using Serilog;
using WebSocketSharp;

namespace MCServerLauncher.Daemon.Storage;

internal static class FileManager
{
    public const string Root = "daemon";
    public static readonly string InstancesRoot = Path.Combine(Root, "instances");
    public static readonly string CacheRoot = Path.Combine(Root, "caches");
    public static readonly string UploadRoot = Path.Combine(CacheRoot, "uploads");
    public static readonly string DownloadRoot = Path.Combine(CacheRoot, "downloads");
    public static readonly string CoreRoot = Path.Combine(Root, "cores");
    public static readonly string LogRoot = Path.Combine(Root, "logs");

    private static readonly ConcurrentDictionary<Guid, FileUploadInfo> _uploadSessions = new();
    private static readonly ConcurrentDictionary<Guid, Task> _downloadSessions = new();
    private static readonly ConcurrentDictionary<Guid, string> _filePath = new();

    /// <summary>
    ///     请求上传文件:首先检查是否有同名文件正在上传,若没有,则预分配空间并添加后缀.tmp,返回file_id
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="size">文件总大小</param>
    /// <param name="chunkSize">文件分片传输大小</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <returns>分配的file_id</returns>
    public static Guid FileUploadRequest(string path, long size, long chunkSize, string? sha1 = "")
    {
        // 由于FileStream.WriteAsync只支持int,故提前检查,若大于2G,则返回空
        if ((int)size != size || (int)chunkSize != chunkSize || size < 0 || chunkSize < 0) return Guid.Empty;

        var fileName = Path.Combine(UploadRoot, path);

        // check if file is uploading
        foreach (var info in _uploadSessions.Values)
            if (info.FileName == path)
                throw new IOException("File is uploading");

        // pre-allocate file in disk
        try
        {
            // ensure directory exists
            Directory.CreateDirectory(UploadRoot);
            var tmpFile = fileName + ".tmp";
            // delete file if exists
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
            if (File.Exists(fileName)) File.Delete(fileName);

            FileStream fs = new(tmpFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength(size);
            fs.Seek(0, SeekOrigin.Begin);
            var guid = Guid.NewGuid();

            _uploadSessions.TryAdd(guid, new FileUploadInfo(fileName, size, chunkSize, sha1, fs));
            Log.Debug("[FileUploadChunk] Uploading file {0}", Path.GetFileName(fileName));
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
    /// <exception cref="Exception"></exception>
    public static async Task<(bool, long)> FileUploadChunk(Guid id, long offset, string strData)
    {
        if (!_uploadSessions.TryGetValue(id, out var info))
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
        File.Move(info.FileName + ".tmp", info.FileName, true);

        if (!sha1.IsNullOrEmpty())
        {
            if (sha1 != info.Sha1)
            {
                Log.Warning("[FileUploadChunk] Uploaded file {0} SHA1 mismatch!", Path.GetFileName(info.FileName));
                File.Delete(info.FileName);
                throw new IOException("File SHA1 mismatch");
            }
        }
        else
        {
            Log.Debug("[FileUploadChunk] Uploaded file {0} did not provide expected sha1, skipping check",
                Path.GetFileName(info.FileName));
        }

        _uploadSessions.TryRemove(id, out _);
        _filePath.TryAdd(id, info.FileName);
        Log.Debug("[FileUploadChunk] File {0} upload complete", Path.GetFileName(info.FileName));
        return (true, info.Size - info.RemainLength);
    }

    public static bool FileUploadCancel(Guid id)
    {
        if (_uploadSessions.TryRemove(id, out var info))
        {
            info.File.Close();
            // delete tmp file 
            if (File.Exists(info.FileName + ".tmp"))
                File.Delete(info.FileName + ".tmp");
            Log.Debug("[FileUploadChunk] File {0} upload canceled", Path.GetFileName(info.FileName));
            return true;
        }

        return false;
    }

    private static Task<string> FileSha1(FileStream fs, uint bufferSize = 16384)
    {
        return Task.Run(() =>
        {
            using (var sha1 = SHA1.Create())
            {
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
            }
        });
    }

    /// <summary>
    ///     下载文件
    /// </summary>
    /// <param name="filename">文件名称</param>
    /// <param name="url">下载URL</param>
    /// <param name="sha1">预期的SHA1，为空为null不进行校验</param>
    /// <param name="maxThreads">下载最大线程数</param>
    /// <returns>分配的file_id</returns>
    public static async Task<Guid> DownloadFile(string filename, string url, string? sha1 = "", int maxThreads = 16)
    {
        // 确保存在
        Directory.CreateDirectory(DownloadRoot);
        var filePath = Path.Combine(DownloadRoot, filename);
        var tmpFilePath = Path.Combine(DownloadRoot, filename + ".tmp");
        File.Delete(tmpFilePath);
        File.Delete(filePath);
        var fileId = Guid.NewGuid();
        Log.Debug("[FileManager] Downloading file {0} from {1}", filename, url);

        var downloadOpt = new DownloadConfiguration
        {
            ChunkCount = maxThreads,
            ParallelDownload = true
        };

        var downloader = new DownloadService(downloadOpt);

        downloader.DownloadFileCompleted += async (_, args) =>
        {
            if (args.Error != null)
            {
                File.Delete(tmpFilePath);
                Log.Warning("[FileManager] Failed to download file {0}: {1}", filename, args.Error.Message);
            }
            else if (!sha1.IsNullOrEmpty())
            {
                var fileSha1 =
                    await FileSha1(new FileStream(tmpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                if (sha1 != fileSha1)
                {
                    Log.Warning("[FileManager] Downloaded file {0} SHA1 mismatch!", filename);
                    File.Delete(tmpFilePath);
                    return;
                }
            }
            else
            {
                Log.Debug("[FileManager] Downloaded file {0} did not provide expected sha1, skipping check",
                    filename);
            }

            File.Move(tmpFilePath, filePath, true);
        };

        var task = downloader.DownloadFileTaskAsync(url, tmpFilePath);

        _downloadSessions.TryAdd(fileId, task);

        return fileId;
    }

    /// <summary>
    ///     根据file_id获取文件路径，不存在为null
    /// </summary>
    /// <param name="fileId">file_id</param>
    /// <returns>文件路径</returns>
    public static string? GetFilePathById(Guid fileId)
    {
        if (!_filePath.TryGetValue(fileId, out var path))
            throw new FileNotFoundException("File id " + fileId + " not found");
        return path;
    }

    /// <summary>
    ///     根据file_id获取文件状态，不存在为null
    /// </summary>
    /// <param name="fileId">file_id</param>
    /// <returns>文件状态</returns>
    public static FileStatus GetFileStatusById(Guid fileId)
    {
        if (_uploadSessions.ContainsKey(fileId))
            return FileStatus.Uploading;
        if (_downloadSessions.ContainsKey(fileId))
            return FileStatus.Downloading;
        if (_filePath.ContainsKey(fileId))
            return FileStatus.Exist;
        return FileStatus.NotExist;
    }

    private static Task WaitUntilExistTask(Guid fileId)
    {
        if (_downloadSessions.TryGetValue(fileId, out var t))
            return t;
        var task = Task.Run(() =>
        {
            while (!_filePath.ContainsKey(fileId)) Thread.Sleep(100);
        });

        return task;
    }

    /// <summary>
    ///     等待直到文件存在（同步）
    /// </summary>
    /// <param name="fileId">file_id</param>
    public static void WaitUntilExist(Guid fileId)
    {
        WaitUntilExistTask(fileId).Wait();
    }

    /// <summary>
    ///     等待直到文件存在（异步）
    /// </summary>
    /// <param name="fileId">file_id</param>
    public static async Task WaitUntilExistAsync(Guid fileId)
    {
        await WaitUntilExistTask(fileId);
    }

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
    public static T? ReadJsonOr<T>(string path, Func<T> defaultFactory)
    {
        try
        {
            return ReadJson<T>(path);
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
}