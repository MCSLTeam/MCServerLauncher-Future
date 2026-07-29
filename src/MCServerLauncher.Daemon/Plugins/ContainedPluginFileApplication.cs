using System.Collections.Concurrent;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Confines a plugin's file surface to an explicitly approved subtree and makes its download and
/// upload sessions owned, counted, and releasable.
/// </summary>
/// <remarks>
/// The daemon data root holds the audit log, plan store, automation policies, backup archives,
/// operation and monitoring history alongside instance data. <see cref="IFileApplication"/> is a
/// daemon-wide singleton rooted at <see cref="FileManager.Root"/>, so handing it to a plugin
/// unchanged grants read and write access to all of that control data, and a recursive delete of
/// the root itself. Permission checks alone do not help: they gate <em>which methods</em> a caller
/// may invoke, never <em>which paths</em> those methods may reach.
///
/// Containment is an allow-list rather than a deny-list of control directories, so a future store
/// added under the data root is out of reach by default instead of by remembering to exclude it.
///
/// Sessions are tracked here as well because the shared coordinator records no owner: without this
/// a plugin's sessions count against no budget, and any session id that leaked to a plugin could be
/// read or closed by it.
/// </remarks>
internal sealed class ContainedPluginFileApplication : IFileApplication
{
    /// <summary>
    /// Concurrent download plus upload sessions a single plugin may hold. Each open session pins a
    /// file handle and, for uploads, staging bytes, so an unbounded plugin can exhaust either. The
    /// bound is deliberately well below the per-connection budget interactive clients get.
    /// </summary>
    internal const int MaximumConcurrentSessions = 8;

    private readonly IFileApplication _inner;
    private readonly string _allowedRoot;
    private readonly ConcurrentDictionary<Guid, byte> _downloadSessions = new();
    private readonly ConcurrentDictionary<Guid, byte> _uploadSessions = new();

    internal ContainedPluginFileApplication(IFileApplication inner, string? allowedRoot = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _allowedRoot = Path.GetFullPath(allowedRoot ?? FileManager.InstancesRoot);
    }

    public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Contained<DirectoryDetails>(request.Path) ??
        _inner.GetDirectoryInfoAsync(request, cancellationToken);

    public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Contained<FileDetails>(request.Path) ?? _inner.GetFileInfoAsync(request, cancellationToken);

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var contained = Contained<DownloadSession>(request.Path);
        if (contained is not null)
            return await contained;
        if (TotalSessions >= MaximumConcurrentSessions)
            return Result.Err<DownloadSession, DaemonError>(SessionLimitReached());

        var result = await _inner.OpenDownloadAsync(request, cancellationToken);
        if (result.IsOk(out var session))
            _downloadSessions.TryAdd(session!.SessionId, 0);
        return result;
    }

    public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken) =>
        _downloadSessions.ContainsKey(request.SessionId)
            ? _inner.ReadDownloadChunkAsync(request, cancellationToken)
            : Task.FromResult(Result.Err<DownloadChunk, DaemonError>(UnownedSession()));

    public async Task<Result<Unit, DaemonError>> CloseDownloadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!_downloadSessions.TryRemove(sessionId, out _))
            return Result.Err<Unit, DaemonError>(UnownedSession());
        return await _inner.CloseDownloadAsync(sessionId, cancellationToken);
    }

    public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.Path) ?? _inner.CreateDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteFileAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.Path) ?? _inner.DeleteFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
        DeleteDirectoryRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.Path) ?? _inner.DeleteDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameFileAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.Path) ?? _inner.RenameFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.Path) ?? _inner.RenameDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.SourcePath, request.DestinationPath) ??
        _inner.MoveFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.SourcePath, request.DestinationPath) ??
        _inner.MoveDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.SourcePath, request.DestinationPath) ??
        _inner.CopyFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        Contained<Unit>(request.SourcePath, request.DestinationPath) ??
        _inner.CopyDirectoryAsync(request, cancellationToken);

    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var contained = Contained<UploadSession>(request.Path);
        if (contained is not null)
            return await contained;
        if (TotalSessions >= MaximumConcurrentSessions)
            return Result.Err<UploadSession, DaemonError>(SessionLimitReached());

        var result = await _inner.OpenUploadAsync(request, cancellationToken);
        if (result.IsOk(out var session))
            _uploadSessions.TryAdd(session!.SessionId, 0);
        return result;
    }

    public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken) =>
        _uploadSessions.ContainsKey(request.SessionId)
            ? _inner.WriteUploadChunkAsync(request, cancellationToken)
            : Task.FromResult(Result.Err<Unit, DaemonError>(UnownedSession()));

    public async Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!_uploadSessions.TryRemove(sessionId, out _))
            return Result.Err<Unit, DaemonError>(UnownedSession());
        return await _inner.CloseUploadAsync(sessionId, cancellationToken);
    }

    public async Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!_uploadSessions.TryRemove(sessionId, out _))
            return Result.Err<Unit, DaemonError>(UnownedSession());
        return await _inner.CancelUploadAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// Releases every session this plugin still holds. Called when the plugin stops so a crashed or
    /// sloppy plugin cannot leave file handles and staging bytes pinned until session expiry.
    /// </summary>
    internal async Task ReleaseSessionsAsync()
    {
        foreach (var sessionId in _downloadSessions.Keys)
        {
            if (_downloadSessions.TryRemove(sessionId, out _))
                await SuppressAsync(() => _inner.CloseDownloadAsync(sessionId, CancellationToken.None));
        }

        foreach (var sessionId in _uploadSessions.Keys)
        {
            if (_uploadSessions.TryRemove(sessionId, out _))
                await SuppressAsync(() => _inner.CancelUploadAsync(sessionId, CancellationToken.None));
        }
    }

    private int TotalSessions => _downloadSessions.Count + _uploadSessions.Count;

    /// <summary>
    /// Returns a faulted result when any supplied path escapes the allowed subtree, or
    /// <see langword="null"/> when every path is contained and the call may proceed.
    /// </summary>
    private Task<Result<T, DaemonError>>? Contained<T>(params string[] paths) where T : notnull
    {
        foreach (var path in paths)
        {
            if (!IsContained(path))
                return Task.FromResult(Result.Err<T, DaemonError>(OutOfContainment()));
        }

        return null;
    }

    private bool IsContained(string path)
    {
        string resolved;
        try
        {
            // Resolve through the same routine the coordinator uses so traversal, OS-absolute and
            // reparse-point forms are collapsed identically here and there. A path this rejects is
            // already out of the daemon root and equally out of containment.
            resolved = FileSessionCoordinator.ResolveAndValidatePath(path);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return false;
        }

        return IsStrictlyUnder(resolved, _allowedRoot);
    }

    /// <summary>
    /// Containment excludes the allowed root itself, so a plugin cannot recursively delete or
    /// rename the subtree it was granted access within.
    /// </summary>
    private static bool IsStrictlyUnder(string candidate, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (candidate.Length <= normalizedRoot.Length)
            return false;
        // Case sensitivity must follow the filesystem, not the host that wrote this code. The
        // daemon also ships linux-x64 and osx-*, where "Instances" and "instances" are different
        // directories; comparing case-insensitively there would admit a sibling of the approved
        // root as if it were inside it.
        if (!candidate.StartsWith(normalizedRoot, FileSessionCoordinator.GetPathComparison()))
            return false;
        return candidate[normalizedRoot.Length] is var separator &&
               (separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar);
    }

    private static async Task SuppressAsync(Func<Task<Result<Unit, DaemonError>>> release)
    {
        try
        {
            await release();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Releasing on plugin stop is best effort: a failure here must not block shutdown.
            _ = exception;
        }
    }

    private static PermissionDaemonError OutOfContainment() =>
        new("plugin.file.out_of_containment",
            "The path is outside the subtree this plugin may reach.");

    private static PermissionDaemonError UnownedSession() =>
        new("plugin.file.session_not_owned",
            "The file session was not opened by this plugin.");

    private static ConflictDaemonError SessionLimitReached() =>
        new("plugin.file.session_limit",
            $"A plugin may hold at most {MaximumConcurrentSessions} concurrent file sessions.");
}
