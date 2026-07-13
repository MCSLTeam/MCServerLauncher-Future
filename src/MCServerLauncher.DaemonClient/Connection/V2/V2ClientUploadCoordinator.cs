using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class V2ClientUploadCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionEntry> _sessions = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly Action _sendLifetimeCreated;
    private readonly Action _sendLifetimeDisposed;
    private readonly V2ClientUploadAdmissionTestGate? _admissionTestGate;
    private bool _closed;

    internal V2ClientUploadCoordinator(
        TimeProvider timeProvider,
        TimeSpan timeout,
        Action sendLifetimeCreated,
        Action sendLifetimeDisposed,
        V2ClientUploadAdmissionTestGate? admissionTestGate = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeout = timeout;
        _sendLifetimeCreated = sendLifetimeCreated ?? throw new ArgumentNullException(nameof(sendLifetimeCreated));
        _sendLifetimeDisposed = sendLifetimeDisposed ?? throw new ArgumentNullException(nameof(sendLifetimeDisposed));
        _admissionTestGate = admissionTestGate;
    }

    internal int PendingCount
    {
        get
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var entry in _sessions.Values)
                {
                    if (entry.Pending is not null)
                        count++;
                }

                return count;
            }
        }
    }

    internal bool TryAdmit(
        Guid sessionId,
        long offset,
        int length,
        out PendingUpload? pending,
        out DaemonError? error)
    {
        lock (_gate)
        {
            if (_closed)
            {
                pending = null;
                error = ClosedError();
                return false;
            }

            if (_sessions.TryGetValue(sessionId, out var existing))
            {
                pending = null;
                error = existing.Pending is null
                    ? new ConflictDaemonError(
                        "file.upload.session_poisoned",
                        "The upload session cannot send another chunk in this connection epoch.")
                    : new ConflictDaemonError(
                        "file.upload.chunk_pending",
                        "The upload session already has a pending chunk acknowledgement.");
                return false;
            }

            pending = new PendingUpload(
                this,
                sessionId,
                offset,
                length,
                _timeProvider,
                _timeout,
                _sendLifetimeCreated,
                _sendLifetimeDisposed);
            if (!pending.TryMarkAdmitted())
                throw new InvalidOperationException("A new upload pending operation could not publish admission.");

            // Deterministic internal test seam; production composition always leaves it null.
            _admissionTestGate?.WaitAtPublicationBoundary();
            _sessions.Add(sessionId, new SessionEntry(pending));
            error = null;
        }

        _admissionTestGate?.WaitForRouteCompletion();
        return true;
    }

    internal UploadAcknowledgementRoute RouteAcknowledgement(UploadChunkAcknowledgement acknowledgement)
    {
        _admissionTestGate?.SignalRouteAttempt();
        try
        {
            PendingUpload? pending;
            Result<Unit, DaemonError> result;
            UploadAcknowledgementRoute route;

            lock (_gate)
            {
                if (_closed ||
                    !_sessions.TryGetValue(acknowledgement.SessionId, out var entry) ||
                    (pending = entry.Pending) is null)
                {
                    return UploadAcknowledgementRoute.Ignored;
                }

                if (pending.Offset != acknowledgement.Offset || pending.Length != acknowledgement.Length)
                {
                    _sessions[acknowledgement.SessionId] = SessionEntry.Poisoned;
                    result = Result.Err<Unit, DaemonError>(new TransportDaemonError(
                        "protocol.upload_ack_mismatch",
                        "The upload acknowledgement does not match the pending chunk."));
                    route = UploadAcknowledgementRoute.ProtocolFault;
                }
                else if (acknowledgement.Status == UploadChunkAcknowledgementStatus.Accepted)
                {
                    _sessions.Remove(acknowledgement.SessionId);
                    result = Result.Ok<Unit, DaemonError>(Unit.Default);
                    route = UploadAcknowledgementRoute.Completed;
                }
                else
                {
                    _sessions[acknowledgement.SessionId] = SessionEntry.Poisoned;
                    result = Result.Err<Unit, DaemonError>(
                        V2ClientConnectionCore.MapError(acknowledgement.Error!));
                    route = UploadAcknowledgementRoute.Completed;
                }
            }

            pending.TrySetResult(result, authoritativeResponse: route == UploadAcknowledgementRoute.Completed);
            return route;
        }
        finally
        {
            _admissionTestGate?.SignalRouteCompleted();
        }
    }

    internal void FailSend(PendingUpload pending, DaemonError error)
    {
        var complete = false;
        lock (_gate)
        {
            if (!_closed && IsPending(pending))
            {
                _sessions[pending.SessionId] = SessionEntry.Poisoned;
                complete = true;
            }
        }

        if (complete)
            pending.TrySetResult(Result.Err<Unit, DaemonError>(error), authoritativeResponse: false);
    }

    internal void Close(DaemonError error)
    {
        List<PendingUpload> pending = [];
        lock (_gate)
        {
            if (_closed)
                return;

            _closed = true;
            foreach (var entry in _sessions.Values)
            {
                if (entry.Pending is not null)
                    pending.Add(entry.Pending);
            }
            _sessions.Clear();
        }

        var result = Result.Err<Unit, DaemonError>(error);
        foreach (var item in pending)
            item.TrySetResult(result, authoritativeResponse: false);
    }

    private void Cancel(PendingUpload pending, CancellationToken token)
    {
        var complete = false;
        lock (_gate)
        {
            if (!_closed && IsPending(pending))
            {
                _sessions[pending.SessionId] = SessionEntry.Poisoned;
                complete = true;
            }
        }

        if (complete)
            pending.TrySetCanceled(token);
    }

    private void Timeout(PendingUpload pending)
    {
        var complete = false;
        lock (_gate)
        {
            if (!_closed && IsPending(pending))
            {
                _sessions[pending.SessionId] = SessionEntry.Poisoned;
                complete = true;
            }
        }

        if (complete)
        {
            pending.TrySetResult(
                Result.Err<Unit, DaemonError>(new TransportDaemonError(
                    "request.timeout",
                    "The V2 upload chunk acknowledgement timed out.")),
                authoritativeResponse: false);
        }
    }

    private bool IsPending(PendingUpload pending) =>
        _sessions.TryGetValue(pending.SessionId, out var entry) &&
        ReferenceEquals(entry.Pending, pending);

    private static TransportDaemonError ClosedError() =>
        new("connection.closed", "The V2 connection is closed.");

    private sealed record SessionEntry(PendingUpload? Pending)
    {
        internal static SessionEntry Poisoned { get; } = new((PendingUpload?)null);
    }

    internal sealed class PendingUpload
    {
        private readonly V2ClientUploadCoordinator _owner;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _timeout;
        private readonly Action _sendLifetimeCreated;
        private readonly Action _sendLifetimeDisposed;
        private readonly object _resourceGate = new();
        private readonly V2ClientInvocationOutcome _outcome = new();
        private readonly TaskCompletionSource<Result<Unit, DaemonError>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly V2ClientInvocationOperation<Unit> _operation;
        private CancellationTokenRegistration _callerRegistration;
        private ITimer? _timer;
        private bool _sendLifetimeActive;

        internal PendingUpload(
            V2ClientUploadCoordinator owner,
            Guid sessionId,
            long offset,
            int length,
            TimeProvider timeProvider,
            TimeSpan timeout,
            Action sendLifetimeCreated,
            Action sendLifetimeDisposed)
        {
            _owner = owner;
            SessionId = sessionId;
            Offset = offset;
            Length = length;
            _timeProvider = timeProvider;
            _timeout = timeout;
            _sendLifetimeCreated = sendLifetimeCreated;
            _sendLifetimeDisposed = sendLifetimeDisposed;
            _operation = new(_completion.Task, _outcome);
        }

        internal Guid SessionId { get; }
        internal long Offset { get; }
        internal int Length { get; }
        internal Task<Result<Unit, DaemonError>> Task => _completion.Task;
        internal V2ClientInvocationOperation<Unit> Operation => _operation;
        internal bool IsCompleted => _outcome.IsCompleted;

        internal bool TryMarkAdmitted() => _outcome.TryMarkAdmitted();

        internal void CreateSendLifetime()
        {
            lock (_resourceGate)
            {
                if (_sendLifetimeActive)
                    throw new InvalidOperationException("The upload send lifetime already exists.");
                _sendLifetimeActive = true;
            }

            _sendLifetimeCreated();
        }

        internal void Register(CancellationToken callerToken)
        {
            if (IsCompleted)
                return;

            if (callerToken.CanBeCanceled)
            {
                var registration = callerToken.Register(static state =>
                {
                    var pair = ((PendingUpload Pending, CancellationToken Token))state!;
                    pair.Pending._owner.Cancel(pair.Pending, pair.Token);
                }, (this, callerToken));
                StoreRegistration(registration);
            }

            if (IsCompleted)
                return;

            var timer = _timeProvider.CreateTimer(static state =>
            {
                var pending = (PendingUpload)state!;
                pending._owner.Timeout(pending);
            }, this, _timeout, System.Threading.Timeout.InfiniteTimeSpan);
            StoreTimer(timer);
        }

        internal bool TrySetResult(Result<Unit, DaemonError> result, bool authoritativeResponse) =>
            TryComplete(authoritativeResponse, () => _completion.TrySetResult(result));

        internal bool TrySetCanceled(CancellationToken token) =>
            TryComplete(authoritativeResponse: false, () => _completion.TrySetCanceled(token));

        internal void DisposeSendLifetime()
        {
            var dispose = false;
            lock (_resourceGate)
            {
                if (_sendLifetimeActive)
                {
                    _sendLifetimeActive = false;
                    dispose = true;
                }
            }

            if (dispose)
                _sendLifetimeDisposed();
        }

        private bool TryComplete(bool authoritativeResponse, Func<bool> complete)
        {
            if (!_outcome.TryComplete(authoritativeResponse))
                return false;

            try
            {
                DisposeRegistrations();
            }
            finally
            {
                complete();
            }
            return true;
        }

        private void StoreRegistration(CancellationTokenRegistration registration)
        {
            var dispose = false;
            lock (_resourceGate)
            {
                if (_outcome.IsCompleted)
                    dispose = true;
                else
                    _callerRegistration = registration;
            }

            if (dispose)
                registration.Dispose();
        }

        private void StoreTimer(ITimer timer)
        {
            var dispose = false;
            lock (_resourceGate)
            {
                if (_outcome.IsCompleted)
                    dispose = true;
                else
                    _timer = timer;
            }

            if (dispose)
                timer.Dispose();
        }

        private void DisposeRegistrations()
        {
            CancellationTokenRegistration registration;
            ITimer? timer;
            lock (_resourceGate)
            {
                registration = _callerRegistration;
                _callerRegistration = default;
                timer = _timer;
                _timer = null;
            }

            try
            {
                registration.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }

            try
            {
                timer?.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
    }
}

internal sealed class V2ClientUploadAdmissionTestGate : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _publicationReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _routeAttempted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _publicationRelease = new();
    private readonly ManualResetEventSlim _routeCompleted = new();

    internal Task PublicationReached => _publicationReached.Task;
    internal Task RouteAttempted => _routeAttempted.Task;

    internal void WaitAtPublicationBoundary()
    {
        _publicationReached.TrySetResult();
        if (!_publicationRelease.Wait(WaitTimeout))
            throw new TimeoutException("The upload admission publication test gate was not released.");
    }

    internal void SignalRouteAttempt() => _routeAttempted.TrySetResult();

    internal void WaitForRouteCompletion()
    {
        if (!_routeCompleted.Wait(WaitTimeout))
            throw new TimeoutException("The upload acknowledgement test route did not complete.");
    }

    internal void SignalRouteCompleted() => _routeCompleted.Set();

    internal void ReleasePublication() => _publicationRelease.Set();

    public void Dispose()
    {
        _publicationRelease.Set();
        _routeCompleted.Set();
        _publicationRelease.Dispose();
        _routeCompleted.Dispose();
    }
}

internal enum UploadAcknowledgementRoute
{
    Completed,
    Ignored,
    ProtocolFault
}
