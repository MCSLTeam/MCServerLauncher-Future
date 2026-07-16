using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Rpc.Files;

internal sealed class V2FileSessionConnection : IProtocolFileSessionOperations, IV2ConnectionCleanup
{
    private const string UploadOpenMethod = "mcsl.file.upload.open";
    private const string UploadCloseMethod = "mcsl.file.upload.close";
    private const string UploadCancelMethod = "mcsl.file.upload.cancel";
    private const string DownloadOpenMethod = "mcsl.file.download.open";
    private const string DownloadReadMethod = "mcsl.file.download.read";
    private const string DownloadCloseMethod = "mcsl.file.download.close";

    private readonly object _gate = new();
    private readonly IFileApplication _application;
    private readonly V2ConnectionOwner _owner;
    private readonly TimeProvider _timeProvider;
    private readonly int _downloadSessionLimit;
    private readonly ImmutableArray<string> _permissionSnapshot;
    private readonly Dictionary<string, PermissionName> _permissions;
    private readonly Dictionary<Guid, Lease> _leases = [];
    private bool _closed;

    private V2FileSessionConnection(
        IFileApplication application,
        FrozenProtocolCatalog catalog,
        V2ConnectionOwner owner,
        TimeProvider timeProvider,
        int downloadSessionLimit)
    {
        _application = application;
        _owner = owner;
        _timeProvider = timeProvider;
        ArgumentOutOfRangeException.ThrowIfNegative(downloadSessionLimit);
        _downloadSessionLimit = downloadSessionLimit;
        _permissionSnapshot = owner.Permissions;
        _permissions = SessionMethods.ToDictionary(
            static method => method,
            method => catalog.TryGetRpc(new RpcMethod(method), out var binding)
                ? binding.Descriptor.Permission
                : throw new InvalidOperationException($"The frozen catalog is missing '{method}'."),
            StringComparer.Ordinal);
    }

    internal static Result<V2FileSessionConnection, DaemonError> Attach(
        IFileApplication application,
        FrozenProtocolCatalog catalog,
        V2ConnectionOwner owner,
        TimeProvider? timeProvider = null,
        int downloadSessionLimit = 0)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(owner);
        var connection = new V2FileSessionConnection(
            application,
            catalog,
            owner,
            timeProvider ?? TimeProvider.System,
            downloadSessionLimit);
        return owner.TryRegisterCleanup(connection)
            ? Result.Ok<V2FileSessionConnection, DaemonError>(connection)
            : Result.Err<V2FileSessionConnection, DaemonError>(ConnectionClosed());
    }


    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        if (PermissionError(UploadOpenMethod) is { } denied)
            return Result.Err<UploadSession, DaemonError>(denied);
        if (IsClosed())
            return Result.Err<UploadSession, DaemonError>(ConnectionClosed());

        var result = await _application.OpenUploadAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsErr(out _))
            return result;

        var session = result.Unwrap();
        var lease = new Lease(session.SessionId, SessionKind.Upload, _owner, _permissionSnapshot,
            _permissions[UploadOpenMethod], session.ExpiresAt, session.MaxChunkSize);
        var admissionError = Admit(lease);
        if (admissionError is null)
            return result;

        var compensationError = await CompensateRegistrationAsync(lease).ConfigureAwait(false);
        if (compensationError is not null)
            return Result.Err<UploadSession, DaemonError>(compensationError);
        return Result.Err<UploadSession, DaemonError>(admissionError);
    }

    public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Upload, UploadCloseMethod, cancelUpload: false, cancellationToken);

    public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Upload, UploadCancelMethod, cancelUpload: true, cancellationToken);

    internal async Task<Result<Unit, DaemonError>> ReceiveUploadChunkAsync(
        Guid sessionId,
        long offset,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var acquisition = AcquireUploadWrite(sessionId, payload.Length);
        if (acquisition.Expired is { } expired)
            await CleanupExpiredAsync(expired).ConfigureAwait(false);
        if (acquisition.Error is { } error)
            return Result.Err<Unit, DaemonError>(error);

        var lease = acquisition.Lease!;
        try
        {
            var owned = payload.ToArray();
            var request = new UploadChunkRequest(
                sessionId,
                offset,
                ImmutableCollectionsMarshal.AsImmutableArray(owned));
            var result = await _application.WriteUploadChunkAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsErr(out _) && IsUploadWriteTerminal(result.UnwrapErr()))
                await TerminateClaimedUploadAsync(lease).ConfigureAwait(false);
            else
                FinishOperation(lease, terminal: false);
            return result;
        }
        catch (OperationCanceledException)
        {
            FinishOperation(lease, terminal: false);
            throw;
        }
        catch
        {
            await TerminateClaimedUploadAsync(lease).ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<bool> TerminateUploadAsync(Guid sessionId)
    {
        Lease? lease;
        lock (_gate)
        {
            if (_closed || !_leases.TryGetValue(sessionId, out lease) ||
                lease.Kind != SessionKind.Upload || !ReferenceEquals(lease.Owner, _owner))
                return false;
            _leases.Remove(sessionId);
        }

        if (!await ClaimForCleanupAsync(lease).ConfigureAwait(false))
            return false;
        await CleanupDetachedUploadAsync(lease).ConfigureAwait(false);
        return true;
    }

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        if (PermissionError(DownloadOpenMethod) is { } denied)
            return Result.Err<DownloadSession, DaemonError>(denied);
        if (IsClosed())
            return Result.Err<DownloadSession, DaemonError>(ConnectionClosed());

        var result = await _application.OpenDownloadAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsErr(out _))
            return result;

        var session = result.Unwrap();
        var lease = new Lease(session.SessionId, SessionKind.Download, _owner, _permissionSnapshot,
            _permissions[DownloadOpenMethod], session.ExpiresAt, session.MaxChunkSize);
        var admissionError = Admit(lease);
        if (admissionError is null)
            return result;

        var compensationError = await CompensateRegistrationAsync(lease).ConfigureAwait(false);
        if (compensationError is not null)
            return Result.Err<DownloadSession, DaemonError>(compensationError);
        return Result.Err<DownloadSession, DaemonError>(admissionError);
    }

    public async Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken)
    {
        var acquisition = Acquire(request.SessionId, SessionKind.Download, DownloadReadMethod, forRead: true);
        if (acquisition.Expired is { } expired)
            await CleanupExpiredAsync(expired).ConfigureAwait(false);
        if (acquisition.Error is { } error)
            return Result.Err<DownloadChunk, DaemonError>(error);

        var lease = acquisition.Lease!;
        try
        {
            var result = await _application.ReadDownloadChunkAsync(request, cancellationToken).ConfigureAwait(false);
            FinishOperation(lease, result.IsErr(out _) && IsReadTerminal(result.UnwrapErr()));
            return result;
        }
        catch (OperationCanceledException)
        {
            FinishOperation(lease, terminal: false);
            throw;
        }
        catch
        {
            FinishOperation(lease, terminal: false);
            throw;
        }
    }

    public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Download, DownloadCloseMethod, cancelUpload: false, cancellationToken);

    public async ValueTask CleanupAsync(CancellationToken cancellationToken)
    {
        Lease[] leases;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            leases = _leases.Values.ToArray();
            _leases.Clear();
        }

        List<Exception>? failures = null;
        foreach (var lease in leases)
        {
            try
            {
                if (!await ClaimForCleanupAsync(lease).ConfigureAwait(false))
                    continue;
                var result = await CleanupLeaseAsync(lease).ConfigureAwait(false);
                if (result.IsErr(out _) && !IsExpectedCleanupTerminal(result.UnwrapErr()))
                {
                    var error = result.UnwrapErr();
                    (failures ??= []).Add(new InvalidOperationException($"{error.Code}: {error.Message}"));
                }
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more file sessions failed connection cleanup.", failures);
    }

    private async Task<Result<Unit, DaemonError>> EndAsync(
        Guid sessionId,
        SessionKind kind,
        string method,
        bool cancelUpload,
        CancellationToken cancellationToken)
    {
        var acquisition = Acquire(sessionId, kind, method, forRead: false);
        if (acquisition.Expired is { } expired)
            await CleanupExpiredAsync(expired).ConfigureAwait(false);
        if (acquisition.Error is { } error)
            return Result.Err<Unit, DaemonError>(error);

        var lease = acquisition.Lease!;
        try
        {
            Result<Unit, DaemonError> result = kind == SessionKind.Download
                ? await _application.CloseDownloadAsync(sessionId, cancellationToken).ConfigureAwait(false)
                : cancelUpload
                    ? await _application.CancelUploadAsync(sessionId, cancellationToken).ConfigureAwait(false)
                    : await _application.CloseUploadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var terminal = result.IsOk(out _) || result.IsErr(out _) &&
                IsEndTerminal(kind, cancelUpload, result.UnwrapErr());
            FinishOperation(lease, terminal);
            return result;
        }
        catch (OperationCanceledException)
        {
            FinishOperation(lease, terminal: false);
            throw;
        }
        catch
        {
            FinishOperation(lease, terminal: true);
            throw;
        }
    }

    private Acquisition Acquire(Guid sessionId, SessionKind kind, string method, bool forRead)
    {
        lock (_gate)
        {
            if (_closed || !_leases.TryGetValue(sessionId, out var lease) || lease.Kind != kind || !ReferenceEquals(lease.Owner, _owner))
                return new(null, NotFound(sessionId), null);
            if (lease.State != LeaseState.Active)
            {
                var conflict = lease.State switch
                {
                    LeaseState.Reading when forRead => new ConflictDaemonError(
                        "file.download.read_in_flight", "A download read is already in flight."),
                    LeaseState.Writing => new ConflictDaemonError(
                        "file.upload.chunk_in_flight", "An upload chunk is already in flight."),
                    _ => new ConflictDaemonError("file.session.busy", "The file session is busy.")
                };
                return new(null, conflict, null);
            }
            if (_timeProvider.GetUtcNow() >= lease.ExpiresAt)
            {
                _leases.Remove(sessionId);
                lease.AuthoritativeOpen = false;
                return new(null, NotFound(sessionId), lease);
            }
            if (!HasPermission(lease.PermissionSnapshot, _permissions[method]) || !HasPermission(lease.PermissionSnapshot, lease.RequiredPermission))
                return new(null, PermissionDenied(_permissions[method]), null);
            lease.State = forRead ? LeaseState.Reading : LeaseState.Ending;
            lease.Idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(lease, null, null);
        }
    }

    private Acquisition AcquireUploadWrite(Guid sessionId, int payloadLength)
    {
        lock (_gate)
        {
            if (_closed || !_leases.TryGetValue(sessionId, out var lease) ||
                lease.Kind != SessionKind.Upload || !ReferenceEquals(lease.Owner, _owner))
                return new(null, NotFound(sessionId), null);
            if (lease.State != LeaseState.Active)
                return new(null, new ConflictDaemonError(
                    lease.State == LeaseState.Writing ? "file.upload.chunk_in_flight" : "file.session.busy",
                    lease.State == LeaseState.Writing ? "An upload chunk is already in flight." : "The file session is busy."), null);
            if (_timeProvider.GetUtcNow() >= lease.ExpiresAt)
            {
                _leases.Remove(sessionId);
                lease.AuthoritativeOpen = false;
                return new(null, NotFound(sessionId), lease);
            }
            if (!HasPermission(lease.PermissionSnapshot, lease.RequiredPermission))
                return new(null, PermissionDenied(lease.RequiredPermission), null);
            if (payloadLength > lease.MaxChunkSize)
            {
                _leases.Remove(sessionId);
                lease.AuthoritativeOpen = false;
                return new(null, new ValidationDaemonError(
                    "file.chunk.too_large", "The upload chunk exceeds the session maximum chunk size."), lease);
            }
            lease.State = LeaseState.Writing;
            lease.Idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(lease, null, null);
        }
    }

    private DaemonError? Admit(Lease lease)
    {
        lock (_gate)
        {
            if (_closed || _owner.State is not (V2ConnectionState.Created or V2ConnectionState.Running))
                return ConnectionClosed();
            if (_timeProvider.GetUtcNow() >= lease.ExpiresAt)
                return new InternalDaemonError("file.session.registration_failed", "The opened file session expired before registration.");
            if (lease.Kind == SessionKind.Download &&
                _downloadSessionLimit > 0 &&
                _leases.Values.Count(static existing => existing.Kind == SessionKind.Download) >= _downloadSessionLimit)
            {
                return new ConflictDaemonError(
                    "file.download.limit",
                    "The file download session limit has been reached.");
            }
            if (!_leases.TryAdd(lease.SessionId, lease))
                return new InternalDaemonError("file.session.registration_failed", "The opened file session identifier is already registered.");
            if (_owner.State is not (V2ConnectionState.Created or V2ConnectionState.Running))
            {
                _leases.Remove(lease.SessionId);
                return ConnectionClosed();
            }
            return null;
        }
    }

    private bool IsClosed()
    {
        lock (_gate)
            return _closed || _owner.State is not (V2ConnectionState.Created or V2ConnectionState.Running);
    }

    private void FinishOperation(Lease lease, bool terminal)
    {
        TaskCompletionSource idle;
        lock (_gate)
        {
            if (terminal)
            {
                lease.AuthoritativeOpen = false;
                if (_leases.TryGetValue(lease.SessionId, out var current) && ReferenceEquals(current, lease))
                    _leases.Remove(lease.SessionId);
            }
            lease.State = LeaseState.Active;
            idle = lease.Idle;
        }
        idle.TrySetResult();
    }

    private async Task<DaemonError?> CompensateRegistrationAsync(Lease lease)
    {
        var result = await CleanupLeaseAsync(lease).ConfigureAwait(false);
        if (result.IsOk(out _) || result.IsErr(out _) && IsExpectedCleanupTerminal(result.UnwrapErr()))
            return null;
        var error = result.UnwrapErr();
        return new InternalDaemonError(
            "file.session.compensation_failed",
            $"Registration compensation failed: {error.Code}: {error.Message}");
    }

    private async Task<bool> ClaimForCleanupAsync(Lease lease)
    {
        while (true)
        {
            Task? wait = null;
            lock (_gate)
            {
                if (lease.State == LeaseState.Active)
                {
                    if (!lease.AuthoritativeOpen)
                        return false;
                    lease.AuthoritativeOpen = false;
                    lease.State = LeaseState.Ending;
                    return true;
                }
                wait = lease.Idle.Task;
            }
            await wait.ConfigureAwait(false);
        }
    }

    private Task<Result<Unit, DaemonError>> CleanupLeaseAsync(Lease lease) => lease.Kind == SessionKind.Upload
        ? _application.CancelUploadAsync(lease.SessionId, CancellationToken.None)
        : _application.CloseDownloadAsync(lease.SessionId, CancellationToken.None);

    private async Task CleanupExpiredAsync(Lease lease)
    {
        try
        {
            var result = await CleanupLeaseAsync(lease).ConfigureAwait(false);
            if (result.IsErr(out _) && !IsExpectedCleanupTerminal(result.UnwrapErr()))
            {
                var error = result.UnwrapErr();
                Log.Warning(
                    "Expired V2 file session {SessionId} cleanup failed with {ErrorCode}: {ErrorMessage}",
                    lease.SessionId,
                    error.Code,
                    error.Message);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Expired V2 file session {SessionId} cleanup threw.", lease.SessionId);
        }
    }

    private async Task TerminateClaimedUploadAsync(Lease lease)
    {
        lock (_gate)
        {
            lease.AuthoritativeOpen = false;
            if (_leases.TryGetValue(lease.SessionId, out var current) && ReferenceEquals(current, lease))
                _leases.Remove(lease.SessionId);
        }
        await CleanupDetachedUploadAsync(lease).ConfigureAwait(false);
        FinishOperation(lease, terminal: true);
    }

    private async Task CleanupDetachedUploadAsync(Lease lease)
    {
        try
        {
            var cleanup = await _application.CancelUploadAsync(lease.SessionId, CancellationToken.None).ConfigureAwait(false);
            if (cleanup.IsErr(out _) && !IsExpectedCleanupTerminal(cleanup.UnwrapErr()))
                Log.Warning("Terminated V2 upload {SessionId} cleanup failed with {ErrorCode}.", lease.SessionId, cleanup.UnwrapErr().Code);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Terminated V2 upload {SessionId} cleanup threw.", lease.SessionId);
        }
    }

    private DaemonError? PermissionError(string method) => HasPermission(_permissionSnapshot, _permissions[method])
        ? null
        : PermissionDenied(_permissions[method]);

    private static bool HasPermission(ImmutableArray<string> permissions, PermissionName required)
    {
        if (permissions.IsDefault)
            return false;
        try
        {
            return new Permissions(permissions.ToArray()).Matches(Permission.Of(required.Value));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsReadTerminal(DaemonError error) =>
        error.Code is "file.session.not_found" or "file.session.expired";

    private static bool IsUploadWriteTerminal(DaemonError error) => error.Code is
        "file.session.not_found" or "file.session.expired" or "file.chunk.offset.invalid" or
        "file.chunk.too_large" or "file.upload.size_exceeded" ||
        error.Kind is DaemonErrorKind.Storage or DaemonErrorKind.Internal;

    private static bool IsEndTerminal(SessionKind kind, bool cancelUpload, DaemonError error) =>
        kind == SessionKind.Download || cancelUpload || error.Code != "file.upload.incomplete";

    private static bool IsExpectedCleanupTerminal(DaemonError error) =>
        error.Code is "file.session.not_found" or "file.session.expired";

    private static NotFoundDaemonError NotFound(Guid sessionId) =>
        new("file.session.not_found", $"The file session '{sessionId}' was not found.");

    private static PermissionDaemonError PermissionDenied(PermissionName permission) =>
        new("auth.permission_denied", $"Permission '{permission.Value}' is required.");

    private static TransportDaemonError ConnectionClosed() =>
        new("file.session.connection_closed", "The connection is closed and cannot own file sessions.");

    private static readonly string[] SessionMethods =
    [
        UploadOpenMethod, UploadCloseMethod, UploadCancelMethod,
        DownloadOpenMethod, DownloadReadMethod, DownloadCloseMethod
    ];

    private enum SessionKind { Upload, Download }
    private enum LeaseState { Active, Reading, Writing, Ending }

    private sealed class Lease(
        Guid sessionId,
        SessionKind kind,
        V2ConnectionOwner owner,
        ImmutableArray<string> permissionSnapshot,
        PermissionName requiredPermission,
        DateTimeOffset expiresAt,
        int maxChunkSize)
    {
        internal Guid SessionId { get; } = sessionId;
        internal SessionKind Kind { get; } = kind;
        internal V2ConnectionOwner Owner { get; } = owner;
        internal ImmutableArray<string> PermissionSnapshot { get; } = permissionSnapshot;
        internal PermissionName RequiredPermission { get; } = requiredPermission;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal int MaxChunkSize { get; } = maxChunkSize;
        internal LeaseState State { get; set; }
        internal bool AuthoritativeOpen { get; set; } = true;
        internal TaskCompletionSource Idle { get; set; } = CompletedIdle();

        private static TaskCompletionSource CompletedIdle()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }
    }

    private sealed record Acquisition(Lease? Lease, DaemonError? Error, Lease? Expired);
}
