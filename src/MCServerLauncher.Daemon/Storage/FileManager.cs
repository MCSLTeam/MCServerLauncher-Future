using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Downloader;
using MCServerLauncher.Daemon.Serialization;
using Serilog;

namespace MCServerLauncher.Daemon.Storage;

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

    private static readonly ConcurrentDictionary<Guid, IDownload> Downloading = new();
    private static readonly JsonSerializerOptions PersistenceSourceGenReadOptions =
        DaemonPersistenceJsonBoundary.CreateStjOptions();
    private static readonly JsonSerializerOptions PersistenceSourceGenWriteIndentedOptions =
        DaemonPersistenceJsonBoundary.CreateStjOptions(writeIndented: true);

    public static async Task<string> FileSha1(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha1 = SHA1.Create();
        var hash = await sha1.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Guid DownloadFromUrl(
        string? targetDir,
        string filename,
        string url,
        out IDownload download,
        string? sha1 = null,
        int maxThreads = 16)
    {
        if (targetDir is not null)
            targetDir = ResolveAndValidatePath(targetDir);

        Directory.CreateDirectory(DownloadRoot);
        var filePath = Path.Combine(targetDir ?? DownloadRoot, filename);
        var temporaryPath = filePath + ".tmp";
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);
        if (File.Exists(filePath))
            File.Delete(filePath);

        var fileId = Guid.NewGuid();
        download = DownloadBuilder.New()
            .WithUrl(url)
            .WithFileLocation(temporaryPath)
            .WithConfiguration(new DownloadConfiguration
            {
                ChunkCount = maxThreads,
                ParallelDownload = true
            })
            .Build();

        download.DownloadFileCompleted += async (sender, args) =>
        {
            try
            {
                if (args.Error is not null)
                {
                    DeleteIfExists(temporaryPath);
                    Log.Warning("[FileManager] Failed to download file {Filename}: {Message}", filename, args.Error.Message);
                    return;
                }

                if (!string.IsNullOrEmpty(sha1))
                {
                    var actualSha1 = await FileSha1(temporaryPath);
                    if (!actualSha1.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteIfExists(temporaryPath);
                        Log.Warning("[FileManager] Downloaded file {Filename} SHA-1 mismatch.", filename);
                        return;
                    }
                }

                File.Move(temporaryPath, filePath, overwrite: true);
            }
            finally
            {
                Downloading.TryRemove(fileId, out var removedDownload);
            }
        };

        download.StartAsync();
        Downloading.TryAdd(fileId, download);
        return fileId;
    }

    public static bool TryGetDownloading(Guid downloadingFileId, out IDownload? download)
    {
        return Downloading.TryGetValue(downloadingFileId, out download);
    }

    public static string ResolveAndValidatePath(string path, string? root = null)
    {
        return FileSessionCoordinator.ResolveAndValidatePath(path, root);
    }

    public static T? ReadJson<T>(string path)
    {
        return JsonSerializer.Deserialize(File.ReadAllText(path), FileManagerPersistenceTypeInfoCache<T>.ReadTypeInfo);
    }

    public static T ReadJsonOr<T>(string path, Func<T> defaultFactory)
    {
        try
        {
            return ReadJson<T>(path)!;
        }
        catch (FileNotFoundException)
        {
            var value = defaultFactory();
            File.WriteAllText(path, JsonSerializer.Serialize(value, FileManagerPersistenceTypeInfoCache<T>.WriteTypeInfo));
            return value;
        }
    }

    public static void WriteJsonAndBackup<T>(string path, T value)
    {
        WriteJsonAndBackupCore(path, value, writeTemporaryFile: null);
    }

    internal static void WriteJsonAndBackupWithTemporaryFileWriterForTests<T>(
        string path,
        T value,
        Action<string, string> writeTemporaryFile)
    {
        ArgumentNullException.ThrowIfNull(writeTemporaryFile);
        WriteJsonAndBackupCore(path, value, writeTemporaryFile);
    }

    private static void WriteJsonAndBackupCore<T>(
        string path,
        T value,
        Action<string, string>? writeTemporaryFile)
    {
        var serialized = JsonSerializer.Serialize(value, FileManagerPersistenceTypeInfoCache<T>.WriteTypeInfo);
        var hasValidExistingFile = false;
        if (File.Exists(path))
        {
            try
            {
                _ = JsonSerializer.Deserialize(File.ReadAllText(path), FileManagerPersistenceTypeInfoCache<T>.ReadTypeInfo);
                hasValidExistingFile = true;
            }
            catch (JsonException)
            {
                Log.Warning("[FileManager] File {Path} is not valid JSON; skipping backup.", path);
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            if (writeTemporaryFile is null)
                File.WriteAllText(temporaryPath, serialized);
            else
                writeTemporaryFile(temporaryPath, serialized);

            if (File.Exists(path))
            {
                if (hasValidExistingFile)
                    File.Replace(temporaryPath, path, path + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, path, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
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
        catch (Exception exception)
        {
            Log.Error(exception, "[FileManager] Failed to remove {Path}.", path);
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static class FileManagerPersistenceTypeInfoCache<T>
    {
        private static readonly JsonTypeInfo<T>? ReadTypeInfoValue = TryResolve(PersistenceSourceGenReadOptions);
        private static readonly JsonTypeInfo<T>? WriteTypeInfoValue = TryResolve(PersistenceSourceGenWriteIndentedOptions);

        public static JsonTypeInfo<T> ReadTypeInfo => ReadTypeInfoValue ?? throw CreateMissingTypeInfoException();
        public static JsonTypeInfo<T> WriteTypeInfo => WriteTypeInfoValue ?? throw CreateMissingTypeInfoException();

        private static JsonTypeInfo<T>? TryResolve(JsonSerializerOptions options)
        {
            try
            {
                return options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static NotSupportedException CreateMissingTypeInfoException()
        {
            return new NotSupportedException(
                $"FileManager persistence helpers require source-generated JsonTypeInfo for {typeof(T).FullName ?? typeof(T).Name}.");
        }
    }
}
