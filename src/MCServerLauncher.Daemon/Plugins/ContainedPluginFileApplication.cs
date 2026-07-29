using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Confines a plugin's file surface to the instance data a catalogued instance owns, and makes its
/// download and upload sessions owned, counted, and revocable.
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
/// The allow-list is not the instances root as a whole: the instance directory and the daemon's own
/// persisted instance files inside it are daemon-owned lifecycle state, not plugin payload. Removing
/// an instance checks running state, holds a mutation lease, updates the catalog and the published
/// snapshot, then stages the rename and the delete; a plugin renaming or deleting
/// <c>instances/{id}</c> through this facade would satisfy none of that. Creating
/// <c>instances/{anything}</c> would likewise mint a directory the catalog knows nothing about.
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

    /// <summary>
    /// How long revocation waits for in-flight opens before sweeping anyway. Short, because every
    /// caller of it is on a stop or shutdown path that is itself deadline-bounded, and a plugin
    /// holding the daemon open is the failure this whole revocation exists to prevent.
    /// </summary>
    private static readonly TimeSpan QuiesceDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The daemon's persistence writer replaces a config through <c>File.Replace</c> and keeps the
    /// previous revision beside it under this suffix, so it owns the backup as much as the original.
    /// </summary>
    private const string BackupSuffix = ".bak";

    /// <summary>
    /// Daemon-owned files inside an instance directory. They are refused for reads as well as
    /// writes: the persisted instance config is what <c>instance.query</c> answers from, and a
    /// plugin holding only the Low-risk <c>file.read</c> feature must not read it around that gate.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively on every platform, unlike the allowed root above. The two rules
    /// point opposite ways on purpose: an allow-list compared case-sensitively fails closed, since
    /// a case variant is either a different directory or simply refused, whereas a deny-list
    /// compared case-sensitively fails <em>open</em> — on Windows <c>DAEMON_INSTANCE.JSON</c> is the
    /// same file and would sail past an Ordinal set. Over-refusing a case variant on a
    /// case-sensitive filesystem costs a plugin nothing; under-refusing one costs the daemon its
    /// config.
    /// </remarks>
    private static readonly HashSet<string> DaemonOwnedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        InstanceConfig.FileName,
        InstanceConfig.FileName + BackupSuffix,
        InstanceInstallMetadataStore.FileName,
        InstanceInstallMetadataStore.FileName + BackupSuffix
    };

    private readonly IFileApplication _inner;
    private readonly IInstanceSnapshotSource? _instances;
    private readonly string _allowedRoot;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _downloadSessions = [];
    private readonly HashSet<Guid> _uploadSessions = [];
    private readonly TaskCompletionSource _quiesced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _reservations;
    private FacadeState _state = FacadeState.Active;

    internal ContainedPluginFileApplication(
        IFileApplication inner,
        IInstanceSnapshotSource? instances,
        string? allowedRoot = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        // A missing catalog is not a reason to widen: with nothing to confirm an instance exists,
        // every path fails the membership check and the whole surface is refused.
        _instances = instances;
        _allowedRoot = Path.GetFullPath(allowedRoot ?? FileManager.InstancesRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Refused<DirectoryDetails>(PathAccess.Readable, request.Path) ??
        _inner.GetDirectoryInfoAsync(request, cancellationToken);

    public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Refused<FileDetails>(PathAccess.Readable, request.Path) ??
        _inner.GetFileInfoAsync(request, cancellationToken);

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var refused = Refused<DownloadSession>(PathAccess.Readable, request.Path);
        if (refused is not null)
            return await refused;

        return await OpenSessionAsync(
            SessionKind.Download,
            () => _inner.OpenDownloadAsync(request, cancellationToken),
            static session => session.SessionId);
    }

    public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken) =>
        Owns(SessionKind.Download, request.SessionId)
            ? _inner.ReadDownloadChunkAsync(request, cancellationToken)
            : Task.FromResult(Result.Err<DownloadChunk, DaemonError>(UnownedSession()));

    public Task<Result<Unit, DaemonError>> CloseDownloadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndSessionAsync(
            SessionKind.Download,
            sessionId,
            () => _inner.CloseDownloadAsync(sessionId, cancellationToken));

    public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Refused<Unit>(PathAccess.Mutable, request.Path) ??
        _inner.CreateDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteFileAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        Refused<Unit>(PathAccess.Mutable, request.Path) ??
        _inner.DeleteFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
        DeleteDirectoryRequest request,
        CancellationToken cancellationToken) =>
        Refused<Unit>(PathAccess.Mutable, request.Path) ??
        _inner.DeleteDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameFileAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        RefusedRename<Unit>(request) ?? _inner.RenameFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        RefusedRename<Unit>(request) ?? _inner.RenameDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        RefusedTransfer<Unit>(PathAccess.Mutable, request) ??
        _inner.MoveFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        RefusedTransfer<Unit>(PathAccess.Mutable, request) ??
        _inner.MoveDirectoryAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        RefusedTransfer<Unit>(PathAccess.Readable, request) ??
        _inner.CopyFileAsync(request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        RefusedTransfer<Unit>(PathAccess.Readable, request) ??
        _inner.CopyDirectoryAsync(request, cancellationToken);

    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var refused = Refused<UploadSession>(PathAccess.Mutable, request.Path);
        if (refused is not null)
            return await refused;

        return await OpenSessionAsync(
            SessionKind.Upload,
            () => _inner.OpenUploadAsync(request, cancellationToken),
            static session => session.SessionId);
    }

    public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken) =>
        Owns(SessionKind.Upload, request.SessionId)
            ? _inner.WriteUploadChunkAsync(request, cancellationToken)
            : Task.FromResult(Result.Err<Unit, DaemonError>(UnownedSession()));

    public Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndSessionAsync(
            SessionKind.Upload,
            sessionId,
            () => _inner.CloseUploadAsync(sessionId, cancellationToken));

    public Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndSessionAsync(
            SessionKind.Upload,
            sessionId,
            () => _inner.CancelUploadAsync(sessionId, cancellationToken));

    /// <summary>
    /// Revokes the plugin's file capability and releases every session it still holds. Admission
    /// closes first, then in-flight opens are allowed to settle, and only then is the remainder
    /// swept - sweeping without those two steps races an open that is already past the admission
    /// check and would leave its session pinned until expiry.
    /// </summary>
    internal async Task ReleaseSessionsAsync()
    {
        lock (_gate)
        {
            if (_state == FacadeState.Active)
                _state = FacadeState.Revoking;
            SignalQuiescedWhenSettled();
        }

        // Every in-flight open holds a reservation, so a zero reservation count is what makes the
        // sweep total. An open that settles after this point sees the revoked state, closes its own
        // inner session and never registers it here.
        //
        // Bounded, because this runs on the plugin-stop and daemon-shutdown paths and a download
        // open hashes the whole file: one open on a stalled volume would otherwise hold shutdown
        // open indefinitely. Past the deadline the sweep proceeds anyway — a straggler that settles
        // later is refused registration and closes its own inner session, so nothing is stranded.
        try
        {
            await _quiesced.Task.WaitAsync(QuiesceDeadline).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        Guid[] downloads;
        Guid[] uploads;
        lock (_gate)
        {
            downloads = [.. _downloadSessions];
            _downloadSessions.Clear();
            uploads = [.. _uploadSessions];
            _uploadSessions.Clear();
        }

        try
        {
            foreach (var sessionId in downloads)
                await SuppressAsync(() => _inner.CloseDownloadAsync(sessionId, CancellationToken.None));

            foreach (var sessionId in uploads)
                await SuppressAsync(() => _inner.CancelUploadAsync(sessionId, CancellationToken.None));
        }
        finally
        {
            // The snapshot above already cleared the sets, so anything escaping the drain would
            // otherwise strand the facade in Revoking and refuse every later release attempt.
            lock (_gate)
            {
                _state = FacadeState.Revoked;
            }
        }
    }

    /// <summary>
    /// Reserves a slot, opens through the inner application, then converts the reservation into an
    /// owned session. Reserving first is what makes the cap hold: reading the count, awaiting an
    /// open and registering afterwards lets every concurrent caller observe the same headroom.
    /// </summary>
    private async Task<Result<TSession, DaemonError>> OpenSessionAsync<TSession>(
        SessionKind kind,
        Func<Task<Result<TSession, DaemonError>>> open,
        Func<TSession, Guid> identify)
        where TSession : notnull
    {
        var refusal = TryReserve();
        if (refusal is not null)
            return Result.Err<TSession, DaemonError>(refusal);

        Result<TSession, DaemonError> result;
        try
        {
            result = await open();
        }
        catch
        {
            ReleaseReservation();
            throw;
        }

        if (!result.IsOk(out var session))
        {
            ReleaseReservation();
            return result;
        }

        var sessionId = identify(session!);
        if (TryRegister(kind, sessionId))
            return result;

        // Revocation began while this open was in flight. The inner session exists but nothing owns
        // it any more, so close it here rather than leave it pinned until expiry.
        await SuppressAsync(() => TerminateAsync(kind, sessionId));
        return Result.Err<TSession, DaemonError>(SessionsRevoked());
    }

    /// <summary>
    /// Drops ownership only once the inner application has actually terminated the session, or has
    /// reported it already gone. A cancelled or otherwise failed call leaves the inner handle and
    /// any staging bytes alive, so freeing the slot there would let a plugin repeat the call and
    /// walk straight past its quota.
    /// </summary>
    private async Task<Result<Unit, DaemonError>> EndSessionAsync(
        SessionKind kind,
        Guid sessionId,
        Func<Task<Result<Unit, DaemonError>>> end)
    {
        if (!Owns(kind, sessionId))
            return Result.Err<Unit, DaemonError>(UnownedSession());

        var result = await end();
        if (result.IsErr(out var error))
        {
            // The coordinator answers NotFound for a session it already expired or closed. Holding
            // the slot for one of those would wedge it until the plugin stops.
            if (error!.Kind == DaemonErrorKind.NotFound)
                Forget(kind, sessionId);
            return result;
        }

        Forget(kind, sessionId);
        return result;
    }

    private Task<Result<Unit, DaemonError>> TerminateAsync(SessionKind kind, Guid sessionId) =>
        kind == SessionKind.Download
            ? _inner.CloseDownloadAsync(sessionId, CancellationToken.None)
            : _inner.CancelUploadAsync(sessionId, CancellationToken.None);

    private DaemonError? TryReserve()
    {
        lock (_gate)
        {
            if (_state != FacadeState.Active)
                return SessionsRevoked();
            if (_downloadSessions.Count + _uploadSessions.Count + _reservations >= MaximumConcurrentSessions)
                return SessionLimitReached();

            _reservations++;
            return null;
        }
    }

    private void ReleaseReservation()
    {
        lock (_gate)
        {
            _reservations--;
            SignalQuiescedWhenSettled();
        }
    }

    private bool TryRegister(SessionKind kind, Guid sessionId)
    {
        lock (_gate)
        {
            _reservations--;
            if (_state != FacadeState.Active)
            {
                SignalQuiescedWhenSettled();
                return false;
            }

            Sessions(kind).Add(sessionId);
            return true;
        }
    }

    private bool Owns(SessionKind kind, Guid sessionId)
    {
        lock (_gate)
        {
            return Sessions(kind).Contains(sessionId);
        }
    }

    private void Forget(SessionKind kind, Guid sessionId)
    {
        lock (_gate)
        {
            Sessions(kind).Remove(sessionId);
        }
    }

    private HashSet<Guid> Sessions(SessionKind kind) =>
        kind == SessionKind.Download ? _downloadSessions : _uploadSessions;

    private void SignalQuiescedWhenSettled()
    {
        if (_state != FacadeState.Active && _reservations == 0)
            _quiesced.TrySetResult();
    }

    /// <summary>
    /// Returns a faulted result when the path is not reachable at the requested access level, or
    /// <see langword="null"/> when the call may proceed.
    /// </summary>
    private Task<Result<T, DaemonError>>? Refused<T>(PathAccess access, string path) where T : notnull =>
        IsAllowed(path, access) ? null : Task.FromResult(Result.Err<T, DaemonError>(OutOfContainment()));

    /// <summary>
    /// A transfer's destination is always written, so it is checked as a mutation regardless of what
    /// the source end requires.
    /// </summary>
    private Task<Result<T, DaemonError>>? RefusedTransfer<T>(PathAccess source, PathTransferRequest request)
        where T : notnull =>
        IsAllowed(request.SourcePath, source) && IsAllowed(request.DestinationPath, PathAccess.Mutable)
            ? null
            : Task.FromResult(Result.Err<T, DaemonError>(OutOfContainment()));

    /// <summary>
    /// The coordinator requires a rename target to be a single file-system name resolved against the
    /// source's parent, so refusing a daemon-owned name here is what stops a rename from producing
    /// one where the mutation check on the source path cannot see it.
    /// </summary>
    private Task<Result<T, DaemonError>>? RefusedRename<T>(PathRenameRequest request) where T : notnull =>
        IsAllowed(request.Path, PathAccess.Mutable) && !IsDaemonOwnedFileName(request.NewName)
            ? null
            : Task.FromResult(Result.Err<T, DaemonError>(OutOfContainment()));

    private bool IsAllowed(string path, PathAccess access)
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

        var segments = SegmentsUnderAllowedRoot(resolved);
        // Zero segments is the instances root itself, which is never reachable at any access level.
        if (segments is not { Length: > 0 })
            return false;

        if (!IsCatalogedInstanceDirectory(segments[0]))
            return false;

        // The instance directory is the unit the manager creates, renames and deletes while holding
        // a mutation lease and updating the catalog; mutating it here would bypass all of that.
        if (access == PathAccess.Mutable && segments.Length == 1)
            return false;

        return !IsDaemonOwnedFileName(segments[^1]);
    }

    /// <summary>
    /// Splits the part of an already-resolved path that lies strictly under the allowed root, or
    /// returns <see langword="null"/> when the path is the root itself or outside it.
    /// </summary>
    /// <remarks>
    /// The comparison is <see cref="StringComparison.Ordinal"/> on every platform rather than the
    /// filesystem-dependent comparison the coordinator uses. The root is a fixed internal name the
    /// daemon writes itself, so demanding the exact spelling is fail-closed everywhere; Windows
    /// supports per-directory case sensitivity, where a case-insensitive compare would admit an
    /// "Instances" sibling as though it were the approved root.
    /// </remarks>
    private string[]? SegmentsUnderAllowedRoot(string resolved)
    {
        if (resolved.Length <= _allowedRoot.Length)
            return null;
        if (!resolved.StartsWith(_allowedRoot, StringComparison.Ordinal))
            return null;
        var separator = resolved[_allowedRoot.Length];
        if (separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar)
            return null;

        return resolved[(_allowedRoot.Length + 1)..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The daemon names an instance directory with the invariant "D" form of its identifier, so the
    /// spelling is required to match exactly: any other rendering of the same value would be a
    /// second directory the catalog does not describe.
    /// </summary>
    private bool IsCatalogedInstanceDirectory(string segment) =>
        Guid.TryParse(segment, out var instanceId) &&
        segment.Equals(instanceId.ToString(), StringComparison.Ordinal) &&
        _instances is not null &&
        _instances.TryGet(instanceId, out _);

    private static bool IsDaemonOwnedFileName(string name) =>
        DaemonOwnedFileNames.Contains(name) || IsAliasingName(name);

    /// <summary>
    /// Refuses names that Windows can resolve to a different file than they spell. A deny-list can
    /// only be as good as the assumption that one file has one name, and NTFS breaks that three
    /// ways: an alternate data stream suffix (<c>x.json::$DATA</c>) reaches the same file, an 8.3
    /// short name (<c>DAEMO~1.JSO</c>) is a second name for a long one, and trailing dots or spaces
    /// are stripped before the name reaches the filesystem.
    /// </summary>
    /// <remarks>
    /// This refuses the shapes rather than trying to resolve them, because resolving an alias back
    /// to its canonical name needs the file to exist and a Win32 call the BCL does not surface —
    /// and a check that only works on files that already exist is no guard at all. The cost is that
    /// a legitimate server file shaped like an 8.3 alias is refused; nothing in a Minecraft server
    /// tree is named that way, and the alternative is a boundary that holds only on Linux.
    /// </remarks>
    private static bool IsAliasingName(string name)
    {
        if (name.Contains(':'))
            return true;
        if (name.Length > 0 && (name[^1] == '.' || name[^1] == ' '))
            return true;

        // 8.3 shape: up to eight characters, a tilde, at least one digit, optional short extension.
        var tilde = name.IndexOf('~');
        if (tilde <= 0 || tilde > 8)
            return false;

        var afterTilde = name.AsSpan(tilde + 1);
        var digits = 0;
        while (digits < afterTilde.Length && char.IsAsciiDigit(afterTilde[digits]))
            digits++;
        if (digits == 0)
            return false;

        var remainder = afterTilde[digits..];
        return remainder.IsEmpty || (remainder[0] == '.' && remainder.Length <= 4);
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

    private static ConflictDaemonError SessionsRevoked() =>
        new("plugin.file.sessions_revoked",
            "The plugin's file sessions have been revoked.");

    private enum PathAccess
    {
        Readable,
        Mutable
    }

    private enum SessionKind
    {
        Download,
        Upload
    }

    private enum FacadeState
    {
        Active,
        Revoking,
        Revoked
    }
}
