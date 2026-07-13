using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Serialization.V2;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class V2ClientConnectionCore
{
    private const string ClosedCode = "connection.closed";
    private readonly IV2ClientWireTransport _transport;
    private readonly Func<JsonRpcRequestId> _idFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly Action<V2ClientDiagnostic>? _diagnostic;
    private readonly Action<JsonRpcRemoteEventNotification>? _remoteEvent;
    private readonly Action<UploadChunkAcknowledgement>? _uploadAcknowledgement;
    private readonly Action _sendLifetimeCreatedCallback;
    private readonly Action _sendLifetimeDisposedCallback;
    private readonly Func<JsonRpcRequestId, IPendingRequest, bool> _removePendingCallback;
    private readonly ConcurrentDictionary<JsonRpcRequestId, IPendingRequest> _pending = new();
    private readonly CancellationTokenSource _connectionCancellation = new();
    private readonly object _admissionLock = new();
    private readonly object _sendObserverLock = new();
    private TaskCompletionSource _sendObserversDrained = CompletedSignal();
    private int _sendObserverCount;
    private int _activeSendLifetimeCount;
    private bool _closing;

    public V2ClientConnectionCore(
        IV2ClientWireTransport transport,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        Func<JsonRpcRequestId>? idFactory = null,
        Action<V2ClientDiagnostic>? diagnostic = null,
        Action<JsonRpcRemoteEventNotification>? remoteEvent = null,
        Action<UploadChunkAcknowledgement>? uploadAcknowledgement = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _requestTimeout = requestTimeout;
        _idFactory = idFactory ?? (() => JsonRpcRequestId.FromString(Guid.NewGuid().ToString("D")));
        _diagnostic = diagnostic;
        _remoteEvent = remoteEvent;
        _uploadAcknowledgement = uploadAcknowledgement;
        _sendLifetimeCreatedCallback = SendLifetimeCreated;
        _sendLifetimeDisposedCallback = SendLifetimeDisposed;
        _removePendingCallback = RemovePending;
    }

    internal int PendingCount => _pending.Count;
    internal int SendObserverCount => Volatile.Read(ref _sendObserverCount);
    internal int ActiveSendLifetimeCount => Volatile.Read(ref _activeSendLifetimeCount);

    internal Task WaitForSendObserversAsync()
    {
        lock (_sendObserverLock)
        {
            return _sendObserversDrained.Task;
        }
    }

    public async Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var id = _idFactory();
        var pending = new PendingRequest<TResult>(
            id,
            descriptor.ResultTypeInfo,
            _removePendingCallback,
            _sendLifetimeCreatedCallback,
            _sendLifetimeDisposedCallback);
        var bytes = V2RequestWriter.Write(descriptor, id, request);
        pending.Register(cancellationToken, _connectionCancellation.Token, _requestTimeout, _timeProvider);
        ValueTask send = default;
        var admitted = false;

        lock (_admissionLock)
        {
            if (_closing)
            {
                pending.TryError(ClosedError());
            }
            else if (!pending.IsCompleted)
            {
                if (!_pending.TryAdd(id, pending))
                {
                    pending.DisposeRegistrations();
                    pending.DisposeSendLifetime();
                    throw new InvalidOperationException("The V2 request id factory produced a duplicate identifier.");
                }

                if (!pending.IsCompleted)
                {
                    try
                    {
                        send = _transport.SendTextAsync(bytes, pending.SendCancellationToken);
                        RegisterSendObserver();
                        admitted = true;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        pending.TryError(new TransportDaemonError("transport.send_failed", "The V2 request could not be sent."));
                    }
                }
                else
                {
                    RemovePending(id, pending);
                }
            }
        }

        if (admitted)
        {
            _ = ObserveSendAsync(send, pending);
        }
        else
        {
            pending.DisposeSendLifetime();
        }

        return await pending.Task.ConfigureAwait(false);
    }

    public async Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
        RpcDescriptor<TRequest, UnitResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeAsync(descriptor, request, cancellationToken).ConfigureAwait(false);
        return result.IsOk(out _)
            ? Result.Ok<Unit, DaemonError>(Unit.Default)
            : Result.Err<Unit, DaemonError>(result.UnwrapErr());
    }

    public void RouteText(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var shape = Classify(utf8Json);
            switch (shape)
            {
                case EnvelopeShape.Success:
                    RouteSuccess(JsonRpcWireParser.ParseSuccessResponse(utf8Json));
                    break;
                case EnvelopeShape.Error:
                    RouteError(JsonRpcWireParser.ParseErrorResponse(utf8Json));
                    break;
                case EnvelopeShape.Notification:
                    RouteNotification(utf8Json);
                    break;
                default:
                    ProtocolFault("The complete text message is not a mapped V2 envelope.");
                    break;
            }
        }
        catch (JsonException)
        {
            ProtocolFault("The complete text message violates the V2 JSON-RPC profile.");
        }
    }

    public void Close()
    {
        KeyValuePair<JsonRpcRequestId, IPendingRequest>[] snapshot;
        lock (_admissionLock)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            snapshot = _pending.ToArray();
        }

        try
        {
            _connectionCancellation.Cancel();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        finally
        {
            foreach (var item in snapshot)
            {
                item.Value.TryError(ClosedError());
            }
        }
    }

    private void RouteSuccess(JsonRpcSuccessResponseEnvelope response)
    {
        if (!_pending.TryGetValue(response.Id, out var pending))
        {
            UnknownResponse();
            return;
        }

        if (!pending.TrySuccess(response.Result))
        {
            UnknownResponse();
        }
    }

    private void RouteError(JsonRpcErrorResponseEnvelope response)
    {
        if (response.Id is null || !_pending.TryGetValue(response.Id, out var pending))
        {
            UnknownResponse();
            return;
        }

        if (!pending.TryError(MapError(response.Error)))
        {
            UnknownResponse();
        }
    }

    private void RouteNotification(ReadOnlySpan<byte> utf8Json)
    {
        JsonRpcUploadAcknowledgementNotification? acknowledgement = null;
        try
        {
            acknowledgement = JsonRpcWireParser.ParseUploadAcknowledgementNotification(utf8Json);
        }
        catch (JsonException)
        {
        }

        if (acknowledgement is not null)
        {
            InvokeConsumer(() => _uploadAcknowledgement?.Invoke(acknowledgement.Params));
            return;
        }

        JsonRpcRemoteEventNotification remoteEvent;
        try
        {
            remoteEvent = JsonRpcWireParser.ParseRemoteEventNotification(utf8Json);
        }
        catch (JsonException)
        {
            EmitDiagnostic(new(V2ClientDiagnosticKind.UnknownNotification, "The V2 notification method is not mapped."));
            return;
        }

        InvokeConsumer(() => _remoteEvent?.Invoke(remoteEvent));
    }

    private void InvokeConsumer(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            EmitDiagnostic(new(V2ClientDiagnosticKind.ConsumerFault, "A V2 notification consumer failed."));
        }
    }

    private async Task ObserveSendAsync(ValueTask send, IPendingRequest pending)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pending.SendCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pending.TryError(new TransportDaemonError("transport.send_failed", "The V2 request could not be sent."));
        }
        finally
        {
            pending.DisposeSendLifetime();
            CompleteSendObserver();
        }
    }

    private void RegisterSendObserver()
    {
        lock (_sendObserverLock)
        {
            if (_sendObserverCount++ == 0)
            {
                _sendObserversDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void CompleteSendObserver()
    {
        TaskCompletionSource? completed = null;
        lock (_sendObserverLock)
        {
            if (--_sendObserverCount == 0)
            {
                completed = _sendObserversDrained;
            }
        }

        completed?.TrySetResult();
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }

    private void SendLifetimeCreated() => Interlocked.Increment(ref _activeSendLifetimeCount);

    private void SendLifetimeDisposed() => Interlocked.Decrement(ref _activeSendLifetimeCount);

    private bool RemovePending(JsonRpcRequestId id, IPendingRequest value) =>
        _pending.TryRemove(new KeyValuePair<JsonRpcRequestId, IPendingRequest>(id, value));

    private void UnknownResponse() =>
        EmitDiagnostic(new(V2ClientDiagnosticKind.UnknownResponse, "The V2 response id has no pending request."));

    private void ProtocolFault(string message) =>
        EmitDiagnostic(new(V2ClientDiagnosticKind.ProtocolFault, message));

    private void EmitDiagnostic(V2ClientDiagnostic diagnostic)
    {
        try
        {
            _diagnostic?.Invoke(diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    private static TransportDaemonError ClosedError() =>
        new(ClosedCode, "The V2 connection is closed.");

    private static DaemonError MapError(JsonRpcErrorObject error)
    {
        var code = error.Data.DaemonErrorCode ?? $"jsonrpc.{error.Code}";
        var details = error.Data.Details;
        return error.Data.DaemonErrorKind switch
        {
            DaemonErrorWireKind.Validation => new ValidationDaemonError(code, error.Message, details),
            DaemonErrorWireKind.NotFound => new NotFoundDaemonError(code, error.Message, details),
            DaemonErrorWireKind.Conflict => new ConflictDaemonError(code, error.Message, details),
            DaemonErrorWireKind.Permission => new PermissionDaemonError(code, error.Message, details),
            DaemonErrorWireKind.Storage => new StorageDaemonError(code, error.Message, details),
            DaemonErrorWireKind.Transport => new TransportDaemonError(code, error.Message, details),
            DaemonErrorWireKind.Internal => new InternalDaemonError(code, error.Message, details),
            _ => throw new JsonException("The V2 error response has an unsupported daemon error kind.")
        };
    }

    private static EnvelopeShape Classify(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var properties = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var name = reader.GetString()!;
            if (!properties.Add(name) || !reader.Read())
            {
                throw new JsonException();
            }

            reader.Skip();
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            throw new JsonException();
        }

        return properties.SetEquals(["jsonrpc", "id", "result"]) ? EnvelopeShape.Success
            : properties.SetEquals(["jsonrpc", "id", "error"]) ? EnvelopeShape.Error
            : properties.SetEquals(["jsonrpc", "method", "params"]) ? EnvelopeShape.Notification
            : EnvelopeShape.Unknown;
    }

    private enum EnvelopeShape { Unknown, Success, Error, Notification }

    private interface IPendingRequest
    {
        bool IsCompleted { get; }
        CancellationToken SendCancellationToken { get; }
        bool TrySuccess(JsonRpcObjectPayload payload);
        bool TryError(DaemonError error);
        void DisposeRegistrations();
        void DisposeSendLifetime();
    }

    private sealed class PendingRequest<TResult> : IPendingRequest where TResult : notnull
    {
        private readonly JsonRpcRequestId _id;
        private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> _resultTypeInfo;
        private readonly Func<JsonRpcRequestId, IPendingRequest, bool> _remove;
        private readonly Action _sendLifetimeCreated;
        private readonly Action _sendLifetimeDisposedCallback;
        private readonly TaskCompletionSource<Result<TResult, DaemonError>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _callerRegistration;
        private CancellationTokenSource? _sendCancellation;
        private ITimer? _timeoutTimer;
        private readonly object _sendLifetimeLock = new();
        private bool _sendLifetimeDisposed;
        private int _won;

        public PendingRequest(
            JsonRpcRequestId id,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo,
            Func<JsonRpcRequestId, IPendingRequest, bool> remove,
            Action sendLifetimeCreated,
            Action sendLifetimeDisposed)
        {
            _id = id;
            _resultTypeInfo = resultTypeInfo;
            _remove = remove;
            _sendLifetimeCreated = sendLifetimeCreated;
            _sendLifetimeDisposedCallback = sendLifetimeDisposed;
        }

        public Task<Result<TResult, DaemonError>> Task => _completion.Task;
        public bool IsCompleted => Volatile.Read(ref _won) != 0;
        public CancellationToken SendCancellationToken => _sendCancellation?.Token ?? CancellationToken.None;

        public void Register(CancellationToken callerToken, CancellationToken connectionToken, TimeSpan timeout, TimeProvider timeProvider)
        {
            _sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken, connectionToken);
            _sendLifetimeCreated();
            if (callerToken.CanBeCanceled)
            {
                var registration = callerToken.Register(static state =>
                {
                    var pair = ((PendingRequest<TResult>, CancellationToken))state!;
                    pair.Item1.TryCancel(pair.Item2);
                }, (this, callerToken));
                _callerRegistration = registration;
                if (Volatile.Read(ref _won) != 0)
                {
                    registration.Dispose();
                }
            }

            var timer = timeProvider.CreateTimer(static state =>
            {
                var pending = (PendingRequest<TResult>)state!;
                pending.CancelSend();
                pending.TryError(new TransportDaemonError("request.timeout", "The V2 request timed out."));
            },
                this, timeout, Timeout.InfiniteTimeSpan);
            _timeoutTimer = timer;
            if (Volatile.Read(ref _won) != 0)
            {
                timer.Dispose();
            }
        }

        public void DisposeRegistrations()
        {
            _callerRegistration.Dispose();
            _timeoutTimer?.Dispose();
        }

        public void DisposeSendLifetime()
        {
            lock (_sendLifetimeLock)
            {
                if (_sendLifetimeDisposed)
                {
                    return;
                }

                _sendLifetimeDisposed = true;
                _sendCancellation?.Dispose();
                _sendLifetimeDisposedCallback();
            }
        }

        public bool TrySuccess(JsonRpcObjectPayload payload)
        {
            TResult result;
            try
            {
                result = (TResult)payload.Deserialize(_resultTypeInfo);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException)
            {
                return TryError(new TransportDaemonError("protocol.result_invalid", "The V2 response result violates its descriptor metadata."));
            }

            return TryComplete(() => _completion.TrySetResult(Result.Ok<TResult, DaemonError>(result)));
        }

        public bool TryError(DaemonError error) =>
            TryComplete(() => _completion.TrySetResult(Result.Err<TResult, DaemonError>(error)));

        public bool TryCancel(CancellationToken token) =>
            TryComplete(() => _completion.TrySetCanceled(token));

        private bool TryComplete(Func<bool> complete)
        {
            if (Interlocked.CompareExchange(ref _won, 1, 0) != 0)
            {
                return false;
            }

            CancelSend();
            _remove(_id, this);
            DisposeRegistrations();
            complete();
            return true;
        }

        private void CancelSend()
        {
            lock (_sendLifetimeLock)
            {
                if (_sendLifetimeDisposed || _sendCancellation is null)
                {
                    return;
                }

                try
                {
                    _sendCancellation.Cancel();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                }
            }
        }
    }
}
