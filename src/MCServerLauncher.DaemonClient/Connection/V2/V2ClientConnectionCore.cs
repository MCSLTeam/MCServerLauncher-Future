using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Files;
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
    private readonly Func<JsonRpcRequestId, IV2ClientPendingRequest, bool> _removePendingCallback;
    private readonly V2ClientUploadCoordinator _uploadCoordinator;
    private readonly V2ClientDownloadCoordinator _downloadCoordinator;
    private readonly ConcurrentDictionary<JsonRpcRequestId, IV2ClientPendingRequest> _pending = new();
    private readonly CancellationTokenSource _connectionCancellation = new();
    private readonly TaskCompletionSource _closed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        : this(
            transport,
            timeProvider,
            requestTimeout,
            idFactory,
            diagnostic,
            remoteEvent,
            uploadAcknowledgement,
            uploadAdmissionTestGate: null)
    {
    }

    internal V2ClientConnectionCore(
        IV2ClientWireTransport transport,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        V2ClientUploadAdmissionTestGate uploadAdmissionTestGate)
        : this(
            transport,
            timeProvider,
            requestTimeout,
            idFactory: null,
            diagnostic: null,
            remoteEvent: null,
            uploadAcknowledgement: null,
            uploadAdmissionTestGate: uploadAdmissionTestGate ??
                throw new ArgumentNullException(nameof(uploadAdmissionTestGate)))
    {
    }

    private V2ClientConnectionCore(
        IV2ClientWireTransport transport,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        Func<JsonRpcRequestId>? idFactory,
        Action<V2ClientDiagnostic>? diagnostic,
        Action<JsonRpcRemoteEventNotification>? remoteEvent,
        Action<UploadChunkAcknowledgement>? uploadAcknowledgement,
        V2ClientUploadAdmissionTestGate? uploadAdmissionTestGate)
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
        _uploadCoordinator = new(
            timeProvider,
            requestTimeout,
            _sendLifetimeCreatedCallback,
            _sendLifetimeDisposedCallback,
            uploadAdmissionTestGate);
        _downloadCoordinator = new(timeProvider, requestTimeout, ProtocolFault);
    }

    internal int PendingCount => _pending.Count;
    internal int UploadPendingCount => _uploadCoordinator.PendingCount;
    internal int DownloadPendingCount => _downloadCoordinator.PendingCount;
    internal int AbandonedDownloadCount => _downloadCoordinator.AbandonedDrainCount;
    internal int SendObserverCount => Volatile.Read(ref _sendObserverCount);
    internal int ActiveSendLifetimeCount => Volatile.Read(ref _activeSendLifetimeCount);
    internal Task Closed => _closed.Task;

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
        var operation = InvokeTracked(descriptor, request, cancellationToken);
        return await operation.Completion.ConfigureAwait(false);
    }

    internal V2ClientInvocationOperation<TResult> InvokeTracked<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var id = _idFactory();
        var outcome = new V2ClientInvocationOutcome();
        var pending = new PendingRequest<TResult>(
            id,
            descriptor.ResultTypeInfo,
            outcome,
            _removePendingCallback,
            _sendLifetimeCreatedCallback,
            _sendLifetimeDisposedCallback);
        var operation = new V2ClientInvocationOperation<TResult>(pending.Task, outcome);
        ImmutableArray<byte> bytes;
        try
        {
            bytes = V2RequestWriter.Write(descriptor, id, request);
            pending.Register(cancellationToken, _connectionCancellation.Token, _requestTimeout, _timeProvider);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pending.TryException(exception);
            pending.DisposeSendLifetime();
            return operation;
        }

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
                    pending.TryException(new InvalidOperationException(
                        "The V2 request id factory produced a duplicate identifier."));
                }
                else if (!pending.IsCompleted && pending.TryMarkAdmitted())
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

        return operation;
    }

    public async Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
        RpcDescriptor<TRequest, UnitResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var operation = InvokeUnitTracked(descriptor, request, cancellationToken);
        return await operation.Completion.ConfigureAwait(false);
    }

    internal V2ClientInvocationOperation<Unit> InvokeUnitTracked<TRequest>(
        RpcDescriptor<TRequest, UnitResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var operation = InvokeTracked(descriptor, request, cancellationToken);
        return new V2ClientInvocationOperation<Unit>(MapUnitAsync(operation.Completion), operation.Outcome);
    }

    private static async Task<Result<Unit, DaemonError>> MapUnitAsync(
        Task<Result<UnitResult, DaemonError>> completion)
    {
        var result = await completion.ConfigureAwait(false);
        return result.IsOk(out _)
            ? Result.Ok<Unit, DaemonError>(Unit.Default)
            : Result.Err<Unit, DaemonError>(result.UnwrapErr());
    }

    internal Task<Result<Unit, DaemonError>> SendUploadChunkAsync(
        UploadChunkRequest request,
        int maximumChunkSize,
        CancellationToken cancellationToken) =>
        SendUploadChunkTracked(request, maximumChunkSize, cancellationToken).Completion;

    internal V2ClientInvocationOperation<Unit> SendUploadChunkTracked(
        UploadChunkRequest request,
        int maximumChunkSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validationError = ValidateUploadChunk(request, maximumChunkSize);
        if (validationError is not null)
            return NotAdmitted(Result.Err<Unit, DaemonError>(validationError));

        var frameBytes = new byte[BinaryFrameCodec.HeaderSize + request.Data.Length];
        var header = new BinaryFrameHeader(
            BinaryFrameKind.UploadChunk,
            request.SessionId,
            request.Offset,
            checked((uint)request.Data.Length));
        if (!BinaryFrameCodec.TryWrite(
                frameBytes,
                header,
                request.Data.AsSpan(),
                out var writeError,
                checked((uint)maximumChunkSize)))
        {
            throw new InvalidOperationException($"The validated upload frame could not be encoded: {writeError}.");
        }

        var frame = ImmutableCollectionsMarshal.AsImmutableArray(frameBytes);
        V2ClientUploadCoordinator.PendingUpload? pending;
        DaemonError? admissionError;
        ValueTask send;

        lock (_admissionLock)
        {
            if (_closing)
                return NotAdmitted(Result.Err<Unit, DaemonError>(ClosedError()));

            if (cancellationToken.IsCancellationRequested)
                return NotAdmittedCanceled<Unit>(cancellationToken);

            if (!_uploadCoordinator.TryAdmit(
                    request.SessionId,
                    request.Offset,
                    request.Data.Length,
                    out pending,
                    out admissionError))
            {
                return NotAdmitted(Result.Err<Unit, DaemonError>(admissionError!));
            }

            RegisterSendObserver();
            pending!.CreateSendLifetime();
        }

        try
        {
            pending!.Register(cancellationToken);
            if (pending.IsCompleted ||
                _connectionCancellation.IsCancellationRequested)
            {
                pending.DisposeSendLifetime();
                CompleteSendObserver();
                return pending.Operation;
            }

            send = _transport.SendBinaryAsync(frame, _connectionCancellation.Token);
        }
        catch (OperationCanceledException) when (_connectionCancellation.IsCancellationRequested)
        {
            _uploadCoordinator.FailSend(pending!, ClosedError());
            pending!.DisposeSendLifetime();
            CompleteSendObserver();
            return pending.Operation;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _uploadCoordinator.FailSend(
                pending!,
                new TransportDaemonError("transport.send_failed", "The V2 upload chunk could not be sent."));
            pending!.DisposeSendLifetime();
            CompleteSendObserver();
            ProtocolFault("The V2 upload binary send failed.");
            return pending.Operation;
        }

        _ = ObserveUploadSendAsync(send, pending!);
        return pending!.Operation;
    }

    internal bool TryRegisterDownloadSession(DownloadSession session, out DaemonError? error) =>
        _downloadCoordinator.TryRegisterSession(session, out error);

    internal bool TryRemoveDownloadSession(Guid sessionId, out DaemonError? error) =>
        _downloadCoordinator.TryRemoveSession(sessionId, out error);

    internal Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = _idFactory();
        var bytes = V2RequestWriter.Write(BuiltInProtocolDefinitions.ReadDownload, id, request);
        V2ClientDownloadCoordinator.PendingDownload? pending;
        DaemonError? admissionError;
        ValueTask send;
        var sendReservation = false;

        lock (_admissionLock)
        {
            if (_closing)
                return Task.FromResult(Result.Err<DownloadChunk, DaemonError>(ClosedError()));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<Result<DownloadChunk, DaemonError>>(cancellationToken);
            if (!_downloadCoordinator.TryAdmit(
                    id,
                    request,
                    static pending => pending.RemoveFromRequestMap(),
                    out pending,
                    out admissionError))
            {
                return Task.FromResult(Result.Err<DownloadChunk, DaemonError>(admissionError!));
            }
            pending!.SetRequestMapRemoval(_removePendingCallback);
            if (!_pending.TryAdd(id, pending))
            {
                _downloadCoordinator.RejectAdmission(pending);
                throw new InvalidOperationException("The V2 request id factory produced a duplicate identifier.");
            }
            RegisterSendObserver();
            SendLifetimeCreated();
            sendReservation = true;
        }

        try
        {
            pending!.Register(cancellationToken, _connectionCancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _downloadCoordinator.FailRegistration(
                pending!,
                new TransportDaemonError(
                    "transport.request_registration_failed",
                    $"The V2 download request could not register cancellation or timeout tracking: {exception.GetType().Name}."));
            CompleteDownloadSendReservation(ref sendReservation);
            return pending!.Task;
        }

        if (pending!.IsCallerCompleted ||
            _connectionCancellation.IsCancellationRequested ||
            !_downloadCoordinator.TryMarkSendStarted(pending))
        {
            CompleteDownloadSendReservation(ref sendReservation);
            return pending.Task;
        }

        try
        {
            send = _transport.SendTextAsync(bytes, _connectionCancellation.Token);
        }
        catch (OperationCanceledException) when (_connectionCancellation.IsCancellationRequested)
        {
            pending.FailSend(ClosedError());
            CompleteDownloadSendReservation(ref sendReservation);
            return pending.Task;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pending.FailSend(new TransportDaemonError(
                "transport.send_failed",
                "The V2 download chunk request could not be sent."));
            CompleteDownloadSendReservation(ref sendReservation);
            ProtocolFault("The V2 download chunk request send failed.");
            return pending.Task;
        }

        sendReservation = false;
        _ = ObserveDownloadSendAsync(send, pending);
        return pending.Task;
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

    public void RouteBinary(ReadOnlySpan<byte> frame)
    {
        if (!BinaryFrameCodec.TryRead(frame, out var result))
        {
            ProtocolFault($"The V2 binary frame is invalid: {result.Error}.");
            return;
        }

        _downloadCoordinator.RouteBinary(result.Header!, frame[BinaryFrameCodec.HeaderSize..]);
    }

    public void Close()
    {
        lock (_admissionLock)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
        }

        try
        {
            // Cancel first so a synchronously blocking transport send observes cancellation
            // without waiting for coordinator cleanup or pending completion work.
            try
            {
                _connectionCancellation.Cancel();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }

            var snapshot = _pending.ToArray();
            try
            {
                _uploadCoordinator.Close(ClosedError());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }

            try
            {
                _downloadCoordinator.Close(ClosedError());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }

            foreach (var item in snapshot)
            {
                try
                {
                    item.Value.TryError(ClosedError());
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                }
            }
        }
        finally
        {
            _closed.TrySetResult();
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

        var error = MapError(response.Error);
        var completed = pending is IV2ClientTrackedPendingRequest tracked
            ? tracked.TryResponseError(error)
            : pending.TryError(error);
        if (!completed)
        {
            UnknownResponse();
        }
    }

    private void RouteNotification(ReadOnlySpan<byte> utf8Json)
    {
        if (HasMethod(utf8Json, JsonRpcWireConstants.UploadAcknowledgementMethod))
        {
            JsonRpcUploadAcknowledgementNotification acknowledgement;
            try
            {
                acknowledgement = JsonRpcWireParser.ParseUploadAcknowledgementNotification(utf8Json);
            }
            catch (JsonException)
            {
                ProtocolFault("The upload acknowledgement violates the V2 JSON-RPC profile.");
                return;
            }

            var route = _uploadCoordinator.RouteAcknowledgement(acknowledgement.Params);
            if (route == UploadAcknowledgementRoute.Ignored)
            {
                EmitDiagnostic(new(
                    V2ClientDiagnosticKind.UnknownNotification,
                    "The upload acknowledgement has no pending chunk."));
            }
            else if (route == UploadAcknowledgementRoute.ProtocolFault)
            {
                ProtocolFault("The upload acknowledgement does not match the pending chunk.");
            }

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

    private async Task ObserveSendAsync(ValueTask send, IV2ClientPendingRequest pending)
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

    private async Task ObserveUploadSendAsync(
        ValueTask send,
        V2ClientUploadCoordinator.PendingUpload pending)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_connectionCancellation.IsCancellationRequested)
        {
            _uploadCoordinator.FailSend(pending, ClosedError());
            ProtocolFault("The V2 upload binary send was canceled by connection termination.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _uploadCoordinator.FailSend(
                pending,
                new TransportDaemonError("transport.send_failed", "The V2 upload chunk could not be sent."));
            ProtocolFault("The V2 upload binary send failed.");
        }
        finally
        {
            pending.DisposeSendLifetime();
            CompleteSendObserver();
        }
    }

    private async Task ObserveDownloadSendAsync(
        ValueTask send,
        V2ClientDownloadCoordinator.PendingDownload pending)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_connectionCancellation.IsCancellationRequested)
        {
            pending.FailSend(ClosedError());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pending.FailSend(new TransportDaemonError(
                "transport.send_failed",
                "The V2 download chunk request could not be sent."));
            ProtocolFault("The V2 download chunk request send failed.");
        }
        finally
        {
            SendLifetimeDisposed();
            CompleteSendObserver();
        }
    }

    private void CompleteDownloadSendReservation(ref bool reservation)
    {
        if (!reservation)
            return;
        reservation = false;
        SendLifetimeDisposed();
        CompleteSendObserver();
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

    private bool RemovePending(JsonRpcRequestId id, IV2ClientPendingRequest value) =>
        _pending.TryRemove(new KeyValuePair<JsonRpcRequestId, IV2ClientPendingRequest>(id, value));

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

    private static V2ClientInvocationOperation<TResult> NotAdmitted<TResult>(
        Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        var outcome = new V2ClientInvocationOutcome();
        outcome.TryComplete(authoritativeResponse: false);
        return new(Task.FromResult(result), outcome);
    }

    private static V2ClientInvocationOperation<TResult> NotAdmittedCanceled<TResult>(
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        var outcome = new V2ClientInvocationOutcome();
        outcome.TryComplete(authoritativeResponse: false);
        return new(Task.FromCanceled<Result<TResult, DaemonError>>(cancellationToken), outcome);
    }

    internal static DaemonError MapError(JsonRpcErrorObject error)
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

    private static DaemonError? ValidateUploadChunk(UploadChunkRequest request, int maximumChunkSize)
    {
        if (request.Data.IsDefault)
            return new ValidationDaemonError("file.chunk.data.invalid", "The upload chunk data is required.");
        if (request.SessionId == Guid.Empty)
            return new ValidationDaemonError("file.session.invalid", "The upload session identifier cannot be empty.");
        if (request.Offset < 0)
            return new ValidationDaemonError("file.chunk.offset.invalid", "The upload chunk offset cannot be negative.");
        if (maximumChunkSize is <= 0 or > (int)BinaryFrameCodec.DefaultMaximumChunkSize)
            return new ValidationDaemonError("file.chunk.size.invalid", "The upload maximum chunk size is invalid.");
        if (request.Data.Length > maximumChunkSize)
            return new ValidationDaemonError("file.chunk.size.invalid", "The upload chunk exceeds the maximum size.");
        return null;
    }

    private static bool HasMethod(ReadOnlySpan<byte> utf8Json, string expectedMethod)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                return false;

            var isMethod = reader.ValueTextEquals("method"u8);
            if (!reader.Read())
                return false;
            if (isMethod)
                return reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(expectedMethod);
            reader.Skip();
        }

        return false;
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

    private sealed class PendingRequest<TResult> : IV2ClientTrackedPendingRequest where TResult : notnull
    {
        private readonly JsonRpcRequestId _id;
        private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> _resultTypeInfo;
        private readonly V2ClientInvocationOutcome _outcome;
        private readonly Func<JsonRpcRequestId, IV2ClientPendingRequest, bool> _remove;
        private readonly Action _sendLifetimeCreated;
        private readonly Action _sendLifetimeDisposedCallback;
        private readonly TaskCompletionSource<Result<TResult, DaemonError>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _callerRegistration;
        private CancellationTokenSource? _sendCancellation;
        private ITimer? _timeoutTimer;
        private readonly object _sendLifetimeLock = new();
        private bool _sendLifetimeActive;
        private bool _sendLifetimeDisposed;

        public PendingRequest(
            JsonRpcRequestId id,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultTypeInfo,
            V2ClientInvocationOutcome outcome,
            Func<JsonRpcRequestId, IV2ClientPendingRequest, bool> remove,
            Action sendLifetimeCreated,
            Action sendLifetimeDisposed)
        {
            _id = id;
            _resultTypeInfo = resultTypeInfo;
            _outcome = outcome;
            _remove = remove;
            _sendLifetimeCreated = sendLifetimeCreated;
            _sendLifetimeDisposedCallback = sendLifetimeDisposed;
        }

        public Task<Result<TResult, DaemonError>> Task => _completion.Task;
        public bool IsCompleted => _outcome.IsCompleted;
        public CancellationToken SendCancellationToken => _sendCancellation?.Token ?? CancellationToken.None;

        public bool TryMarkAdmitted() => _outcome.TryMarkAdmitted();

        public void Register(CancellationToken callerToken, CancellationToken connectionToken, TimeSpan timeout, TimeProvider timeProvider)
        {
            _sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken, connectionToken);
            lock (_sendLifetimeLock)
            {
                _sendLifetimeActive = true;
            }
            _sendLifetimeCreated();
            if (callerToken.CanBeCanceled)
            {
                var registration = callerToken.Register(static state =>
                {
                    var pair = ((PendingRequest<TResult>, CancellationToken))state!;
                    pair.Item1.TryCancel(pair.Item2);
                }, (this, callerToken));
                _callerRegistration = registration;
                if (_outcome.IsCompleted)
                {
                    registration.Dispose();
                }
            }

            if (_outcome.IsCompleted)
            {
                return;
            }

            var timer = timeProvider.CreateTimer(static state =>
            {
                var pending = (PendingRequest<TResult>)state!;
                pending.CancelSend();
                pending.TryError(new TransportDaemonError("request.timeout", "The V2 request timed out."));
            },
                this, timeout, Timeout.InfiniteTimeSpan);
            _timeoutTimer = timer;
            if (_outcome.IsCompleted)
            {
                timer.Dispose();
            }
        }

        public void DisposeRegistrations()
        {
            try
            {
                _callerRegistration.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }

            try
            {
                _timeoutTimer?.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }

        public void DisposeSendLifetime()
        {
            lock (_sendLifetimeLock)
            {
                if (_sendLifetimeDisposed || !_sendLifetimeActive)
                {
                    return;
                }

                _sendLifetimeDisposed = true;
                _sendLifetimeActive = false;
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

            return TryComplete(
                authoritativeResponse: true,
                () => _completion.TrySetResult(Result.Ok<TResult, DaemonError>(result)));
        }

        public bool TryError(DaemonError error) =>
            TryComplete(
                authoritativeResponse: false,
                () => _completion.TrySetResult(Result.Err<TResult, DaemonError>(error)));

        public bool TryResponseError(DaemonError error) =>
            TryComplete(
                authoritativeResponse: true,
                () => _completion.TrySetResult(Result.Err<TResult, DaemonError>(error)));

        public bool TryException(Exception exception) =>
            TryComplete(
                authoritativeResponse: false,
                () => _completion.TrySetException(exception));

        public bool TryCancel(CancellationToken token) =>
            TryComplete(
                authoritativeResponse: false,
                () => _completion.TrySetCanceled(token));

        private bool TryComplete(bool authoritativeResponse, Func<bool> complete)
        {
            if (!_outcome.TryComplete(authoritativeResponse))
            {
                return false;
            }

            try
            {
                CancelSend();
                _remove(_id, this);
                DisposeRegistrations();
            }
            finally
            {
                complete();
            }
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

internal interface IV2ClientPendingRequest
{
    bool IsCompleted { get; }
    CancellationToken SendCancellationToken { get; }
    bool TrySuccess(JsonRpcObjectPayload payload);
    bool TryError(DaemonError error);
    void DisposeRegistrations();
    void DisposeSendLifetime();
}

internal interface IV2ClientTrackedPendingRequest : IV2ClientPendingRequest
{
    bool TryResponseError(DaemonError error);
}
