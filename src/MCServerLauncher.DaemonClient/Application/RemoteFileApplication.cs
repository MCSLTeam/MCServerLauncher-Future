using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteFileApplication : IFileApplication
{
    private readonly object _ledgerGate = new();
    private readonly IRemoteApplicationInvoker _invoker;
    private readonly V2ClientConnectionOwner _owner;
    private readonly TimeProvider _timeProvider;
    private readonly RemoteFileApplicationTestHooks? _testHooks;
    private readonly Dictionary<Guid, Lease> _leases = [];
    private readonly Dictionary<V2ClientConnectionCore, CoreEntry> _cores =
        new(ReferenceEqualityComparer.Instance);
    private int _disposedDownloadHashCount;

    internal RemoteFileApplication(
        IRemoteApplicationInvoker invoker,
        V2ClientConnectionOwner owner)
        : this(invoker, owner, TimeProvider.System)
    {
    }

    internal RemoteFileApplication(
        IRemoteApplicationInvoker invoker,
        V2ClientConnectionOwner owner,
        TimeProvider timeProvider)
        : this(invoker, owner, timeProvider, testHooks: null)
    {
    }

    internal RemoteFileApplication(
        IRemoteApplicationInvoker invoker,
        V2ClientConnectionOwner owner,
        TimeProvider timeProvider,
        RemoteFileApplicationTestHooks? testHooks)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHooks = testHooks;
    }

    internal int LeaseCount
    {
        get
        {
            lock (_ledgerGate)
                return _leases.Count;
        }
    }

    internal int CoreEntryCount
    {
        get
        {
            lock (_ledgerGate)
                return _cores.Count;
        }
    }

    internal int DisposedDownloadHashCount => Volatile.Read(ref _disposedDownloadHashCount);

    internal Task WaitForCoreCleanupAsync(V2ClientConnectionCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        lock (_ledgerGate)
            return _cores.TryGetValue(core, out var entry) ? entry.CleanupCompleted.Task : Task.CompletedTask;
    }

    internal bool TryGetDownloadState(Guid sessionId, out long verifiedPrefix, out bool hashVerified)
    {
        lock (_ledgerGate)
        {
            if (_leases.TryGetValue(sessionId, out var lease) && lease is DownloadLease download)
            {
                verifiedPrefix = download.VerifiedPrefix;
                hashVerified = download.HashVerified;
                return true;
            }
        }

        verifiedPrefix = 0;
        hashVerified = false;
        return false;
    }

    public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetDirectoryInfo, request, cancellationToken);

    public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetFileInfo, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.CreateDirectory, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteFileAsync(
        PathRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.DeleteFile, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
        DeleteDirectoryRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.DeleteDirectory, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameFileAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.RenameFile, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.RenameDirectory, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.MoveFile, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.MoveDirectory, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.CopyFile, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.CopyDirectory, request, cancellationToken);

    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryBeginOpen(out var core, out var entry, out var readinessError))
            return Result.Err<UploadSession, DaemonError>(readinessError!);

        var operation = core.InvokeTracked(BuiltInProtocolDefinitions.OpenUpload, request, cancellationToken);
        Result<UploadSession, DaemonError> result;
        try
        {
            result = await operation.Completion.ConfigureAwait(false);
        }
        catch
        {
            FinishExceptionalOpen(entry, operation.Outcome);
            throw;
        }

        if (operation.Outcome.Disposition == V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse)
        {
            FinishOpenAdmission(entry);
            InvalidateEpoch(core, InvocationFailure(
                result,
                "protocol.file_session_open_ambiguous",
                "The upload open was admitted without an authoritative response."));
            return result;
        }

        if (result.IsErr(out _))
        {
            FinishOpenAdmission(entry);
            return result;
        }

        var session = result.Unwrap();
        if (ClassifyOpenBeforeValidation(entry, session.SessionId, out var earlyCollision) is { } earlyRejection)
        {
            FinishOpenAdmission(entry);
            await HandleOpenRejectionAsync(
                core,
                SessionKind.Upload,
                session.SessionId,
                earlyRejection,
                earlyCollision).ConfigureAwait(false);
            return Result.Err<UploadSession, DaemonError>(earlyRejection);
        }

        var beforeOpenCommit = _testHooks?.BeforeOpenCommit;
        if (beforeOpenCommit is not null)
        {
            try
            {
                await beforeOpenCommit(core).ConfigureAwait(false);
            }
            catch
            {
                FinishOpenAdmission(entry);
                InvalidateEpoch(core, OpenCommitInterrupted());
                throw;
            }
        }
        var validationError = ValidateUploadSession(request, session);
        var lease = validationError is null
            ? new UploadLease(session.SessionId, core, entry, request.Length, session.MaxChunkSize)
            : null;
        var rejection = CommitOpen(
            entry,
            session.SessionId,
            lease,
            validationError,
            out var collision);
        if (rejection is null)
            return result;

        await HandleOpenRejectionAsync(
            core,
            SessionKind.Upload,
            session.SessionId,
            rejection,
            collision).ConfigureAwait(false);

        return Result.Err<UploadSession, DaemonError>(rejection);
    }

    public async Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetLease(request.SessionId, SessionKind.Upload, out var found, out var lookupError))
            return Result.Err<Unit, DaemonError>(lookupError!);

        var lease = (UploadLease)found!;
        await lease.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var releaseGate = true;
        try
        {
            if (!IsCurrent(lease))
                return Result.Err<Unit, DaemonError>(SessionNotFound(request.SessionId));
            if (ValidateUploadChunk(lease, request) is { } validationError)
                return Result.Err<Unit, DaemonError>(validationError);

            var operation = lease.Core.SendUploadChunkTracked(request, lease.MaxChunkSize, cancellationToken);
            Result<Unit, DaemonError> result;
            try
            {
                result = await operation.Completion.ConfigureAwait(false);
            }
            catch
            {
                HandleAmbiguousLeaseOperation(lease, operation.Outcome, removeDownloadRegistration: false);
                throw;
            }

            switch (operation.Outcome.Disposition)
            {
                case V2ClientInvocationDisposition.NotAdmitted:
                    return result;
                case V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse:
                    DetachLease(lease);
                    InvalidateEpoch(lease.Core, InvocationFailure(
                        result,
                        "protocol.upload_chunk_ambiguous",
                        "The upload chunk was admitted without an authoritative acknowledgement."));
                    return result;
                case V2ClientInvocationDisposition.ResponseReceived:
                    break;
                default:
                    throw new InvalidOperationException("The upload operation has an unknown tracked disposition.");
            }

            if (result.IsOk(out _))
            {
                lease.NextOffset += request.Data.Length;
                return result;
            }

            var error = result.UnwrapErr();
            if (IsUploadWriteTerminal(error))
            {
                DetachLease(lease);
            }
            else
            {
                DetachLease(lease);
                lease.Gate.Release();
                releaseGate = false;
                await CompensateOrInvalidateAsync(
                    lease.Core,
                    SessionKind.Upload,
                    lease.SessionId).ConfigureAwait(false);
            }
            return result;
        }
        finally
        {
            if (releaseGate)
                lease.Gate.Release();
        }
    }

    public Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Upload, cancelUpload: false, cancellationToken);

    public Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Upload, cancelUpload: true, cancellationToken);

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryBeginOpen(out var core, out var entry, out var readinessError))
            return Result.Err<DownloadSession, DaemonError>(readinessError!);

        var operation = core.InvokeTracked(BuiltInProtocolDefinitions.OpenDownload, request, cancellationToken);
        Result<DownloadSession, DaemonError> result;
        try
        {
            result = await operation.Completion.ConfigureAwait(false);
        }
        catch
        {
            FinishExceptionalOpen(entry, operation.Outcome);
            throw;
        }

        if (operation.Outcome.Disposition == V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse)
        {
            FinishOpenAdmission(entry);
            InvalidateEpoch(core, InvocationFailure(
                result,
                "protocol.file_session_open_ambiguous",
                "The download open was admitted without an authoritative response."));
            return result;
        }

        if (result.IsErr(out _))
        {
            FinishOpenAdmission(entry);
            return result;
        }

        var session = result.Unwrap();
        if (ClassifyOpenBeforeValidation(entry, session.SessionId, out var earlyCollision) is { } earlyRejection)
        {
            FinishOpenAdmission(entry);
            await HandleOpenRejectionAsync(
                core,
                SessionKind.Download,
                session.SessionId,
                earlyRejection,
                earlyCollision).ConfigureAwait(false);
            return Result.Err<DownloadSession, DaemonError>(earlyRejection);
        }

        var beforeOpenCommit = _testHooks?.BeforeOpenCommit;
        if (beforeOpenCommit is not null)
        {
            try
            {
                await beforeOpenCommit(core).ConfigureAwait(false);
            }
            catch
            {
                FinishOpenAdmission(entry);
                InvalidateEpoch(core, OpenCommitInterrupted());
                throw;
            }
        }
        if (!core.TryRegisterDownloadSession(session, out var registrationError))
        {
            FinishOpenAdmission(entry);
            var returnedError = registrationError!.Code == "file.download.session_duplicate"
                ? DuplicateSession()
                : registrationError;
            if (registrationError.Code is "file.download.session_duplicate" or "protocol.download_session_invalid" ||
                session.SessionId == Guid.Empty)
                InvalidateEpoch(core, returnedError);
            else if (registrationError.Code != "connection.closed")
                await CompensateOrInvalidateAsync(core, SessionKind.Download, session.SessionId).ConfigureAwait(false);
            return Result.Err<DownloadSession, DaemonError>(returnedError);
        }

        DownloadLease lease;
        try
        {
            lease = new DownloadLease(
                session.SessionId,
                core,
                entry,
                session.Length,
                session.MaxChunkSize,
                Convert.FromHexString(session.Sha256));
        }
        catch
        {
            core.TryRemoveDownloadSession(session.SessionId, out _);
            FinishOpenAdmission(entry);
            await CompensateOrInvalidateAsync(core, SessionKind.Download, session.SessionId).ConfigureAwait(false);
            throw;
        }

        var rejection = CommitOpen(
            entry,
            session.SessionId,
            lease,
            validationError: null,
            out var collision);
        if (rejection is null)
            return result;

        core.TryRemoveDownloadSession(session.SessionId, out _);
        DisposeDownloadHash(lease);
        await HandleOpenRejectionAsync(
            core,
            SessionKind.Download,
            session.SessionId,
            rejection,
            collision).ConfigureAwait(false);

        return Result.Err<DownloadSession, DaemonError>(rejection);
    }

    public async Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetLease(request.SessionId, SessionKind.Download, out var found, out var lookupError))
            return Result.Err<DownloadChunk, DaemonError>(lookupError!);

        var lease = (DownloadLease)found!;
        await lease.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(lease))
                return Result.Err<DownloadChunk, DaemonError>(SessionNotFound(request.SessionId));

            Result<DownloadChunk, DaemonError> result;
            try
            {
                result = await lease.Core.ReadDownloadChunkAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (result.IsErr(out var error))
            {
                if (IsReadTerminal(error!))
                    DetachDownload(lease);
                return result;
            }

            var chunk = result.Unwrap();
            if (!lease.HashVerified && chunk.Offset == lease.VerifiedPrefix)
            {
                if (chunk.Data.IsDefault)
                    return Result.Err<DownloadChunk, DaemonError>(new TransportDaemonError(
                        "protocol.download_chunk_invalid",
                        "The download chunk data violates the V2 contract."));

                lease.Hash.AppendData(chunk.Data.AsSpan());
                lease.VerifiedPrefix += chunk.Data.Length;
                if (lease.Length == 0 && chunk.Offset == 0 && chunk.Data.Length == 0 && chunk.IsFinal)
                    lease.EmptyFinalObserved = true;

                if (lease.VerifiedPrefix == lease.Length &&
                    (lease.Length != 0 || lease.EmptyFinalObserved))
                {
                    var actual = lease.Hash.GetHashAndReset();
                    if (!CryptographicOperations.FixedTimeEquals(actual, lease.ExpectedHash))
                    {
                        var mismatch = DownloadHashMismatch();
                        DetachDownload(lease);
                        InvalidateEpoch(lease.Core, mismatch);
                        return Result.Err<DownloadChunk, DaemonError>(mismatch);
                    }

                    lease.HashVerified = true;
                }
            }

            return result;
        }
        finally
        {
            lease.Gate.Release();
        }
    }

    public Task<Result<Unit, DaemonError>> CloseDownloadAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        EndAsync(sessionId, SessionKind.Download, cancelUpload: false, cancellationToken);

    private async Task<Result<Unit, DaemonError>> EndAsync(
        Guid sessionId,
        SessionKind kind,
        bool cancelUpload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetLease(sessionId, kind, out var lease, out var lookupError))
            return Result.Err<Unit, DaemonError>(lookupError!);

        await lease!.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(lease))
                return Result.Err<Unit, DaemonError>(SessionNotFound(sessionId));

            var descriptor = kind == SessionKind.Download
                ? BuiltInProtocolDefinitions.CloseDownload
                : cancelUpload
                    ? BuiltInProtocolDefinitions.CancelUpload
                    : BuiltInProtocolDefinitions.CloseUpload;
            var operation = lease.Core.InvokeUnitTracked(descriptor, new FileSessionReference(sessionId), cancellationToken);
            Result<Unit, DaemonError> result;
            try
            {
                result = await operation.Completion.ConfigureAwait(false);
            }
            catch
            {
                HandleAmbiguousLeaseOperation(
                    lease,
                    operation.Outcome,
                    removeDownloadRegistration: kind == SessionKind.Download);
                throw;
            }

            switch (operation.Outcome.Disposition)
            {
                case V2ClientInvocationDisposition.NotAdmitted:
                    return result;
                case V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse:
                    DetachTerminal(lease, kind == SessionKind.Download);
                    InvalidateEpoch(lease.Core, InvocationFailure(
                        result,
                        "protocol.file_session_end_ambiguous",
                        "The file-session end was admitted without an authoritative response."));
                    return result;
                case V2ClientInvocationDisposition.ResponseReceived:
                    break;
                default:
                    throw new InvalidOperationException("The file-session end operation has an unknown tracked disposition.");
            }

            var terminal = result.IsOk(out _) || result.IsErr(out var error) &&
                IsEndTerminal(kind, cancelUpload, error!);
            if (terminal)
                DetachTerminal(lease, kind == SessionKind.Download);
            return result;
        }
        finally
        {
            lease.Gate.Release();
        }
    }

    private bool TryBeginOpen(
        out V2ClientConnectionCore core,
        out CoreEntry entry,
        out DaemonError? error)
    {
        if (!_owner.TryGetReadyCore(out core))
        {
            entry = null!;
            error = NotReady();
            return false;
        }

        var startObserver = false;
        lock (_ledgerGate)
        {
            if (!_cores.TryGetValue(core, out entry!))
            {
                entry = new CoreEntry(core);
                _cores.Add(core, entry);
                startObserver = true;
            }

            if (entry.ClosedObserved || core.Closed.IsCompleted)
            {
                entry.ClosedObserved = true;
                TryRemoveCoreEntry(entry);
                error = ConnectionClosed();
            }
            else
            {
                entry.OpenAdmissions++;
                error = null;
            }
        }

        if (startObserver)
            _ = ObserveCoreClosedAsync(entry);
        return error is null;
    }

    private DaemonError? ClassifyOpenBeforeValidation(
        CoreEntry entry,
        Guid sessionId,
        out CollisionKind collision)
    {
        lock (_ledgerGate)
        {
            collision = CollisionKind.None;
            if (_leases.TryGetValue(sessionId, out var existing))
            {
                collision = ReferenceEquals(existing.Core, entry.Core)
                    ? CollisionKind.SameCore
                    : CollisionKind.CrossCore;
                return DuplicateSession();
            }
            return entry.ClosedObserved || entry.Core.Closed.IsCompleted
                ? ConnectionClosed()
                : null;
        }
    }

    private DaemonError? CommitOpen(
        CoreEntry entry,
        Guid sessionId,
        Lease? lease,
        DaemonError? validationError,
        out CollisionKind collision)
    {
        lock (_ledgerGate)
        {
            entry.OpenAdmissions--;
            collision = CollisionKind.None;
            DaemonError? rejection;
            if (_leases.TryGetValue(sessionId, out var existing))
            {
                collision = ReferenceEquals(existing.Core, entry.Core)
                    ? CollisionKind.SameCore
                    : CollisionKind.CrossCore;
                rejection = DuplicateSession();
            }
            else if (entry.ClosedObserved || entry.Core.Closed.IsCompleted)
            {
                rejection = ConnectionClosed();
            }
            else if (validationError is not null)
            {
                rejection = validationError;
            }
            else
            {
                _leases.Add(sessionId, lease!);
                entry.Leases.Add(lease!);
                if (lease is DownloadLease download)
                {
                    download.HashCounted = true;
                    entry.UndisposedDownloadHashes++;
                }
                rejection = null;
            }

            TryRemoveCoreEntry(entry);
            return rejection;
        }
    }

    private async Task HandleOpenRejectionAsync(
        V2ClientConnectionCore core,
        SessionKind kind,
        Guid sessionId,
        DaemonError rejection,
        CollisionKind collision)
    {
        if (collision == CollisionKind.SameCore || sessionId == Guid.Empty ||
            collision == CollisionKind.None &&
            rejection.Code is "protocol.upload_session_invalid" or "protocol.download_session_invalid")
        {
            InvalidateEpoch(core, rejection);
            return;
        }
        if (rejection.Code != "connection.closed")
            await CompensateOrInvalidateAsync(core, kind, sessionId).ConfigureAwait(false);
    }

    private void FinishExceptionalOpen(CoreEntry entry, V2ClientInvocationOutcome outcome)
    {
        FinishOpenAdmission(entry);
        if (outcome.IsCompleted &&
            outcome.Disposition == V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse)
        {
            InvalidateEpoch(entry.Core, new TransportDaemonError(
                "protocol.file_session_open_ambiguous",
                "The file-session open was admitted without an authoritative response."));
        }
    }

    private void FinishOpenAdmission(CoreEntry entry)
    {
        lock (_ledgerGate)
        {
            entry.OpenAdmissions--;
            TryRemoveCoreEntry(entry);
        }
    }

    private async Task ObserveCoreClosedAsync(CoreEntry entry)
    {
        await entry.Core.Closed.ConfigureAwait(false);
        Exception? observerError = null;
        try
        {
            var beforeClosedObserver = _testHooks?.BeforeClosedObserver;
            if (beforeClosedObserver is not null)
                await beforeClosedObserver(entry.Core).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            observerError = exception;
        }
        finally
        {
            List<Lease> detached = [];
            lock (_ledgerGate)
            {
                entry.CleanupError = observerError;
                entry.ClosedObserved = true;
                foreach (var lease in entry.Leases)
                {
                    if (_leases.TryGetValue(lease.SessionId, out var current) && ReferenceEquals(current, lease))
                    {
                        _leases.Remove(lease.SessionId);
                        lease.Detached = true;
                        detached.Add(lease);
                    }
                }
                entry.Leases.Clear();
                TryRemoveCoreEntry(entry);
            }

            foreach (var lease in detached)
                await DisposeAfterGateAsync(lease).ConfigureAwait(false);
        }
    }

    private void TryRemoveCoreEntry(CoreEntry entry)
    {
        if (entry.ClosedObserved && entry.OpenAdmissions == 0 && entry.Leases.Count == 0 &&
            entry.UndisposedDownloadHashes == 0 &&
            _cores.TryGetValue(entry.Core, out var current) && ReferenceEquals(current, entry))
        {
            _cores.Remove(entry.Core);
            if (entry.CleanupError is OperationCanceledException cancellation)
                entry.CleanupCompleted.TrySetCanceled(cancellation.CancellationToken);
            else if (entry.CleanupError is { } exception)
                entry.CleanupCompleted.TrySetException(exception);
            else
                entry.CleanupCompleted.TrySetResult();
        }
    }

    private bool TryGetLease(
        Guid sessionId,
        SessionKind expectedKind,
        out Lease? lease,
        out DaemonError? error)
    {
        lock (_ledgerGate)
        {
            if (!_leases.TryGetValue(sessionId, out lease))
            {
                error = SessionNotFound(sessionId);
                return false;
            }
            if (lease.Kind != expectedKind)
            {
                error = SessionKindMismatch(sessionId, expectedKind);
                lease = null;
                return false;
            }
        }

        error = null;
        return true;
    }

    private bool IsCurrent(Lease lease)
    {
        lock (_ledgerGate)
            return _leases.TryGetValue(lease.SessionId, out var current) && ReferenceEquals(current, lease);
    }

    private bool DetachLease(Lease lease)
    {
        lock (_ledgerGate)
        {
            if (!_leases.TryGetValue(lease.SessionId, out var current) || !ReferenceEquals(current, lease))
                return false;
            _leases.Remove(lease.SessionId);
            lease.Entry.Leases.Remove(lease);
            lease.Detached = true;
            TryRemoveCoreEntry(lease.Entry);
            return true;
        }
    }

    private void DetachDownload(DownloadLease lease)
    {
        if (DetachLease(lease))
            lease.Core.TryRemoveDownloadSession(lease.SessionId, out _);
        DisposeDownloadHash(lease);
    }

    private void DetachTerminal(Lease lease, bool removeDownloadRegistration)
    {
        var detached = DetachLease(lease);
        if (detached && removeDownloadRegistration)
            lease.Core.TryRemoveDownloadSession(lease.SessionId, out _);
        if (lease is DownloadLease download)
            DisposeDownloadHash(download);
    }

    private void HandleAmbiguousLeaseOperation(
        Lease lease,
        V2ClientInvocationOutcome outcome,
        bool removeDownloadRegistration)
    {
        if (!outcome.IsCompleted ||
            outcome.Disposition != V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse)
        {
            return;
        }

        DetachTerminal(lease, removeDownloadRegistration);
        InvalidateEpoch(lease.Core, new TransportDaemonError(
            "protocol.file_session_operation_ambiguous",
            "The file-session operation was admitted without an authoritative response."));
    }

    private async Task DisposeAfterGateAsync(Lease lease)
    {
        await lease.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (lease is DownloadLease download)
                DisposeDownloadHash(download);
        }
        finally
        {
            lease.Gate.Release();
        }
    }

    private void DisposeDownloadHash(DownloadLease lease)
    {
        if (Interlocked.Exchange(ref lease.HashDisposed, 1) != 0)
            return;
        lease.Hash.Dispose();
        Interlocked.Increment(ref _disposedDownloadHashCount);
        if (lease.HashCounted)
        {
            lock (_ledgerGate)
            {
                lease.Entry.UndisposedDownloadHashes--;
                TryRemoveCoreEntry(lease.Entry);
            }
        }
    }

    private async Task CompensateOrInvalidateAsync(
        V2ClientConnectionCore core,
        SessionKind kind,
        Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            InvalidateEpoch(core, CompensationFailed());
            return;
        }

        var descriptor = kind == SessionKind.Upload
            ? BuiltInProtocolDefinitions.CancelUpload
            : BuiltInProtocolDefinitions.CloseDownload;
        var operation = core.InvokeUnitTracked(descriptor, new FileSessionReference(sessionId), CancellationToken.None);
        DaemonError? compensationError = null;
        try
        {
            var result = await operation.Completion.ConfigureAwait(false);
            if (operation.Outcome.Disposition == V2ClientInvocationDisposition.ResponseReceived &&
                (result.IsOk(out _) || result.IsErr(out var error) && IsExpectedCleanupTerminal(error!)))
            {
                return;
            }
            if (result.IsErr(out var failure))
                compensationError = failure;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        InvalidateEpoch(core, CompensationFailed(compensationError));
    }

    private void InvalidateEpoch(V2ClientConnectionCore core, DaemonError error) =>
        _owner.InvalidateEpoch(core, error);

    private DaemonError? ValidateUploadSession(UploadOpenRequest request, UploadSession session)
    {
        if (session.SessionId == Guid.Empty || request.Length < 0 || !IsSha256(request.Sha256) ||
            session.MaxChunkSize is <= 0 or > (int)BinaryFrameCodec.DefaultMaximumChunkSize ||
            session.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            return new TransportDaemonError(
                "protocol.upload_session_invalid",
                "The upload session metadata violates the V2 contract.");
        }
        return null;
    }

    private static DaemonError? ValidateUploadChunk(UploadLease lease, UploadChunkRequest request)
    {
        if (request.Data.IsDefault)
            return new ValidationDaemonError("file.chunk.data.invalid", "The upload chunk data is required.");
        if (request.Offset != lease.NextOffset)
            return new ValidationDaemonError("file.chunk.offset.invalid", "The upload chunk offset must be the next expected offset.");
        if (request.Data.Length > lease.MaxChunkSize)
            return new ValidationDaemonError("file.chunk.too_large", "The upload chunk exceeds the session maximum chunk size.");
        if (request.Offset < 0 || request.Offset > lease.DeclaredLength ||
            request.Data.Length > lease.DeclaredLength - request.Offset)
        {
            return new ValidationDaemonError("file.upload.size_exceeded", "The upload chunk exceeds the declared file length.");
        }
        return null;
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))
                return false;
        }
        return true;
    }

    private static bool IsReadTerminal(DaemonError error) =>
        error.Code is "file.session.not_found" or "file.session.expired";

    private static bool IsUploadWriteTerminal(DaemonError error) => error.Code is
        "file.session.not_found" or "file.session.expired" or "file.chunk.offset.invalid" or
        "file.chunk.too_large" or "file.upload.size_exceeded" ||
        error.Kind is DaemonErrorKind.Storage or DaemonErrorKind.Internal;

    private static bool IsEndTerminal(SessionKind kind, bool cancelUpload, DaemonError error)
    {
        if (error.Code == "file.session.busy")
            return false;
        if (kind == SessionKind.Upload && error.Code == "file.upload.chunk_in_flight")
            return false;
        return kind == SessionKind.Download || cancelUpload || error.Code != "file.upload.incomplete";
    }

    private static bool IsExpectedCleanupTerminal(DaemonError error) =>
        error.Code is "file.session.not_found" or "file.session.expired";

    private static DaemonError InvocationFailure<TResult>(
        Result<TResult, DaemonError> result,
        string fallbackCode,
        string fallbackMessage)
        where TResult : notnull =>
        result.IsErr(out var error) ? error! : new TransportDaemonError(fallbackCode, fallbackMessage);

    private static InternalDaemonError CompensationFailed(DaemonError? error = null) =>
        new(
            "file.session.compensation_failed",
            error is null
                ? "File-session registration compensation did not complete authoritatively."
                : $"File-session registration compensation failed: {error.Code}: {error.Message}");

    private static TransportDaemonError NotReady() =>
        new("client.not_ready", "The daemon client is not connected and ready.");

    private static TransportDaemonError ConnectionClosed() =>
        new("connection.closed", "The V2 connection is closed.");

    private static TransportDaemonError OpenCommitInterrupted() =>
        new("protocol.file_session_open_commit_interrupted", "The file-session open was interrupted before local commit.");

    private static NotFoundDaemonError SessionNotFound(Guid sessionId) =>
        new("file.session.not_found", $"The file session '{sessionId}' was not found.");

    private static ConflictDaemonError SessionKindMismatch(Guid sessionId, SessionKind expectedKind) =>
        new("file.session.kind_mismatch", $"The file session '{sessionId}' is not a {expectedKind.ToString().ToLowerInvariant()} session.");

    private static TransportDaemonError DuplicateSession() =>
        new("protocol.file_session_duplicate", "The opened file session identifier is already registered.");

    private static TransportDaemonError DownloadHashMismatch() =>
        new("protocol.download_hash_mismatch", "The downloaded bytes do not match the declared SHA-256 hash.");

    private enum SessionKind
    {
        Upload,
        Download
    }

    private enum CollisionKind
    {
        None,
        SameCore,
        CrossCore
    }

    private sealed class CoreEntry(V2ClientConnectionCore core)
    {
        internal V2ClientConnectionCore Core { get; } = core;
        internal HashSet<Lease> Leases { get; } = new(ReferenceEqualityComparer.Instance);
        internal TaskCompletionSource CleanupCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? CleanupError { get; set; }
        internal int OpenAdmissions { get; set; }
        internal int UndisposedDownloadHashes { get; set; }
        internal bool ClosedObserved { get; set; }
    }

    private abstract class Lease(
        Guid sessionId,
        SessionKind kind,
        V2ClientConnectionCore core,
        CoreEntry entry)
    {
        internal Guid SessionId { get; } = sessionId;
        internal SessionKind Kind { get; } = kind;
        internal V2ClientConnectionCore Core { get; } = core;
        internal CoreEntry Entry { get; } = entry;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal bool Detached { get; set; }
    }

    private sealed class UploadLease(
        Guid sessionId,
        V2ClientConnectionCore core,
        CoreEntry entry,
        long declaredLength,
        int maxChunkSize)
        : Lease(sessionId, SessionKind.Upload, core, entry)
    {
        internal long DeclaredLength { get; } = declaredLength;
        internal int MaxChunkSize { get; } = maxChunkSize;
        internal long NextOffset { get; set; }
    }

    private sealed class DownloadLease(
        Guid sessionId,
        V2ClientConnectionCore core,
        CoreEntry entry,
        long length,
        int maxChunkSize,
        byte[] expectedHash)
        : Lease(sessionId, SessionKind.Download, core, entry)
    {
        internal long Length { get; } = length;
        internal int MaxChunkSize { get; } = maxChunkSize;
        internal byte[] ExpectedHash { get; } = expectedHash;
        internal IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal long VerifiedPrefix { get; set; }
        internal bool EmptyFinalObserved { get; set; }
        internal bool HashVerified { get; set; }
        internal bool HashCounted { get; set; }
        internal int HashDisposed;
    }
}

internal sealed class RemoteFileApplicationTestHooks
{
    internal Func<V2ClientConnectionCore, ValueTask>? BeforeOpenCommit { get; init; }
    internal Func<V2ClientConnectionCore, ValueTask>? BeforeClosedObserver { get; init; }
}
