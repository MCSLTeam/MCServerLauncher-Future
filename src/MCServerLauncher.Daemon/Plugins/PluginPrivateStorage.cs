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
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            lock (_gate)
            {
                EnsureCapacityForWrite(fullPath, bytes.LongLength);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                // Atomic replace via temp + move.
                var temp = fullPath + ".tmp";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, fullPath, overwrite: true);
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
            var line = JsonSerializer.Serialize(value, typeInfo) + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            lock (_gate)
            {
                EnsureCapacityForWrite(fullPath, bytes.LongLength);
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
            if (File.Exists(fullPath))
                File.Delete(fullPath);
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

    private void EnsureCapacityForWrite(string fullPath, long incomingBytes)
    {
        long total = 0;
        var fileCount = 0;
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                if (StringComparer.OrdinalIgnoreCase.Equals(file, fullPath))
                    continue;
                total += new FileInfo(file).Length;
            }
        }

        var replacing = File.Exists(fullPath);
        if (!replacing)
            fileCount++;

        if (fileCount > MaxFiles)
        {
            throw new DaemonErrorException(_errors.Create(
                "plugin_storage_file_quota",
                $"Plugin storage file count would exceed the maximum of {MaxFiles}."));
        }

        if (total + incomingBytes > QuotaBytes)
        {
            throw new DaemonErrorException(_errors.Create(
                "plugin_storage_byte_quota",
                $"Plugin storage write would exceed the quota of {QuotaBytes} bytes."));
        }
    }

    private sealed class DaemonErrorException(DaemonError error) : Exception(error.Message)
    {
        public DaemonError Error { get; } = error;
    }
}
