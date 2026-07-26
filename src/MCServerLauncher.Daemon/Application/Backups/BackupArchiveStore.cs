using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Backups;

/// <summary>
/// Daemon-owned cold backup archive store: one validated atomic <c>index.json</c> snapshot plus
/// <c>archives/{archiveId}.zip</c> payloads, outside plugin-private data. Archive manifests must stay
/// durable independently of the operation index, whose retention silently prunes terminal records.
/// </summary>
internal sealed class BackupArchiveStore
{
    internal const int CurrentManifestVersion = 1;
    internal const string CompressionMethod = "deflate";

    /// <summary>
    /// Names carrying these prefixes are daemon staging/teardown artifacts and never belong in an
    /// archive; backing up an instance mid-swap must not capture its own scaffolding.
    /// </summary>
    private static readonly string[] ExcludedNamePrefixes = [".instance-update-", ".removing-", ".restore-"];

    private readonly object _persistGate = new();
    private readonly Dictionary<Guid, BackupArchiveManifest> _manifests = new();
    private readonly string _root;
    private readonly string _archiveRoot;
    private readonly string _indexPath;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupArchiveStore> _logger;
    private readonly DaemonBackupConfig _config;

    public BackupArchiveStore(
        DaemonBackupConfig config,
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        ILogger<BackupArchiveStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<BackupArchiveStore>.Instance;
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "backups"));
        _archiveRoot = Path.Combine(_root, "archives");
        Directory.CreateDirectory(_archiveRoot);
        _indexPath = Path.Combine(_root, "index.json");
        Load();
        SweepOrphans();
    }

    public ImmutableArray<BackupArchiveManifest> List(Guid? instanceId = null)
    {
        lock (_persistGate)
        {
            var manifests = _manifests.Values
                .Where(manifest => instanceId is null || manifest.InstanceId == instanceId)
                .OrderByDescending(static manifest => manifest.CreatedAt)
                .ThenBy(static manifest => manifest.ArchiveId)
                .ToImmutableArray();
            return manifests;
        }
    }

    public Result<BackupArchiveManifest, DaemonError> Get(Guid archiveId)
    {
        lock (_persistGate)
        {
            return _manifests.TryGetValue(archiveId, out var manifest)
                ? Result.Ok<BackupArchiveManifest, DaemonError>(manifest)
                : Result.Err<BackupArchiveManifest, DaemonError>(
                    new NotFoundDaemonError("backup.archive_not_found", "The backup archive was not found."));
        }
    }

    public string GetArchivePath(Guid archiveId) => Path.Combine(_archiveRoot, $"{archiveId:D}.zip");

    /// <summary>
    /// Archives the given working directory into a new zip, records the manifest, and returns it.
    /// The directory must belong to a stopped instance; the caller owns that gate.
    /// </summary>
    public async Task<Result<BackupArchiveManifest, DaemonError>> CreateArchiveAsync(
        Guid instanceId,
        string instanceName,
        string instanceVersion,
        string workingDirectory,
        string configFileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        workingDirectory = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            return Result.Err<BackupArchiveManifest, DaemonError>(
                new NotFoundDaemonError("backup.instance_directory_missing", "The instance directory does not exist."));
        }

        var archiveId = Guid.NewGuid();
        var archivePath = GetArchivePath(archiveId);
        var temporaryPath = archivePath + ".tmp";
        var files = ImmutableArray.CreateBuilder<BackupFileEntry>();
        long totalSizeBytes = 0;
        try
        {
            using (var zipStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var filePath in EnumerateArchivableFiles(workingDirectory, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(workingDirectory, filePath)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    var length = new FileInfo(filePath).Length;
                    archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                    files.Add(new BackupFileEntry(relativePath, length));
                    totalSizeBytes += length;
                }
            }

            var configPath = Path.Combine(workingDirectory, configFileName);
            var configSha256 = File.Exists(configPath)
                ? await FileManager.FileSha256(configPath, cancellationToken).ConfigureAwait(false)
                : null;
            var sha256 = await FileManager.FileSha256(temporaryPath, cancellationToken).ConfigureAwait(false);
            var manifest = new BackupArchiveManifest(
                archiveId,
                instanceId,
                instanceName,
                CurrentManifestVersion,
                instanceVersion,
                configSha256,
                _timeProvider.GetUtcNow(),
                files.ToImmutable(),
                totalSizeBytes,
                new FileInfo(temporaryPath).Length,
                CompressionMethod,
                sha256);
            ValidateManifest(manifest);

            lock (_persistGate)
            {
                // The zip lands before the index entry: a crash between the two leaves an orphaned
                // zip (swept on next load), never a manifest without its payload.
                File.Move(temporaryPath, archivePath, overwrite: true);
                try
                {
                    _manifests.Add(archiveId, manifest);
                    PersistUnderLock();
                }
                catch
                {
                    _manifests.Remove(archiveId);
                    FileManager.DeleteIfExists(archivePath);
                    throw;
                }
            }

            return Result.Ok<BackupArchiveManifest, DaemonError>(manifest);
        }
        finally
        {
            FileManager.DeleteIfExists(temporaryPath);
        }
    }

    /// <summary>
    /// Recomputes the archive checksum against its manifest before any restore mutates state.
    /// </summary>
    public async Task<Result<BackupArchiveManifest, DaemonError>> VerifyArchiveAsync(
        Guid archiveId,
        CancellationToken cancellationToken)
    {
        var manifest = Get(archiveId);
        if (!manifest.IsOk(out var expected))
            return manifest;

        var archivePath = GetArchivePath(archiveId);
        if (!File.Exists(archivePath))
        {
            return Result.Err<BackupArchiveManifest, DaemonError>(
                new ConflictDaemonError("backup.archive_missing", "The backup archive payload is missing."));
        }

        var actual = await FileManager.FileSha256(archivePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, expected!.Sha256, StringComparison.Ordinal))
        {
            return Result.Err<BackupArchiveManifest, DaemonError>(
                new ConflictDaemonError("backup.checksum_mismatch", "The backup archive does not match its manifest."));
        }

        return Result.Ok<BackupArchiveManifest, DaemonError>(expected);
    }

    /// <summary>
    /// Extracts an archive into <paramref name="stagingDirectory" /> entry by entry. Every entry path
    /// is contained-checked before any byte is written, and link entries are rejected — an archive can
    /// never write outside the staging directory it is handed.
    /// </summary>
    public Result<Unit, DaemonError> ExtractToStaging(
        Guid archiveId,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var archivePath = GetArchivePath(archiveId);
        if (!File.Exists(archivePath))
        {
            return Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("backup.archive_missing", "The backup archive payload is missing."));
        }

        stagingDirectory = Path.GetFullPath(stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveEntryDestination(stagingDirectory, entry, out var destination))
            {
                return Result.Err<Unit, DaemonError>(
                    new ValidationDaemonError(
                        "backup.archive_entry_invalid",
                        $"The archive entry '{entry.FullName}' escapes the restore staging directory or is a link."));
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }

        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    /// <summary>
    /// Applies age, count, and byte retention caps, never touching pinned archives (those referenced
    /// by active restore plans). The index rewrite commits before any zip deletion, so a crash leaves
    /// orphaned zips for the load-time sweep instead of dangling manifests.
    /// </summary>
    public Result<ImmutableArray<Guid>, DaemonError> Prune(IReadOnlySet<Guid> pinnedArchiveIds)
    {
        ArgumentNullException.ThrowIfNull(pinnedArchiveIds);
        lock (_persistGate)
        {
            var now = _timeProvider.GetUtcNow();
            var ageLimit = now - TimeSpan.FromDays(_config.RetentionDays);
            var removable = _manifests.Values
                .Where(manifest => !pinnedArchiveIds.Contains(manifest.ArchiveId))
                .OrderBy(static manifest => manifest.CreatedAt)
                .ThenBy(static manifest => manifest.ArchiveId)
                .ToList();

            var removed = new List<BackupArchiveManifest>();
            foreach (var manifest in removable.Where(manifest => manifest.CreatedAt < ageLimit))
                removed.Add(manifest);

            var survivors = removable.Except(removed).ToList();
            var keptCount = _manifests.Count - removed.Count;
            foreach (var manifest in survivors)
            {
                if (keptCount <= _config.MaximumCount)
                    break;
                removed.Add(manifest);
                keptCount--;
            }

            var totalBytes = _manifests.Values
                .Except(removed)
                .Sum(static manifest => manifest.CompressedSizeBytes);
            foreach (var manifest in survivors.Except(removed))
            {
                if (totalBytes <= _config.MaximumBytes)
                    break;
                removed.Add(manifest);
                totalBytes -= manifest.CompressedSizeBytes;
            }

            if (removed.Count == 0)
                return Result.Ok<ImmutableArray<Guid>, DaemonError>(ImmutableArray<Guid>.Empty);

            foreach (var manifest in removed)
                _manifests.Remove(manifest.ArchiveId);
            try
            {
                PersistUnderLock();
            }
            catch
            {
                foreach (var manifest in removed)
                    _manifests.Add(manifest.ArchiveId, manifest);
                throw;
            }

            foreach (var manifest in removed)
                FileManager.DeleteIfExists(GetArchivePath(manifest.ArchiveId));
            return Result.Ok<ImmutableArray<Guid>, DaemonError>(
                removed.Select(static manifest => manifest.ArchiveId).ToImmutableArray());
        }
    }

    private static IEnumerable<string> EnumerateArchivableFiles(string root, CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (ExcludedNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;

                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException(
                        $"The instance directory entry '{Path.GetRelativePath(root, entry)}' is a reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                    pendingDirectories.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    private static bool TryResolveEntryDestination(
        string stagingDirectory,
        ZipArchiveEntry entry,
        out string destination)
    {
        destination = string.Empty;
        // Unix external attributes live in the upper 16 bits; 0xA000 marks a symlink entry.
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            return false;

        var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(stagingDirectory, name));
        var containedRoot = stagingDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? stagingDirectory
            : stagingDirectory + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(containedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        destination = candidate;
        return true;
    }

    private void PersistUnderLock()
    {
        var manifests = _manifests.Values
            .OrderBy(static manifest => manifest.CreatedAt)
            .ThenBy(static manifest => manifest.ArchiveId)
            .ToArray();
        foreach (var manifest in manifests)
            ValidateManifest(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            manifests,
            BackupPersistenceJsonContext.Default.BackupArchiveManifestArray);
        var temporaryPath = _indexPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, _indexPath, overwrite: true);
        }
        finally
        {
            FileManager.DeleteIfExists(temporaryPath);
        }
    }

    private void Load()
    {
        if (!File.Exists(_indexPath))
            return;

        BackupArchiveManifest[] manifests;
        try
        {
            var bytes = File.ReadAllBytes(_indexPath);
            manifests = JsonSerializer.Deserialize(bytes, BackupPersistenceJsonContext.Default.BackupArchiveManifestArray)
                ?? throw new InvalidDataException("The backup index JSON root cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The backup index contains invalid JSON.", exception);
        }

        foreach (var manifest in manifests)
        {
            ValidateManifest(manifest);
            if (!_manifests.TryAdd(manifest.ArchiveId, manifest))
                throw new InvalidDataException($"The backup index contains duplicate archive id '{manifest.ArchiveId:D}'.");
        }
    }

    /// <summary>
    /// Deletes archive payloads the index does not reference and drops manifests whose payload is
    /// gone, so the index always reflects what a restore can actually read.
    /// </summary>
    private void SweepOrphans()
    {
        lock (_persistGate)
        {
            foreach (var stray in Directory.EnumerateFiles(_archiveRoot, "*.tmp"))
                FileManager.DeleteIfExists(stray);

            var lost = _manifests.Values
                .Where(manifest => !File.Exists(GetArchivePath(manifest.ArchiveId)))
                .ToList();
            if (lost.Count > 0)
            {
                foreach (var manifest in lost)
                {
                    _logger.LogWarning(
                        "[BackupArchiveStore] Dropping manifest '{ArchiveId}' because its archive payload is missing.",
                        manifest.ArchiveId);
                    _manifests.Remove(manifest.ArchiveId);
                }

                PersistUnderLock();
            }

            foreach (var zipPath in Directory.EnumerateFiles(_archiveRoot, "*.zip"))
            {
                var name = Path.GetFileNameWithoutExtension(zipPath);
                if (!Guid.TryParse(name, out var archiveId) || !_manifests.ContainsKey(archiveId))
                {
                    _logger.LogWarning(
                        "[BackupArchiveStore] Deleting orphaned archive payload '{Path}'.",
                        zipPath);
                    FileManager.DeleteIfExists(zipPath);
                }
            }
        }
    }

    private static void ValidateManifest(BackupArchiveManifest manifest)
    {
        if (manifest is null ||
            manifest.ArchiveId == Guid.Empty ||
            manifest.InstanceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(manifest.InstanceName) ||
            manifest.ManifestVersion < 1 ||
            manifest.InstanceVersion is null ||
            manifest.CreatedAt == default ||
            manifest.Files.IsDefault ||
            manifest.Files.Any(static file =>
                file is null ||
                string.IsNullOrWhiteSpace(file.RelativePath) ||
                file.SizeBytes < 0) ||
            manifest.TotalSizeBytes < 0 ||
            manifest.CompressedSizeBytes < 0 ||
            string.IsNullOrWhiteSpace(manifest.CompressionMethod) ||
            !IsSha256Hex(manifest.Sha256) ||
            (manifest.ConfigSha256 is not null && !IsSha256Hex(manifest.ConfigSha256)))
        {
            throw new InvalidDataException("The backup index contains an invalid manifest record.");
        }
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character) && !char.IsUpper(character));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BackupArchiveManifest[]))]
internal partial class BackupPersistenceJsonContext : JsonSerializerContext;
