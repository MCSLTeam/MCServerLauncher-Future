using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;
using RResult = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Storage;

internal sealed class FileSessionCoordinator : IAsyncDisposable
{
    internal const int MaxChunkSize = 1024 * 1024;
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, FileUploadInfo> _uploadSessions = new();
    private readonly ConcurrentDictionary<Guid, FileDownloadInfo> _downloadSessions = new();
    private readonly ConcurrentDictionary<string, Guid> _uploadLeases = new(GetPathComparer());
    private readonly SemaphoreSlim _sessionAdmissionGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly Func<Stream, HashAlgorithmName, CancellationToken, Task<string>> _hashAsync;
    private readonly Action<string> _deleteUploadStaging;
    private readonly Func<string, Task>? _onUploadStagingCreatedAsync;
    private int? _downloadSessionLimit;
    private ITimer? _cleanupTimer;
    private int _started;
    private int _stopped;

    public FileSessionCoordinator(
        TimeProvider? timeProvider = null,
        int? downloadSessionLimit = null,
        Func<Stream, HashAlgorithmName, CancellationToken, Task<string>>? hashAsync = null,
        Action<string>? deleteUploadStaging = null,
        Func<string, Task>? onUploadStagingCreatedAsync = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _downloadSessionLimit = downloadSessionLimit;
        _hashAsync = hashAsync ?? GetHashAsync;
        _deleteUploadStaging = deleteUploadStaging ?? DeleteIfExists;
        _onUploadStagingCreatedAsync = onUploadStagingCreatedAsync;
    }

    internal static FileSessionCoordinator Shared { get; } = new();

    internal void ConfigureDownloadSessionLimit(int downloadSessionLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(downloadSessionLimit);
        if (Volatile.Read(ref _started) != 0)
            throw new InvalidOperationException("The file session coordinator has already started.");

        _downloadSessionLimit = downloadSessionLimit;
    }

    internal void Start()
    {
        if (IsStopped)
            return;

        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _cleanupTimer = _timeProvider.CreateTimer(
            static state => ((FileSessionCoordinator)state!).ScheduleCleanup(),
            this,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await OpenUploadCoreAsync(request.Path, request.Length, request.Sha256, legacySha1: null, cancellationToken);
    }

    internal async Task<Result<LegacyUploadSession, DaemonError>> OpenLegacyUploadAsync(
        string? path,
        long length,
        string? sha1,
        CancellationToken cancellationToken)
    {
        var result = await OpenUploadCoreAsync(
            GetLegacyUploadTargetPath(path),
            length,
            sha256: null,
            NormalizeOptionalSha1(sha1),
            cancellationToken);
        return result.Match(
            value => Ok<LegacyUploadSession>(new LegacyUploadSession(value.SessionId)),
            Err<LegacyUploadSession>);
    }

    public async Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await WriteUploadChunkCoreAsync(request.SessionId, request.Offset, request.Data, cancellationToken);
        return result.Match(
            _ => Ok(Unit.Default),
            Err<Unit>);
    }

    internal async Task<Result<LegacyUploadWriteResult, DaemonError>> WriteLegacyUploadChunkAsync(
        Guid sessionId,
        long offset,
        ImmutableArray<byte> data,
        CancellationToken cancellationToken)
    {
        var writeResult = await WriteUploadChunkCoreAsync(sessionId, offset, data, cancellationToken);
        if (!writeResult.IsOk(out var progress))
            return Err<LegacyUploadWriteResult>(writeResult.UnwrapErr());
        if (!progress.IsComplete)
            return Ok(new LegacyUploadWriteResult(false, progress.Received));

        var closeResult = await CloseUploadCoreAsync(sessionId, cancellationToken);
        return closeResult.Match(
            _ => Ok(new LegacyUploadWriteResult(true, progress.Received)),
            Err<LegacyUploadWriteResult>);
    }

    public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return CloseUploadCoreAsync(sessionId, cancellationToken);
    }

    public async Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_uploadSessions.TryGetValue(sessionId, out var session))
            return Err<Unit>(SessionNotFound(sessionId));

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (session.IsClosed || !_uploadSessions.TryRemove(sessionId, out _))
                return Err<Unit>(SessionNotFound(sessionId));

            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeUploadAsync(session);
        return Ok(Unit.Default);
    }

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await OpenDownloadCoreAsync(request.Path, includeLegacySha1: false, cancellationToken);
        return result.Match(
            value => Ok(new DownloadSession(value.SessionId, value.Size, value.Sha256, MaxChunkSize, value.ExpiresAt)),
            Err<DownloadSession>);
    }

    internal async Task<Result<LegacyDownloadSession, DaemonError>> OpenLegacyDownloadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await OpenDownloadCoreAsync(path, includeLegacySha1: true, cancellationToken);
        return result.Match(
            value => Ok(new LegacyDownloadSession(value.SessionId, value.Size, value.LegacySha1!)),
            Err<LegacyDownloadSession>);
    }

    public async Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ReadDownloadCoreAsync(request.SessionId, request.Offset, request.MaximumLength, cancellationToken);
    }

    internal async Task<Result<ImmutableArray<byte>, DaemonError>> ReadLegacyDownloadRangeAsync(
        Guid sessionId,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            return Err<ImmutableArray<byte>>(new ValidationDaemonError("file.range.invalid", "The requested range is invalid."));

        var length = to - from;
        if (length > MaxChunkSize)
            return Err<ImmutableArray<byte>>(new ValidationDaemonError("file.chunk.size.invalid", "The requested chunk exceeds the maximum size."));

        var result = await ReadDownloadCoreAsync(sessionId, from, checked((int)length), cancellationToken);
        return result.Match(value => Ok(value.Data), Err<ImmutableArray<byte>>);
    }

    public async Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_downloadSessions.TryGetValue(sessionId, out var session))
            return Err<Unit>(SessionNotFound(sessionId));

        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (session.IsClosed || !_downloadSessions.TryRemove(sessionId, out _))
                return Err<Unit>(SessionNotFound(sessionId));

            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeSessionAsync(session);
        return Ok(Unit.Default);
    }

    internal Task<Result<Unit, DaemonError>> CloseLegacyDownloadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return CloseDownloadAsync(sessionId, cancellationToken);
    }

    public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveAndValidatePath(request.Path);
            var info = new DirectoryInfo(path);
            if (!info.Exists)
                throw new DirectoryNotFoundException(path);

            var parent = string.Equals(path, GetFullRoot(), GetPathComparison())
                ? null
                : ToDaemonRelativePath(info.Parent?.FullName);
            return new DirectoryDetails(
                parent,
                info.GetFiles().Select(file => new FileEntry(file.Name, ToFileMetadata(file))).ToImmutableArray(),
                info.GetDirectories().Select(directory => new DirectoryEntry(directory.Name, ToDirectoryMetadata(directory))).ToImmutableArray());
        });
    }

    public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(ResolveAndValidatePath(request.Path));
            if (!info.Exists)
                throw new FileNotFoundException("File not found.", info.FullName);

            return new FileDetails(ToFileMetadata(info));
        });
    }

    public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(ResolveAndValidatePath(request.Path));
            return Unit.Default;
        });
    }

    public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveAndValidatePath(request.Path);
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found.", path);

            File.Delete(path);
            return Unit.Default;
        });
    }

    public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveAndValidatePath(request.Path);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);

            Directory.Delete(path, request.Recursive);
            return Unit.Default;
        });
    }

    public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest request, CancellationToken cancellationToken)
    {
        return RenameAsync(request, isDirectory: false, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest request, CancellationToken cancellationToken)
    {
        return RenameAsync(request, isDirectory: true, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest request, CancellationToken cancellationToken)
    {
        return TransferAsync(request, isDirectory: false, copy: false, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken)
    {
        return TransferAsync(request, isDirectory: true, copy: false, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest request, CancellationToken cancellationToken)
    {
        return TransferAsync(request, isDirectory: false, copy: true, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken)
    {
        return TransferAsync(request, isDirectory: true, copy: true, cancellationToken);
    }

    internal async Task CleanupExpiredAsync()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _uploadSessions)
        {
            if (pair.Value.ExpiresAt <= now && await TryExpireUploadAsync(pair.Key, pair.Value))
                Log.Information("[FileSessionCoordinator] Upload session {SessionId} expired.", pair.Key);
        }

        foreach (var pair in _downloadSessions)
        {
            if (pair.Value.ExpiresAt <= now && await TryExpireDownloadAsync(pair.Key, pair.Value))
                Log.Information("[FileSessionCoordinator] Download session {SessionId} expired.", pair.Key);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _cleanupTimer?.Dispose();
        _cleanupTimer = null;

        await _sessionAdmissionGate.WaitAsync();
        _sessionAdmissionGate.Release();

        foreach (var pair in _uploadSessions)
        {
            if (_uploadSessions.TryRemove(pair.Key, out var session))
                await DisposeUploadLockedAsync(session);
        }

        foreach (var pair in _downloadSessions)
        {
            if (_downloadSessions.TryRemove(pair.Key, out var session))
                await DisposeSessionLockedAsync(session);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    internal static string ResolveAndValidatePath(string path, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        root ??= FileManager.Root;

        var fullRoot = NormalizeDirectoryPath(root);
        var normalizedPath = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var hasDriveQualifiedPath = normalizedPath.Length >= 2 && normalizedPath[1] == Path.VolumeSeparatorChar;
        var hasFullyQualifiedPath = Path.IsPathFullyQualified(normalizedPath);
        var candidate = hasDriveQualifiedPath || hasFullyQualifiedPath
            ? Path.GetFullPath(normalizedPath)
            : Path.GetFullPath(Path.Combine(fullRoot, normalizedPath.TrimStart(Path.DirectorySeparatorChar)));

        if (!IsWithinRoot(candidate, fullRoot))
            throw new IOException("Invalid path: out of daemon root.");

        EnsureExistingPathDoesNotContainReparsePoint(candidate, fullRoot);
        return candidate;
    }

    private async Task<Result<UploadSession, DaemonError>> OpenUploadCoreAsync(
        string path,
        long length,
        string? sha256,
        string? legacySha1,
        CancellationToken cancellationToken)
    {
        if (IsStopped)
            return Err<UploadSession>(CoordinatorStopped());

        await _sessionAdmissionGate.WaitAsync(cancellationToken);
        try
        {
            if (IsStopped)
                return Err<UploadSession>(CoordinatorStopped());

            return await OpenUploadCoreLockedAsync(path, length, sha256, legacySha1, cancellationToken);
        }
        finally
        {
            _sessionAdmissionGate.Release();
        }
    }

    private async Task<Result<UploadSession, DaemonError>> OpenUploadCoreLockedAsync(
        string path,
        long length,
        string? sha256,
        string? legacySha1,
        CancellationToken cancellationToken)
    {
        if (length < 0)
            return Err<UploadSession>(new ValidationDaemonError("file.upload.length.invalid", "The upload length cannot be negative."));

        if (sha256 is not null && !IsHexHash(sha256, 32))
            return Err<UploadSession>(new ValidationDaemonError("file.upload.sha256.invalid", "The upload SHA-256 hash must be 64 hexadecimal characters."));

        if (legacySha1 is not null && !IsHexHash(legacySha1, 20))
            return Err<UploadSession>(new ValidationDaemonError("file.upload.sha1.invalid", "The upload SHA-1 hash must be 40 hexadecimal characters."));

        string targetPath;
        try
        {
            targetPath = ResolveAndValidatePath(path);
        }
        catch (Exception exception)
        {
            return Err<UploadSession>(MapException(exception));
        }

        var sessionId = Guid.NewGuid();
        if (!_uploadLeases.TryAdd(targetPath, sessionId))
            return Err<UploadSession>(new ConflictDaemonError("file.upload.active", "An upload is already active for the target path."));

        string? stagingPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("The upload target has no parent directory.");

            Directory.CreateDirectory(directory);
            stagingPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{sessionId:N}.upload.tmp");
            await using (var staging = new FileStream(
                             stagingPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                staging.SetLength(length);
                await staging.FlushAsync(cancellationToken);
            }

            if (_onUploadStagingCreatedAsync is not null)
                await _onUploadStagingCreatedAsync(stagingPath);

            var stream = new FileStream(
                stagingPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            var expiresAt = _timeProvider.GetUtcNow() + SessionLifetime;
            var session = new FileUploadInfo(targetPath, stagingPath, length, sha256, legacySha1, stream, expiresAt);
            if (!_uploadSessions.TryAdd(sessionId, session))
                throw new IOException("The upload session could not be registered.");

            return Ok(new UploadSession(sessionId, MaxChunkSize, expiresAt));
        }
        catch (OperationCanceledException)
        {
            TryDeleteUploadStaging(stagingPath);
            ReleaseUploadLease(targetPath);
            throw;
        }
        catch (Exception exception)
        {
            TryDeleteUploadStaging(stagingPath);
            ReleaseUploadLease(targetPath);
            return Err<UploadSession>(MapException(exception));
        }
    }

    private async Task<Result<UploadWriteProgress, DaemonError>> WriteUploadChunkCoreAsync(
        Guid sessionId,
        long offset,
        ImmutableArray<byte> data,
        CancellationToken cancellationToken)
    {
        if (data.IsDefault)
            return Err<UploadWriteProgress>(new ValidationDaemonError("file.chunk.data.invalid", "The upload chunk data is required."));

        if (data.Length > MaxChunkSize)
            return Err<UploadWriteProgress>(new ValidationDaemonError("file.chunk.size.invalid", "The upload chunk exceeds the maximum size."));

        if (!_uploadSessions.TryGetValue(sessionId, out var session))
            return Err<UploadWriteProgress>(SessionNotFound(sessionId));

        var cleanup = false;
        var entered = false;
        try
        {
            await session.Gate.WaitAsync(cancellationToken);
            entered = true;
            if (session.IsClosed || !_uploadSessions.ContainsKey(sessionId))
                return Err<UploadWriteProgress>(SessionNotFound(sessionId));

            if (session.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                session.IsClosed = true;
                _uploadSessions.TryRemove(sessionId, out _);
                cleanup = true;
                return Err<UploadWriteProgress>(SessionExpired(sessionId));
            }

            if (offset != session.NextExpectedOffset)
            {
                session.IsClosed = true;
                _uploadSessions.TryRemove(sessionId, out _);
                cleanup = true;
                return Err<UploadWriteProgress>(new ValidationDaemonError("file.chunk.offset.invalid", "The upload chunk offset must be the next expected offset."));
            }

            if (offset < 0 || data.Length > session.Size - offset)
                return Err<UploadWriteProgress>(new ValidationDaemonError("file.chunk.offset.invalid", "The upload chunk is outside the declared file bounds."));

            await RandomAccess.WriteAsync(session.Stream.SafeFileHandle, data.AsMemory(), offset, cancellationToken);
            session.NextExpectedOffset += data.Length;
            return Ok(new UploadWriteProgress(session.IsComplete, session.Received));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Err<UploadWriteProgress>(MapException(exception));
        }
        finally
        {
            if (entered)
                session.Gate.Release();
            if (cleanup)
                await DisposeUploadAsync(session);
        }
    }

    private async Task<Result<Unit, DaemonError>> CloseUploadCoreAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!_uploadSessions.TryGetValue(sessionId, out var session))
            return Err<Unit>(SessionNotFound(sessionId));

        var cleanup = false;
        var entered = false;
        try
        {
            await session.Gate.WaitAsync(cancellationToken);
            entered = true;
            if (session.IsClosed || !_uploadSessions.ContainsKey(sessionId))
                return Err<Unit>(SessionNotFound(sessionId));

            if (session.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                session.IsClosed = true;
                _uploadSessions.TryRemove(sessionId, out _);
                cleanup = true;
                return Err<Unit>(SessionExpired(sessionId));
            }

            if (!session.IsComplete)
                return Err<Unit>(new ConflictDaemonError("file.upload.incomplete", "The upload has not received its declared content."));

            await session.Stream.FlushAsync(cancellationToken);
            session.Stream.Flush(flushToDisk: true);
            session.Stream.Position = 0;
            var actualSha256 = await _hashAsync(session.Stream, HashAlgorithmName.SHA256, cancellationToken);
            if (session.Sha256 is not null && !actualSha256.Equals(session.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                session.IsClosed = true;
                _uploadSessions.TryRemove(sessionId, out _);
                cleanup = true;
                return Err<Unit>(new ValidationDaemonError("file.upload.hash_mismatch", "The uploaded file SHA-256 hash does not match the declared hash."));
            }

            if (session.LegacySha1 is not null)
            {
                session.Stream.Position = 0;
                var actualSha1 = await _hashAsync(session.Stream, HashAlgorithmName.SHA1, cancellationToken);
                if (!actualSha1.Equals(session.LegacySha1, StringComparison.OrdinalIgnoreCase))
                {
                    session.IsClosed = true;
                    _uploadSessions.TryRemove(sessionId, out _);
                    cleanup = true;
                    return Err<Unit>(new ValidationDaemonError("file.upload.hash_mismatch", "The uploaded file SHA-1 hash does not match the declared hash."));
                }
            }

            await session.Stream.DisposeAsync();
            File.Move(session.StagingPath, session.Path, overwrite: true);
            session.IsClosed = true;
            _uploadSessions.TryRemove(sessionId, out _);
            _uploadLeases.TryRemove(session.Path, out _);
            return Ok(Unit.Default);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.IsClosed = true;
            _uploadSessions.TryRemove(sessionId, out _);
            cleanup = true;
            return Err<Unit>(MapException(exception));
        }
        finally
        {
            if (entered)
                session.Gate.Release();
            if (cleanup)
                await DisposeUploadAsync(session);
        }
    }

    private async Task<Result<FileDownloadInfo, DaemonError>> OpenDownloadCoreAsync(
        string path,
        bool includeLegacySha1,
        CancellationToken cancellationToken)
    {
        if (IsStopped)
            return Err<FileDownloadInfo>(CoordinatorStopped());

        await _sessionAdmissionGate.WaitAsync(cancellationToken);
        try
        {
            if (IsStopped)
                return Err<FileDownloadInfo>(CoordinatorStopped());

            return await OpenDownloadCoreLockedAsync(path, includeLegacySha1, cancellationToken);
        }
        finally
        {
            _sessionAdmissionGate.Release();
        }
    }

    private async Task<Result<FileDownloadInfo, DaemonError>> OpenDownloadCoreLockedAsync(
        string path,
        bool includeLegacySha1,
        CancellationToken cancellationToken)
    {
        string resolvedPath;
        try
        {
            resolvedPath = ResolveAndValidatePath(path);
            if (!File.Exists(resolvedPath))
                throw new FileNotFoundException("File not found.", resolvedPath);
        }
        catch (Exception exception)
        {
            return Err<FileDownloadInfo>(MapException(exception));
        }

        if (_downloadSessionLimit is { } limit && _downloadSessions.Count(pair =>
                string.Equals(pair.Value.Path, resolvedPath, GetPathComparison())) >= limit)
            return Err<FileDownloadInfo>(new ConflictDaemonError("file.download.limit", "The file download session limit has been reached."));

        FileStream? stream = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream = new FileStream(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var length = stream.Length;
            var sha256 = await _hashAsync(stream, HashAlgorithmName.SHA256, cancellationToken);
            string? sha1 = null;
            if (includeLegacySha1)
            {
                stream.Position = 0;
                sha1 = await _hashAsync(stream, HashAlgorithmName.SHA1, cancellationToken);
            }

            stream.Position = 0;
            var sessionId = Guid.NewGuid();
            var session = new FileDownloadInfo(
                sessionId,
                resolvedPath,
                length,
                sha256,
                sha1,
                stream,
                _timeProvider.GetUtcNow() + SessionLifetime);
            if (!_downloadSessions.TryAdd(sessionId, session))
            {
                await session.DisposeAsync();
                stream = null;
                return Err<FileDownloadInfo>(new InternalDaemonError("file.download.session_create_failed", "The download session could not be registered."));
            }

            stream = null;
            return Ok(session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Err<FileDownloadInfo>(MapException(exception));
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    private async Task<Result<DownloadChunk, DaemonError>> ReadDownloadCoreAsync(
        Guid sessionId,
        long offset,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        if (maximumLength <= 0 || maximumLength > MaxChunkSize)
            return Err<DownloadChunk>(new ValidationDaemonError("file.chunk.size.invalid", "The requested chunk size is invalid."));

        if (!_downloadSessions.TryGetValue(sessionId, out var session))
            return Err<DownloadChunk>(SessionNotFound(sessionId));

        var expired = false;
        var entered = false;
        try
        {
            await session.Gate.WaitAsync(cancellationToken);
            entered = true;
            if (session.IsClosed || !_downloadSessions.ContainsKey(sessionId))
                return Err<DownloadChunk>(SessionNotFound(sessionId));

            if (session.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                session.IsClosed = true;
                _downloadSessions.TryRemove(sessionId, out _);
                expired = true;
                return Err<DownloadChunk>(SessionExpired(sessionId));
            }

            if (offset < 0 || offset > session.Size)
                return Err<DownloadChunk>(new ValidationDaemonError("file.chunk.offset.invalid", "The requested offset is outside the file bounds."));

            var remaining = session.Size - offset;
            var requested = (int)Math.Min(remaining, maximumLength);
            if (requested == 0)
                return Ok(new DownloadChunk(offset, ImmutableArray<byte>.Empty, IsFinal: offset == session.Size));

            var buffer = new byte[requested];
            var read = await RandomAccess.ReadAsync(session.Stream.SafeFileHandle, buffer.AsMemory(), offset, cancellationToken);
            var data = read == requested
                ? ImmutableArray.CreateRange(buffer)
                : ImmutableArray.CreateRange(buffer.AsSpan(0, read).ToArray());
            return Ok(new DownloadChunk(offset, data, IsFinal: offset + read == session.Size));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Err<DownloadChunk>(MapException(exception));
        }
        finally
        {
            if (entered)
                session.Gate.Release();
            if (expired)
                await DisposeSessionAsync(session);
        }
    }

    private Task<Result<Unit, DaemonError>> RenameAsync(PathRenameRequest request, bool isDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(request.NewName)
                || Path.IsPathRooted(request.NewName)
                || !string.Equals(Path.GetFileName(request.NewName), request.NewName, StringComparison.Ordinal))
                throw new ArgumentException("The new name must be a single file-system name.", nameof(request));

            var path = ResolveAndValidatePath(request.Path);
            var parent = Path.GetDirectoryName(path) ?? throw new IOException("The path has no parent directory.");
            var target = ResolveAndValidatePath(Path.Combine(parent, request.NewName));
            if (isDirectory)
            {
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(path);
                Directory.Move(path, target);
            }
            else
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("File not found.", path);
                File.Move(path, target);
            }

            return Unit.Default;
        });
    }

    private Task<Result<Unit, DaemonError>> TransferAsync(
        PathTransferRequest request,
        bool isDirectory,
        bool copy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveAndValidatePath(request.SourcePath);
            var destination = ResolveAndValidatePath(request.DestinationPath);
            if (isDirectory)
            {
                if (!Directory.Exists(source))
                    throw new DirectoryNotFoundException(source);
                if (copy)
                {
                    EnsureDirectoryDestinationIsOutsideSource(source, destination);
                    CopyDirectory(source, destination);
                }
                else
                {
                    Directory.Move(source, destination);
                }
            }
            else
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("File not found.", source);
                if (copy)
                    File.Copy(source, destination, overwrite: true);
                else
                    File.Move(source, destination);
            }

            return Unit.Default;
        });
    }

    private async Task<bool> TryExpireUploadAsync(Guid sessionId, FileUploadInfo session)
    {
        await session.Gate.WaitAsync();
        try
        {
            if (session.IsClosed || session.ExpiresAt > _timeProvider.GetUtcNow() || !_uploadSessions.TryRemove(sessionId, out _))
                return false;

            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeUploadAsync(session);
        return true;
    }

    private async Task<bool> TryExpireDownloadAsync(Guid sessionId, FileDownloadInfo session)
    {
        await session.Gate.WaitAsync();
        try
        {
            if (session.IsClosed || session.ExpiresAt > _timeProvider.GetUtcNow() || !_downloadSessions.TryRemove(sessionId, out _))
                return false;

            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeSessionAsync(session);
        return true;
    }

    private async Task DisposeUploadLockedAsync(FileUploadInfo session)
    {
        await session.Gate.WaitAsync();
        try
        {
            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeUploadAsync(session);
    }

    private async Task DisposeSessionLockedAsync(FileSessionInfo session)
    {
        await session.Gate.WaitAsync();
        try
        {
            session.IsClosed = true;
        }
        finally
        {
            session.Gate.Release();
        }

        await DisposeSessionAsync(session);
    }

    private async Task DisposeUploadAsync(FileUploadInfo session)
    {
        session.IsClosed = true;
        try
        {
            await DisposeSessionAsync(session);
        }
        finally
        {
            TryDeleteUploadStaging(session.StagingPath);
            ReleaseUploadLease(session.Path);
        }
    }

    private static async Task DisposeSessionAsync(FileSessionInfo session)
    {
        try
        {
            await session.Stream.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[FileSessionCoordinator] Failed to dispose file session stream for {Path}", session.Path);
        }
    }

    private void ScheduleCleanup()
    {
        _ = CleanupExpiredAsync().ContinueWith(
            task => Log.Error(task.Exception, "[FileSessionCoordinator] Session cleanup failed."),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void CopyDirectory(string source, string destination)
    {
        EnsureNotReparsePoint(source);
        EnsureExistingCopyDestinationIsNotReparsePoint(destination);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            EnsureNotReparsePoint(file);
            var destinationFile = Path.Combine(destination, Path.GetFileName(file));
            EnsureExistingCopyDestinationIsNotReparsePoint(destinationFile);
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void EnsureExistingCopyDestinationIsNotReparsePoint(string path)
    {
        if (Path.Exists(path))
            EnsureNotReparsePoint(path);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Invalid path: reparse-point entries are not permitted during directory copy.");
    }

    private static void EnsureDirectoryDestinationIsOutsideSource(string source, string destination)
    {
        var normalizedSource = NormalizeDirectoryPath(source);
        var normalizedDestination = NormalizeDirectoryPath(destination);
        if (IsWithinRoot(normalizedDestination, normalizedSource))
            throw new IOException("A directory cannot be copied into itself.");
    }

    private static bool IsWithinRoot(string candidate, string root)
    {
        var normalizedRoot = NormalizeDirectoryPath(root);
        if (string.Equals(candidate, normalizedRoot, GetPathComparison()))
            return true;

        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            || normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, GetPathComparison());
    }

    private static void EnsureExistingPathDoesNotContainReparsePoint(string candidate, string root)
    {
        if (string.Equals(candidate, root, GetPathComparison()))
            return;

        var lastExistingPath = candidate;
        while (!Path.Exists(lastExistingPath))
        {
            if (string.Equals(lastExistingPath, root, GetPathComparison()))
                return;

            var parent = Path.GetDirectoryName(lastExistingPath);
            if (string.IsNullOrEmpty(parent) || !IsWithinRoot(parent, root))
                throw new IOException("Invalid path: out of daemon root.");

            lastExistingPath = parent;
        }

        var existingComponents = new Stack<string>();
        while (!string.Equals(lastExistingPath, root, GetPathComparison()))
        {
            existingComponents.Push(lastExistingPath);
            var parent = Path.GetDirectoryName(lastExistingPath);
            if (string.IsNullOrEmpty(parent) || !IsWithinRoot(parent, root))
                throw new IOException("Invalid path: out of daemon root.");

            lastExistingPath = parent;
        }

        while (existingComponents.TryPop(out var component))
        {
            if (File.GetAttributes(component).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Invalid path: reparse points are not permitted below daemon root.");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath, root, GetPathComparison()))
            return fullPath;

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static async Task<string> GetHashAsync(Stream stream, HashAlgorithmName algorithm, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            hash.AppendData(buffer, 0, read);

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? NormalizeOptionalSha1(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsHexHash(string value, int byteLength)
    {
        if (value.Length != byteLength * 2)
            return false;

        return value.All(Uri.IsHexDigit);
    }

    private static FileMetadata ToFileMetadata(FileInfo info)
    {
        return new FileMetadata(
            new DateTimeOffset(info.CreationTimeUtc),
            info.Attributes.HasFlag(FileAttributes.Hidden),
            new DateTimeOffset(info.LastAccessTimeUtc),
            new DateTimeOffset(info.LastWriteTimeUtc),
            info.IsReadOnly,
            info.Length);
    }

    private static DirectoryMetadata ToDirectoryMetadata(DirectoryInfo info)
    {
        return new DirectoryMetadata(
            new DateTimeOffset(info.CreationTimeUtc),
            info.Attributes.HasFlag(FileAttributes.Hidden),
            new DateTimeOffset(info.LastAccessTimeUtc),
            new DateTimeOffset(info.LastWriteTimeUtc));
    }

    private static string? ToDaemonRelativePath(string? path)
    {
        if (path is null || string.Equals(path, GetFullRoot(), GetPathComparison()))
            return null;

        return Path.GetRelativePath(GetFullRoot(), path);
    }

    private static string GetFullRoot()
    {
        return NormalizeDirectoryPath(FileManager.Root);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    private void TryDeleteUploadStaging(string? path)
    {
        if (path is null)
            return;

        try
        {
            _deleteUploadStaging(path);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "[FileSessionCoordinator] Failed to remove upload staging file {Path}", path);
        }
    }

    private void ReleaseUploadLease(string targetPath)
    {
        _uploadLeases.TryRemove(targetPath, out _);
    }

    private static DaemonError MapException(Exception exception)
    {
        Log.Debug(exception, "[FileSessionCoordinator] Mapping file operation exception to a public daemon error");
        return exception switch
        {
            FileNotFoundException or DirectoryNotFoundException =>
                new NotFoundDaemonError("file.not_found", "The requested file or directory was not found."),
            ArgumentException => new ValidationDaemonError("file.path.invalid", "The requested path is invalid."),
            IOException ioException when ioException.Message.StartsWith("Invalid path:", StringComparison.Ordinal) =>
                new ValidationDaemonError("file.path.invalid", "The requested path is invalid."),
            UnauthorizedAccessException =>
                new StorageDaemonError("file.access_denied", "The daemon cannot access the requested file system entry."),
            IOException => new StorageDaemonError("file.storage_failed", "The file operation could not be completed."),
            _ => new InternalDaemonError("file.unexpected", "The daemon could not complete the file operation.")
        };
    }

    private static DaemonError SessionNotFound(Guid sessionId)
    {
        return new NotFoundDaemonError("file.session.not_found", $"The file session '{sessionId}' was not found.");
    }

    private static DaemonError SessionExpired(Guid sessionId)
    {
        return new NotFoundDaemonError("file.session.expired", $"The file session '{sessionId}' expired.");
    }

    private static ConflictDaemonError CoordinatorStopped()
    {
        return new ConflictDaemonError("file.session.stopped", "The file session coordinator is stopping.");
    }

    private bool IsStopped => Volatile.Read(ref _stopped) != 0;

    internal static string GetLegacyUploadTargetPath(string? path)
    {
        return path ?? FileManager.UploadRoot;
    }

    private Task<Result<T, DaemonError>> ExecuteAsync<T>(Func<T> action)
        where T : notnull
    {
        if (IsStopped)
            return Task.FromResult(Err<T>(CoordinatorStopped()));

        try
        {
            return Task.FromResult(Ok(action()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(Err<T>(MapException(exception)));
        }
    }

    private static Result<T, DaemonError> Ok<T>(T value)
        where T : notnull
    {
        return RResult.Ok<T, DaemonError>(value);
    }

    private static Result<T, DaemonError> Err<T>(DaemonError error)
        where T : notnull
    {
        return RResult.Err<T, DaemonError>(error);
    }

    internal readonly record struct LegacyUploadSession(Guid SessionId);

    internal readonly record struct LegacyUploadWriteResult(bool Done, long Received);

    internal readonly record struct LegacyDownloadSession(Guid SessionId, long Size, string Sha1);

    private readonly record struct UploadWriteProgress(bool IsComplete, long Received);
}
