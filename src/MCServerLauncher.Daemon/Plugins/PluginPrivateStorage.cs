using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Plugin-private storage under daemon-owned root with quota. Paths are relative and
/// containment-validated; absolute host paths are never returned.
/// </summary>
internal sealed class PluginPrivateStorage : IPluginPrivateStorage
{
    private readonly string _root;
    private readonly PluginErrorFactory _errors;
    private readonly object _gate = new();

    internal PluginPrivateStorage(
        PluginIdentity identity,
        DaemonPluginsConfig pluginsConfig,
        PluginErrorFactory errors)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(pluginsConfig);
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

        var quota = pluginsConfig.Storage.DefaultQuotaBytes;
        var maxFiles = pluginsConfig.Storage.DefaultMaxFiles;
        if (pluginsConfig.Entries.TryGetValue(identity.Id, out var entry) &&
            entry.StorageQuotaBytes is { } overrideQuota &&
            overrideQuota > 0)
        {
            quota = overrideQuota;
        }

        QuotaBytes = quota > 0 ? quota : 268_435_456;
        MaxFiles = maxFiles > 0 ? maxFiles : 4096;

        _root = Path.GetFullPath(Path.Combine(FileManager.Root, "plugins", identity.Id, "data"));
        Directory.CreateDirectory(_root);
    }

    public long QuotaBytes { get; }

    public int MaxFiles { get; }

    public async Task<Result<Unit, DaemonError>> WriteSnapshotAsync<T>(
        string relativePath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!TryResolve(relativePath, out var fullPath, out var error))
            return Result.Err<Unit, DaemonError>(error!);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCapacityForReplace(fullPath, bytes.LongLength);
                var directory = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllBytes(temporaryPath, bytes);
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }

            await Task.CompletedTask.ConfigureAwait(false);
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
        catch (DaemonErrorException exception)
        {
            return Result.Err<Unit, DaemonError>(exception.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Err<Unit, DaemonError>(_errors.Create(
                "plugin_storage_write_failed",
                $"Failed to write plugin storage snapshot: {exception.Message}"));
        }
    }

    public async Task<Result<T, DaemonError>> ReadSnapshotAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!TryResolve(relativePath, out var fullPath, out var error))
            return Result.Err<T, DaemonError>(error!);

        try
        {
            if (!File.Exists(fullPath))
            {
                return Result.Err<T, DaemonError>(_errors.Create(
                    "plugin_storage_not_found",
                    "The requested plugin storage snapshot does not exist."));
            }

            await using var stream = File.OpenRead(fullPath);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
                .ConfigureAwait(false);
            if (value is null)
            {
                return Result.Err<T, DaemonError>(_errors.Create(
                    "plugin_storage_invalid",
                    "The plugin storage snapshot deserialized to null."));
            }

            return Result.Ok<T, DaemonError>(value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Err<T, DaemonError>(_errors.Create(
                "plugin_storage_read_failed",
                $"Failed to read plugin storage snapshot: {exception.Message}"));
        }
    }

    public async Task<Result<Unit, DaemonError>> AppendJsonlAsync<T>(
        string relativePath,
        T value,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!TryResolve(relativePath, out var fullPath, out var error))
            return Result.Err<Unit, DaemonError>(error!);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            var bytes = new byte[payload.Length + 1];
            payload.CopyTo(bytes, 0);
            bytes[^1] = (byte)'\n';
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCapacityForAppend(fullPath, bytes.LongLength);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var stream = new FileStream(
                    fullPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Write(bytes, 0, bytes.Length);
            }

            await Task.CompletedTask.ConfigureAwait(false);
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
        catch (DaemonErrorException exception)
        {
            return Result.Err<Unit, DaemonError>(exception.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Err<Unit, DaemonError>(_errors.Create(
                "plugin_storage_append_failed",
                $"Failed to append plugin storage JSONL: {exception.Message}"));
        }
    }

    public Task<Result<Unit, DaemonError>> DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (!TryResolve(relativePath, out var fullPath, out var error))
            return Task.FromResult(Result.Err<Unit, DaemonError>(error!));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(_errors.Create(
                "plugin_storage_delete_failed",
                $"Failed to delete plugin storage entry: {exception.Message}")));
        }
    }

    private bool TryResolve(string relativePath, out string fullPath, out DaemonError? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal) ||
            relativePath.Contains(':') ||
            relativePath.StartsWith('/') ||
            relativePath.StartsWith('\\'))
        {
            error = _errors.Create(
                "plugin_storage_path_invalid",
                "Plugin storage paths must be relative and contained.");
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            error = _errors.Create(
                "plugin_storage_path_escape",
                "Plugin storage path escapes the private root.");
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private void EnsureCapacityForReplace(string fullPath, long replacementBytes)
    {
        var usage = MeasureUsage(fullPath);
        EnsureFileCapacity(usage.FileCount + (usage.TargetExists ? 0 : 1));

        var retainedBytes = usage.TotalBytes - usage.TargetBytes;
        EnsureByteCapacity(retainedBytes, replacementBytes, "write");
    }

    private void EnsureCapacityForAppend(string fullPath, long appendedBytes)
    {
        var usage = MeasureUsage(fullPath);
        EnsureFileCapacity(usage.FileCount + (usage.TargetExists ? 0 : 1));
        EnsureByteCapacity(usage.TotalBytes, appendedBytes, "append");
    }

    private StorageUsage MeasureUsage(string fullPath)
    {
        long totalBytes = 0;
        long targetBytes = 0;
        var fileCount = 0;
        var targetExists = false;
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var length = new FileInfo(file).Length;
                fileCount++;
                totalBytes += length;
                if (StringComparer.OrdinalIgnoreCase.Equals(file, fullPath))
                {
                    targetExists = true;
                    targetBytes = length;
                }
            }
        }

        return new StorageUsage(totalBytes, fileCount, targetExists, targetBytes);
    }

    private void EnsureFileCapacity(int projectedFileCount)
    {
        if (projectedFileCount <= MaxFiles)
            return;

        throw new DaemonErrorException(_errors.Create(
            "plugin_storage_file_quota",
            $"Plugin storage file count would exceed the maximum of {MaxFiles}."));
    }

    private void EnsureByteCapacity(long retainedBytes, long incomingBytes, string operation)
    {
        if (incomingBytes <= QuotaBytes && retainedBytes <= QuotaBytes - incomingBytes)
            return;

        throw new DaemonErrorException(_errors.Create(
            "plugin_storage_byte_quota",
            $"Plugin storage {operation} would exceed the quota of {QuotaBytes} bytes."));
    }

    private readonly record struct StorageUsage(
        long TotalBytes,
        int FileCount,
        bool TargetExists,
        long TargetBytes);

    private sealed class DaemonErrorException(DaemonError error) : Exception(error.Message)
    {
        public DaemonError Error { get; } = error;
    }
}
