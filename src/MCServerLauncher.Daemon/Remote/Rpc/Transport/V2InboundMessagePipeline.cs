using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Files;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal enum V2InboundDisposition
{
    NoResponse,
    Queued,
    Rejected,
    SessionTerminated,
    ConnectionAborted,
    ConnectionClosing
}

internal sealed record V2InboundOutcome(
    V2InboundDisposition Disposition,
    BinaryFrameReadResult? BinaryRead = null,
    UploadChunkAcknowledgement? Acknowledgement = null);

internal interface IV2InboundDiagnosticSink
{
    void RecordUnexpected(string correlationId, Exception exception);
    void RecordBinaryFault(BinaryFrameReadResult readResult);
}

internal sealed class V2InboundMessagePipeline
{
    private readonly FrozenProtocolCatalog _catalog;
    private readonly V2RpcDispatcher _dispatcher;
    private readonly V2RpcConnectionContext _context;
    private readonly V2ConnectionOwner _owner;
    private readonly V2FileSessionConnection _files;
    private readonly IV2InboundDiagnosticSink _diagnostics;

    internal V2InboundMessagePipeline(
        FrozenProtocolCatalog catalog,
        V2RpcDispatcher dispatcher,
        V2RpcConnectionContext context,
        V2ConnectionOwner owner,
        V2FileSessionConnection files,
        IV2InboundDiagnosticSink diagnostics)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    internal async Task<V2InboundOutcome> ReceiveTextAsync(
        ReadOnlyMemory<byte> requestUtf8,
        CancellationToken cancellationToken = default)
    {
        var dispatch = await _dispatcher.DispatchAsync(requestUtf8, _context, cancellationToken).ConfigureAwait(false);
        if (!dispatch.HasResponse)
            return new(V2InboundDisposition.NoResponse);

        V2OutboundMessage message;
        if (dispatch.DownloadAttachment is null)
        {
            message = V2OutboundMessage.Single(V2OutboundFrame.Text(dispatch.ResponseUtf8));
        }
        else
        {
            var attachment = dispatch.DownloadAttachment;
            var bytes = new byte[checked(BinaryFrameCodec.HeaderSize + attachment.Data.Length)];
            var header = new BinaryFrameHeader(
                BinaryFrameKind.DownloadChunk,
                attachment.SessionId,
                attachment.Offset,
                checked((uint)attachment.Data.Length));
            if (!BinaryFrameCodec.TryWrite(bytes, header, attachment.Data.AsSpan(), out _))
            {
                _diagnostics.RecordUnexpected(Guid.NewGuid().ToString("N"),
                    new InvalidOperationException("A download attachment could not be encoded."));
                BeginAbort();
                return new(V2InboundDisposition.ConnectionAborted);
            }

            message = V2OutboundMessage.TextThenBinary(
                V2OutboundFrame.Text(dispatch.ResponseUtf8),
                V2OutboundFrame.Binary(ImmutableCollectionsMarshal.AsImmutableArray(bytes)));
        }

        return new(_owner.TryEnqueue(message) ? V2InboundDisposition.Queued : V2InboundDisposition.ConnectionClosing);
    }

    internal async Task<V2InboundOutcome> ReceiveBinaryAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        if (!BinaryFrameCodec.TryRead(frame.Span, out BinaryFrameReadResult read))
        {
            _diagnostics.RecordBinaryFault(read);
            if (read.TrustedTarget is { Kind: BinaryFrameKind.UploadChunk } target)
            {
                await _files.TerminateUploadAsync(target.SessionId).ConfigureAwait(false);
                return new(V2InboundDisposition.SessionTerminated, read);
            }

            BeginAbort();
            return new(V2InboundDisposition.ConnectionAborted, read);
        }

        var header = read.Header!;
        if (header.Kind != BinaryFrameKind.UploadChunk)
        {
            BeginAbort();
            return new(V2InboundDisposition.ConnectionAborted, read);
        }

        Result<RustyOptions.Unit, DaemonError> received;
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _context.ConnectionCancellationToken);
            received = await _files.ReceiveUploadChunkAsync(
                header.SessionId,
                header.Offset,
                frame[BinaryFrameCodec.HeaderSize..],
                linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_context.ConnectionCancellationToken.IsCancellationRequested)
        {
            BeginAbort();
            return new(V2InboundDisposition.ConnectionClosing, read);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _diagnostics.RecordUnexpected(correlationId, exception);
            return EnqueueAcknowledgement(read, CreateRejected(header, -32603, "Internal error", null, correlationId));
        }

        var acknowledgement = received.IsOk(out _)
            ? new UploadChunkAcknowledgement(
                header.SessionId, header.Offset, checked((int)header.PayloadLength),
                UploadChunkAcknowledgementStatus.Accepted, null)
            : CreateDaemonRejected(header, received.UnwrapErr());
        return EnqueueAcknowledgement(read, acknowledgement);
    }

    private V2InboundOutcome EnqueueAcknowledgement(BinaryFrameReadResult read, UploadChunkAcknowledgement acknowledgement)
    {
        var notification = new JsonRpcUploadAcknowledgementNotification(acknowledgement);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            notification,
            BuiltInProtocolJsonContext.Default.JsonRpcUploadAcknowledgementNotification);
        var immutable = ImmutableCollectionsMarshal.AsImmutableArray(bytes);
        var queued = _owner.TryEnqueue(V2OutboundMessage.Single(V2OutboundFrame.Text(immutable)));
        return new(queued
            ? acknowledgement.Status == UploadChunkAcknowledgementStatus.Accepted
                ? V2InboundDisposition.Queued
                : V2InboundDisposition.Rejected
            : V2InboundDisposition.ConnectionClosing, read, acknowledgement);
    }

    private void BeginAbort()
    {
        var close = _owner.AbortAsync();
        _ = close.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private UploadChunkAcknowledgement CreateDaemonRejected(BinaryFrameHeader header, DaemonError error) =>
        CreateRejected(
            header,
            error is PermissionDaemonError ? -32001 : -32000,
            error is PermissionDaemonError ? "Permission denied" : "Daemon error",
            error,
            Guid.NewGuid().ToString("N"));

    private UploadChunkAcknowledgement CreateRejected(
        BinaryFrameHeader header,
        int code,
        string message,
        DaemonError? error,
        string correlationId)
    {
        var data = new JsonRpcErrorData(
            error?.Code,
            error is null ? DaemonErrorWireKind.Internal : ToWireKind(error.Kind),
            correlationId,
            error?.Details,
            originPlugin: null,
            executionOwner: new ProtocolOwnerIdentity("mcsl.daemon", _catalog.Document.Info.Version));
        return new UploadChunkAcknowledgement(
            header.SessionId,
            header.Offset,
            checked((int)header.PayloadLength),
            UploadChunkAcknowledgementStatus.Rejected,
            new JsonRpcErrorObject(code, message, data));
    }

    private static DaemonErrorWireKind ToWireKind(DaemonErrorKind kind) => kind switch
    {
        DaemonErrorKind.Validation => DaemonErrorWireKind.Validation,
        DaemonErrorKind.NotFound => DaemonErrorWireKind.NotFound,
        DaemonErrorKind.Conflict => DaemonErrorWireKind.Conflict,
        DaemonErrorKind.Permission => DaemonErrorWireKind.Permission,
        DaemonErrorKind.Storage => DaemonErrorWireKind.Storage,
        DaemonErrorKind.Transport => DaemonErrorWireKind.Transport,
        DaemonErrorKind.Internal => DaemonErrorWireKind.Internal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
