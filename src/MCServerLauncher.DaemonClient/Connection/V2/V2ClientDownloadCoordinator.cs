using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class V2ClientDownloadCoordinator
{
    internal static readonly TimeSpan DefaultAbandonedDrainTimeout = TimeSpan.FromSeconds(30);
    internal const int DefaultMaximumAbandonedDrains = 64;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionState> _sessions = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _abandonedDrainTimeout;
    private readonly int _maximumAbandonedDrains;
    private readonly Action<string> _protocolFault;
    private int _abandonedDrainCount;
    private bool _closed;

    internal V2ClientDownloadCoordinator(
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        Action<string> protocolFault,
        TimeSpan? abandonedDrainTimeout = null,
        int maximumAbandonedDrains = DefaultMaximumAbandonedDrains)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _requestTimeout = requestTimeout;
        _protocolFault = protocolFault ?? throw new ArgumentNullException(nameof(protocolFault));
        _abandonedDrainTimeout = abandonedDrainTimeout ?? DefaultAbandonedDrainTimeout;
        if (_abandonedDrainTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(abandonedDrainTimeout));
        if (maximumAbandonedDrains <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAbandonedDrains));
        _maximumAbandonedDrains = maximumAbandonedDrains;
    }

    internal int PendingCount
    {
        get
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var session in _sessions.Values)
                {
                    if (session.Pending is not null)
                        count++;
                }
                return count;
            }
        }
    }

    internal int AbandonedDrainCount
    {
        get
        {
            lock (_gate)
                return _abandonedDrainCount;
        }
    }

    internal bool TryRegisterSession(DownloadSession session, out DaemonError? error)
    {
        ArgumentNullException.ThrowIfNull(session);
        error = ValidateSession(session);
        if (error is not null)
            return false;

        lock (_gate)
        {
            if (_closed)
            {
                error = ClosedError();
                return false;
            }
            if (!_sessions.TryAdd(session.SessionId, new SessionState(session)))
            {
                error = new ConflictDaemonError(
                    "file.download.session_duplicate",
                    "The download session is already registered in this connection epoch.");
                return false;
            }
        }

        return true;
    }

    internal bool TryRemoveSession(Guid sessionId, out DaemonError? error)
    {
        lock (_gate)
        {
            if (_closed)
            {
                error = ClosedError();
                return false;
            }
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                error = SessionNotFound(sessionId);
                return false;
            }
            if (session.Pending is not null)
            {
                error = new ConflictDaemonError(
                    "file.download.chunk_pending",
                    "The download session has a pending chunk read.");
                return false;
            }
            _sessions.Remove(sessionId);
            error = null;
            return true;
        }
    }

    internal bool TryAdmit(
        JsonRpcRequestId requestId,
        DownloadChunkRequest request,
        Action<PendingDownload> drained,
        out PendingDownload? pending,
        out DaemonError? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(drained);

        lock (_gate)
        {
            if (_closed)
            {
                pending = null;
                error = ClosedError();
                return false;
            }
            if (!_sessions.TryGetValue(request.SessionId, out var session))
            {
                pending = null;
                error = SessionNotFound(request.SessionId);
                return false;
            }
            error = ValidateRequest(session.Descriptor, request);
            if (error is not null)
            {
                pending = null;
                return false;
            }
            if (session.Poisoned)
            {
                pending = null;
                error = new ConflictDaemonError(
                    "file.download.session_poisoned",
                    "The download session cannot read another chunk in this connection epoch.");
                return false;
            }
            if (session.Pending is not null)
            {
                pending = null;
                error = new ConflictDaemonError(
                    "file.download.chunk_pending",
                    "The download session already has a pending chunk read.");
                return false;
            }

            pending = new PendingDownload(
                this,
                requestId,
                request,
                drained,
                _timeProvider,
                _requestTimeout,
                _abandonedDrainTimeout);
            session.Pending = pending;
            error = null;
            return true;
        }
    }

    internal bool TryMarkSendStarted(PendingDownload pending)
    {
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out _))
                return false;
            pending.MarkSendStartedCore();
            return true;
        }
    }

    internal void RejectAdmission(PendingDownload pending)
    {
        lock (_gate)
        {
            if (!_closed && TryGetCurrent(pending, out var session))
                session.Pending = null;
        }
        pending.Drain();
    }

    internal void FailRegistration(PendingDownload pending, DaemonError error)
    {
        lock (_gate)
        {
            if (!_closed && TryGetCurrent(pending, out var session))
                session.Pending = null;
        }
        pending.Drain();
        pending.FinishError(error);
    }

    internal void Close(DaemonError error)
    {
        List<PendingDownload> pending = [];
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            foreach (var session in _sessions.Values)
            {
                if (session.Pending is not null)
                    pending.Add(session.Pending);
            }
            _sessions.Clear();
            _abandonedDrainCount = 0;
        }

        foreach (var item in pending)
            item.FinishClose(error);
    }

    internal BinaryRoute RouteBinary(BinaryFrameHeader header, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(header);
        PendingDownload? pending = null;
        DownloadReadResult? metadata = null;
        var fault = "The download binary frame has no pending read.";

        lock (_gate)
        {
            if (_closed)
                return BinaryRoute.Ignored;
            if (header.Kind != BinaryFrameKind.DownloadChunk ||
                !_sessions.TryGetValue(header.SessionId, out var session) ||
                (pending = session.Pending) is null)
            {
                pending = null;
            }
            else if ((metadata = pending.Metadata) is null)
            {
                fault = "The download binary frame arrived before its JSON metadata.";
            }
            else if (header.Offset != metadata.Offset ||
                     header.PayloadLength != checked((uint)metadata.Length) ||
                     payload.Length != metadata.Length)
            {
                fault = "The download binary frame does not match its JSON metadata.";
            }
            else
            {
                session.Pending = null;
                if (pending.IsAbandoned)
                    _abandonedDrainCount--;
                else
                    session.Poisoned = false;
            }
        }

        if (pending is null || metadata is null ||
            header.Offset != metadata.Offset ||
            header.PayloadLength != checked((uint)metadata.Length) ||
            payload.Length != metadata.Length)
        {
            if (pending is not null)
                PoisonAndDrain(pending, ProtocolError("protocol.download_binary_mismatch", fault));
            _protocolFault(fault);
            return BinaryRoute.ProtocolFault;
        }

        var data = ImmutableCollectionsMarshal.AsImmutableArray(payload.ToArray());
        pending.FinishBinary(new DownloadChunk(metadata.Offset, data, metadata.IsFinal));
        return BinaryRoute.Completed;
    }

    private void Cancel(PendingDownload pending, CancellationToken token)
    {
        var abandoned = false;
        var drained = false;
        var overLimit = false;
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out var session))
                return;
            if (pending.IsAbandoned)
                return;
            if (!pending.TryReserveCallerCompletion())
                return;
            if (!pending.SendStarted)
            {
                session.Pending = null;
                drained = true;
            }
            else
            {
                session.Poisoned = true;
                pending.IsAbandoned = true;
                abandoned = true;
                overLimit = ++_abandonedDrainCount > _maximumAbandonedDrains;
            }
        }

        if (drained)
            pending.Drain();
        pending.CompleteReservedCanceled(token);
        if (abandoned)
            pending.BeginAbandonedDrain();
        if (overLimit)
            _protocolFault("The connection exceeded the abandoned download drain limit.");
    }

    private void Timeout(PendingDownload pending)
    {
        var abandoned = false;
        var drained = false;
        var overLimit = false;
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out var session))
                return;
            if (pending.IsAbandoned)
                return;
            if (!pending.TryReserveCallerCompletion())
                return;
            if (!pending.SendStarted)
            {
                session.Pending = null;
                drained = true;
            }
            else
            {
                session.Poisoned = true;
                pending.IsAbandoned = true;
                abandoned = true;
                overLimit = ++_abandonedDrainCount > _maximumAbandonedDrains;
            }
        }

        if (drained)
            pending.Drain();
        pending.CompleteReservedError(new TransportDaemonError(
            "request.timeout",
            "The V2 download chunk read timed out."));
        if (abandoned)
            pending.BeginAbandonedDrain();
        if (overLimit)
            _protocolFault("The connection exceeded the abandoned download drain limit.");
    }

    private void AbandonedDrainExpired(PendingDownload pending)
    {
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out _) || !pending.IsAbandoned)
                return;
        }
        _protocolFault("An abandoned download read did not drain before its deadline.");
    }

    private bool RouteMetadata(PendingDownload pending, DownloadReadResult metadata)
    {
        string? fault = null;
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out var session))
                return false;
            if (pending.Metadata is not null)
            {
                fault = "The download read returned duplicate JSON metadata.";
            }
            else if (metadata.SessionId != pending.Request.SessionId ||
                     metadata.Offset != pending.Request.Offset ||
                     metadata.Length > pending.Request.MaximumLength ||
                     metadata.Length > session.Descriptor.MaxChunkSize ||
                     metadata.Length > (int)BinaryFrameCodec.DefaultMaximumChunkSize ||
                     metadata.Offset > session.Descriptor.Length - metadata.Length ||
                     metadata.IsFinal != (metadata.Offset + metadata.Length == session.Descriptor.Length))
            {
                fault = "The download JSON metadata does not match the pending read or session bounds.";
            }
            else
            {
                pending.Metadata = metadata;
                return true;
            }
        }

        PoisonAndDrain(pending, ProtocolError("protocol.download_metadata_mismatch", fault));
        _protocolFault(fault);
        return true;
    }

    private bool RouteError(PendingDownload pending, DaemonError error)
    {
        var protocolFault = false;
        lock (_gate)
        {
            if (_closed || !TryGetCurrent(pending, out var session))
                return false;
            if (pending.Metadata is not null)
            {
                PoisonAndDrainUnderLock(session, pending);
                protocolFault = true;
            }
            else
            {
                session.Pending = null;
                if (pending.IsAbandoned)
                    _abandonedDrainCount--;
            }
        }

        pending.Drain();
        if (protocolFault)
        {
            pending.FinishError(ProtocolError(
                "protocol.download_response_order_invalid",
                "The download read returned an error after JSON metadata."));
            _protocolFault("The download read returned an error after JSON metadata.");
        }
        else
        {
            pending.FinishError(error);
        }
        return true;
    }

    private void FailSend(PendingDownload pending, DaemonError error)
    {
        PoisonAndDrain(pending, error);
    }

    private void PoisonAndDrain(PendingDownload pending, DaemonError error)
    {
        lock (_gate)
        {
            if (!_closed && TryGetCurrent(pending, out var session))
                PoisonAndDrainUnderLock(session, pending);
        }
        pending.Drain();
        pending.FinishError(error);
    }

    private void PoisonAndDrainUnderLock(SessionState session, PendingDownload pending)
    {
        session.Poisoned = true;
        session.Pending = null;
        if (pending.IsAbandoned)
            _abandonedDrainCount--;
    }

    private bool TryGetCurrent(PendingDownload pending, out SessionState session) =>
        _sessions.TryGetValue(pending.Request.SessionId, out session!) &&
        ReferenceEquals(session.Pending, pending);

    private DaemonError? ValidateSession(DownloadSession session)
    {
        if (session.SessionId == Guid.Empty || session.Length < 0 ||
            !IsSha256(session.Sha256) ||
            session.ExpiresAt <= _timeProvider.GetUtcNow() ||
            session.MaxChunkSize is <= 0 or > (int)BinaryFrameCodec.DefaultMaximumChunkSize)
        {
            return ProtocolError(
                "protocol.download_session_invalid",
                "The download session metadata violates the V2 contract.");
        }
        return null;
    }

    private static bool IsSha256(string value)
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

    private static DaemonError? ValidateRequest(DownloadSession session, DownloadChunkRequest request)
    {
        if (request.SessionId == Guid.Empty)
            return new ValidationDaemonError("file.session.invalid", "The download session identifier cannot be empty.");
        if (request.Offset < 0 || request.Offset > session.Length)
            return new ValidationDaemonError("file.chunk.offset.invalid", "The download chunk offset is outside the session bounds.");
        if (request.MaximumLength <= 0 ||
            request.MaximumLength > session.MaxChunkSize ||
            request.MaximumLength > (int)BinaryFrameCodec.DefaultMaximumChunkSize)
        {
            return new ValidationDaemonError("file.chunk.size.invalid", "The download maximum chunk size is invalid.");
        }
        return null;
    }

    private static TransportDaemonError ProtocolError(string code, string message) => new(code, message);
    private static TransportDaemonError ClosedError() => new("connection.closed", "The V2 connection is closed.");
    private static NotFoundDaemonError SessionNotFound(Guid sessionId) =>
        new("file.download.session_not_registered", $"The download session '{sessionId}' is not registered in this connection epoch.");

    private sealed class SessionState(DownloadSession descriptor)
    {
        internal DownloadSession Descriptor { get; } = descriptor;
        internal PendingDownload? Pending { get; set; }
        internal bool Poisoned { get; set; }
    }

    internal sealed class PendingDownload : IV2ClientPendingRequest
    {
        private readonly V2ClientDownloadCoordinator _owner;
        private readonly Action<PendingDownload> _drained;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _requestTimeout;
        private readonly TimeSpan _abandonedDrainTimeout;
        private readonly object _resourceGate = new();
        private readonly TaskCompletionSource<Result<DownloadChunk, DaemonError>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _callerRegistration;
        private ITimer? _timer;
        private CancellationToken _connectionToken;
        private Func<JsonRpcRequestId, IV2ClientPendingRequest, bool>? _removeFromRequestMap;
        private int _sendStarted;
        private int _callerCompleted;
        private int _drainCompleted;

        internal PendingDownload(
            V2ClientDownloadCoordinator owner,
            JsonRpcRequestId requestId,
            DownloadChunkRequest request,
            Action<PendingDownload> drained,
            TimeProvider timeProvider,
            TimeSpan requestTimeout,
            TimeSpan abandonedDrainTimeout)
        {
            _owner = owner;
            RequestId = requestId;
            Request = request;
            _drained = drained;
            _timeProvider = timeProvider;
            _requestTimeout = requestTimeout;
            _abandonedDrainTimeout = abandonedDrainTimeout;
        }

        internal JsonRpcRequestId RequestId { get; }
        internal DownloadChunkRequest Request { get; }
        internal DownloadReadResult? Metadata { get; set; }
        internal bool SendStarted => Volatile.Read(ref _sendStarted) != 0;
        internal bool IsAbandoned { get; set; }
        internal bool IsCallerCompleted => Volatile.Read(ref _callerCompleted) != 0;
        internal Task<Result<DownloadChunk, DaemonError>> Task => _completion.Task;
        public bool IsCompleted => Volatile.Read(ref _drainCompleted) != 0;
        public CancellationToken SendCancellationToken => _connectionToken;

        internal void SetRequestMapRemoval(
            Func<JsonRpcRequestId, IV2ClientPendingRequest, bool> removeFromRequestMap) =>
            _removeFromRequestMap = removeFromRequestMap ?? throw new ArgumentNullException(nameof(removeFromRequestMap));

        internal void RemoveFromRequestMap() => _removeFromRequestMap?.Invoke(RequestId, this);

        internal void Register(CancellationToken callerToken, CancellationToken connectionToken = default)
        {
            _connectionToken = connectionToken;
            if (callerToken.CanBeCanceled)
            {
                StoreRegistration(callerToken.Register(static state =>
                {
                    var pair = ((PendingDownload Pending, CancellationToken Token))state!;
                    pair.Pending._owner.Cancel(pair.Pending, pair.Token);
                }, (this, callerToken)));
            }
            StoreTimer(_timeProvider.CreateTimer(static state =>
            {
                var pending = (PendingDownload)state!;
                pending._owner.Timeout(pending);
            }, this, _requestTimeout, System.Threading.Timeout.InfiniteTimeSpan));
        }

        internal void MarkSendStarted() => _owner.TryMarkSendStarted(this);
        internal void MarkSendStartedCore() => Volatile.Write(ref _sendStarted, 1);

        internal bool TryRouteMetadata(JsonRpcObjectPayload payload)
        {
            DownloadReadResult metadata;
            try
            {
                metadata = (DownloadReadResult)payload.Deserialize(
                    BuiltInProtocolJsonContext.Default.DownloadReadResult);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException)
            {
                _owner.PoisonAndDrain(this, ProtocolError(
                    "protocol.result_invalid",
                    "The download read result violates its descriptor metadata."));
                _owner._protocolFault("The download read result violates its descriptor metadata.");
                return true;
            }
            return _owner.RouteMetadata(this, metadata);
        }

        internal bool TryRouteError(DaemonError error) => _owner.RouteError(this, error);
        internal void FailSend(DaemonError error) => _owner.FailSend(this, error);

        public bool TrySuccess(JsonRpcObjectPayload payload) => TryRouteMetadata(payload);
        public bool TryError(DaemonError error) => TryRouteError(error);
        public void DisposeRegistrations() => DisposeTracking();
        public void DisposeSendLifetime()
        {
        }

        internal void FinishBinary(DownloadChunk chunk)
        {
            Drain();
            TrySetCaller(() => _completion.TrySetResult(Result.Ok<DownloadChunk, DaemonError>(chunk)));
        }

        internal void FinishCanceled(CancellationToken token) =>
            TrySetCaller(() => _completion.TrySetCanceled(token));

        internal void FinishError(DaemonError error) =>
            TrySetCaller(() => _completion.TrySetResult(Result.Err<DownloadChunk, DaemonError>(error)));

        internal void FinishClose(DaemonError error)
        {
            Drain();
            FinishError(error);
        }

        internal bool TryReserveCallerCompletion() =>
            Interlocked.CompareExchange(ref _callerCompleted, 1, 0) == 0;

        internal void CompleteReservedCanceled(CancellationToken token) =>
            _completion.TrySetCanceled(token);

        internal void CompleteReservedError(DaemonError error) =>
            _completion.TrySetResult(Result.Err<DownloadChunk, DaemonError>(error));

        internal void BeginAbandonedDrain()
        {
            DisposeTracking();
            StoreTimer(_timeProvider.CreateTimer(static state =>
            {
                var pending = (PendingDownload)state!;
                pending._owner.AbandonedDrainExpired(pending);
            }, this, _abandonedDrainTimeout, System.Threading.Timeout.InfiniteTimeSpan));
        }

        internal void Drain()
        {
            if (Interlocked.CompareExchange(ref _drainCompleted, 1, 0) != 0)
                return;
            DisposeTracking();
            _drained(this);
        }

        private void TrySetCaller(Func<bool> complete)
        {
            if (Interlocked.CompareExchange(ref _callerCompleted, 1, 0) != 0)
                return;
            if (!IsAbandoned)
                DisposeTracking();
            complete();
        }

        private void StoreRegistration(CancellationTokenRegistration registration)
        {
            var dispose = false;
            lock (_resourceGate)
            {
                if (Volatile.Read(ref _drainCompleted) != 0)
                    dispose = true;
                else
                    _callerRegistration = registration;
            }
            if (dispose)
                registration.Dispose();
        }

        private void StoreTimer(ITimer timer)
        {
            ITimer? previous = null;
            var dispose = false;
            lock (_resourceGate)
            {
                if (Volatile.Read(ref _drainCompleted) != 0)
                    dispose = true;
                else
                {
                    previous = _timer;
                    _timer = timer;
                }
            }
            previous?.Dispose();
            if (dispose)
                timer.Dispose();
        }

        private void DisposeTracking()
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
            registration.Dispose();
            timer?.Dispose();
        }
    }
}

internal enum BinaryRoute
{
    Completed,
    Ignored,
    ProtocolFault
}
