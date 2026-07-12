using System.Collections.Immutable;
using System.Text;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Files;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.Rpc.Transport;

public sealed class V2InboundMessagePipelineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Text_NoResponseDoesNotEnqueue_AndPlainResponseIsSentUnchanged()
    {
        await using var fixture = Fixture.Create();
        var pump = fixture.Owner.Start();

        var noResponse = await fixture.Pipeline.ReceiveTextAsync(Utf8(
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.cancel\",\"params\":{{\"session_id\":\"{Guid.NewGuid()}\"}}}}"));
        Assert.Equal(V2InboundDisposition.NoResponse, noResponse.Disposition);
        Assert.Equal(0, fixture.Sender.SendCount);

        var response = await fixture.Pipeline.ReceiveTextAsync(Utf8("{"));
        var sent = await fixture.Sender.NextFrame.Task.WaitAsync(Timeout);
        Assert.Equal(V2InboundDisposition.Queued, response.Disposition);
        Assert.Equal(V2OutboundFrameKind.Text, sent.Kind);
        Assert.Equal(-32700, JsonRpcWireParser.ParseErrorResponse(sent.Payload.AsSpan()).Error.Code);

        await fixture.Owner.CompleteAsync();
        await pump;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadResponseIsOneTextThenBinaryGroup_IncludingEmptyFinalPayload(bool empty)
    {
        await using var fixture = Fixture.Create();
        var session = (await fixture.Files.OpenDownloadAsync(new DownloadOpenRequest("x"), CancellationToken.None)).Unwrap();
        fixture.Application.DownloadData = empty ? [] : [4, 5, 6];
        var pump = fixture.Owner.Start();

        var outcome = await fixture.Pipeline.ReceiveTextAsync(Utf8(
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.download.read\",\"id\":1,\"params\":{{\"session_id\":\"{session.SessionId}\",\"offset\":0,\"maximum_length\":8}}}}"));
        await fixture.Sender.TwoFrames.Task.WaitAsync(Timeout);

        Assert.Equal(V2InboundDisposition.Queued, outcome.Disposition);
        Assert.Equal(2, fixture.Sender.Frames.Count);
        Assert.Equal(V2OutboundFrameKind.Text, fixture.Sender.Frames[0].Kind);
        var binary = fixture.Sender.Frames[1];
        Assert.Equal(V2OutboundFrameKind.Binary, binary.Kind);
        Assert.Equal(BinaryFrameCodec.HeaderSize + fixture.Application.DownloadData.Length, binary.Payload.Length);
        Assert.True(BinaryFrameCodec.TryRead(binary.Payload.AsSpan(), out var header, out var error));
        Assert.Equal(BinaryFrameReadError.None, error);
        Assert.Equal(session.SessionId, header!.SessionId);
        Assert.True(fixture.Application.DownloadData.AsSpan().SequenceEqual(
            binary.Payload[BinaryFrameCodec.HeaderSize..].AsSpan()));

        await fixture.Owner.CompleteAsync();
        await pump;
    }

    [Theory]
    [InlineData(null, -32000)]
    [InlineData("permission", -32001)]
    [InlineData("conflict", -32000)]
    public async Task ValidUploadAlwaysEmitsPrivateAcceptedOrTypedRejectedAcknowledgement(
        string? failure,
        int expectedCode)
    {
        await using var fixture = Fixture.Create();
        var session = (await fixture.Files.OpenUploadAsync(new UploadOpenRequest("x", 3, "hash"), CancellationToken.None)).Unwrap();
        fixture.Application.UploadWriteError = failure switch
        {
            "permission" => new PermissionDaemonError("auth.permission_denied", "denied"),
            null => null,
            _ => new ConflictDaemonError("file.conflict", "conflict")
        };
        var pump = fixture.Owner.Start();

        var outcome = await fixture.Pipeline.ReceiveBinaryAsync(Frame(BinaryFrameKind.UploadChunk, session.SessionId, 0, [1, 2, 3]));
        var sent = await fixture.Sender.NextFrame.Task.WaitAsync(Timeout);
        var acknowledgement = JsonRpcWireParser.ParseUploadAcknowledgementNotification(sent.Payload.AsSpan()).Params;

        Assert.Equal(session.SessionId, acknowledgement.SessionId);
        Assert.Equal(3, acknowledgement.Length);
        if (failure is null)
        {
            Assert.Equal(V2InboundDisposition.Queued, outcome.Disposition);
            Assert.Equal(UploadChunkAcknowledgementStatus.Accepted, acknowledgement.Status);
            Assert.Null(acknowledgement.Error);
        }
        else
        {
            Assert.Equal(V2InboundDisposition.Rejected, outcome.Disposition);
            Assert.Equal(UploadChunkAcknowledgementStatus.Rejected, acknowledgement.Status);
            Assert.Equal(expectedCode, acknowledgement.Error!.Code);
            Assert.Equal("mcsl.daemon", acknowledgement.Error.Data.ExecutionOwner!.Id);
        }

        await fixture.Owner.AbortAsync();
        await pump;
    }

    [Fact]
    public async Task MalformedFramesUseAuditableTargetDisposition()
    {
        await AssertConnectionAbortAsync(new byte[3], BinaryFrameReadError.FrameTooShort);

        var emptySession = Frame(BinaryFrameKind.UploadChunk, Guid.NewGuid(), 0, []);
        emptySession.AsSpan(4, 16).Clear();
        await AssertConnectionAbortAsync(emptySession, BinaryFrameReadError.EmptySessionId);

        await using (var fixture = Fixture.Create())
        {
            var session = (await fixture.Files.OpenUploadAsync(new UploadOpenRequest("x", 1, "hash"), CancellationToken.None)).Unwrap();
            var malformed = Frame(BinaryFrameKind.UploadChunk, session.SessionId, 0, [1]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(malformed.AsSpan(20, 8), -1);
            var outcome = await fixture.Pipeline.ReceiveBinaryAsync(malformed);
            Assert.Equal(V2InboundDisposition.SessionTerminated, outcome.Disposition);
            Assert.Equal(1, fixture.Application.UploadCancelCalls);
            Assert.Equal(V2ConnectionState.Created, fixture.Owner.State);
        }

        await using (var fixture = Fixture.Create())
        {
            var outcome = await fixture.Pipeline.ReceiveBinaryAsync(
                Frame(BinaryFrameKind.DownloadChunk, Guid.NewGuid(), 0, []));
            Assert.Equal(V2InboundDisposition.ConnectionAborted, outcome.Disposition);
            await fixture.Sender.Closed.Task.WaitAsync(Timeout);
            Assert.Equal(V2ConnectionState.Closed, fixture.Owner.State);
        }
    }

    [Fact]
    public async Task QueueFullClosesWithoutRetry()
    {
        await using var fixture = Fixture.Create();
        for (var index = 0; index < V2ConnectionOwner.OutboundCapacity; index++)
            Assert.True(fixture.Owner.TryEnqueue(V2OutboundMessage.Single(V2OutboundFrame.CopyText([1]))));

        var outcome = await fixture.Pipeline.ReceiveTextAsync(Utf8("{"));
        await fixture.Sender.Closed.Task.WaitAsync(Timeout);

        Assert.Equal(V2InboundDisposition.ConnectionClosing, outcome.Disposition);
        Assert.Equal(0, fixture.Sender.SendCount);
        Assert.Equal(1, fixture.Sender.CloseCount);
        Assert.Equal(V2ConnectionState.Closed, fixture.Owner.State);
    }

    [Fact]
    public async Task CallerCancellationRethrows_ButConnectionCancellationAbortsWithoutAcknowledgement()
    {
        await using (var fixture = Fixture.Create())
        {
            var session = (await fixture.Files.OpenUploadAsync(new UploadOpenRequest("x", 1, "hash"), CancellationToken.None)).Unwrap();
            fixture.Application.BlockWrite = true;
            using var caller = new CancellationTokenSource();
            var receive = fixture.Pipeline.ReceiveBinaryAsync(
                Frame(BinaryFrameKind.UploadChunk, session.SessionId, 0, [1]), caller.Token);
            await fixture.Application.WriteEntered.Task.WaitAsync(Timeout);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive);
            Assert.Equal(0, fixture.Sender.SendCount);
        }

        using var connectionCancellation = new CancellationTokenSource();
        await using (var fixture = Fixture.Create(connectionCancellation.Token))
        {
            var session = (await fixture.Files.OpenUploadAsync(new UploadOpenRequest("x", 1, "hash"), CancellationToken.None)).Unwrap();
            fixture.Application.BlockWrite = true;
            var receive = fixture.Pipeline.ReceiveBinaryAsync(
                Frame(BinaryFrameKind.UploadChunk, session.SessionId, 0, [1]));
            await fixture.Application.WriteEntered.Task.WaitAsync(Timeout);
            connectionCancellation.Cancel();
            var outcome = await receive.WaitAsync(Timeout);
            Assert.Equal(V2InboundDisposition.ConnectionClosing, outcome.Disposition);
            await fixture.Sender.Closed.Task.WaitAsync(Timeout);
            Assert.Equal(V2ConnectionState.Closed, fixture.Owner.State);
            Assert.Equal(0, fixture.Sender.SendCount);
            Assert.Equal(1, fixture.Application.UploadCancelCalls);
        }
    }

    private static async Task AssertConnectionAbortAsync(byte[] frame, BinaryFrameReadError error)
    {
        await using var fixture = Fixture.Create();
        var outcome = await fixture.Pipeline.ReceiveBinaryAsync(frame);
        Assert.Equal(V2InboundDisposition.ConnectionAborted, outcome.Disposition);
        Assert.Equal(error, outcome.BinaryRead!.Error);
        await fixture.Sender.Closed.Task.WaitAsync(Timeout);
        Assert.Equal(V2ConnectionState.Closed, fixture.Owner.State);
    }

    private static byte[] Frame(BinaryFrameKind kind, Guid sessionId, long offset, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[BinaryFrameCodec.HeaderSize + payload.Length];
        Assert.True(BinaryFrameCodec.TryWrite(
            frame,
            new BinaryFrameHeader(kind, sessionId, offset, checked((uint)payload.Length)),
            payload,
            out _));
        return frame;
    }

    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(FakeFileApplication application, RecordingSender sender, V2ConnectionOwner owner,
            V2FileSessionConnection files, V2InboundMessagePipeline pipeline)
        {
            Application = application;
            Sender = sender;
            Owner = owner;
            Files = files;
            Pipeline = pipeline;
        }

        internal FakeFileApplication Application { get; }
        internal RecordingSender Sender { get; }
        internal V2ConnectionOwner Owner { get; }
        internal V2FileSessionConnection Files { get; }
        internal V2InboundMessagePipeline Pipeline { get; }

        internal static Fixture Create(CancellationToken connectionToken = default)
        {
            var application = new FakeFileApplication();
            var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("pipeline", "1.0.0"));
            BuiltInFileRpcRegistrar.Register(builder, application);
            var catalog = builder.Freeze();
            var sender = new RecordingSender();
            var owner = new V2ConnectionOwner(sender, ["mcsl.daemon.file.**"], connectionCancellation: connectionToken);
            var files = V2FileSessionConnection.Attach(application, catalog, owner).Unwrap();
            var context = new V2RpcConnectionContext(owner, null, connectionToken, files);
            var dispatcher = new V2RpcDispatcher(catalog, new RpcDiagnostics());
            var pipeline = new V2InboundMessagePipeline(catalog, dispatcher, context, owner, files, new InboundDiagnostics());
            return new(application, sender, owner, files, pipeline);
        }

        public async ValueTask DisposeAsync() => await Owner.DisposeAsync();
    }

    private sealed class RecordingSender : IV2OutboundSender
    {
        internal List<V2OutboundFrame> Frames { get; } = [];
        internal TaskCompletionSource<V2OutboundFrame> NextFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TwoFrames { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SendCount { get; private set; }
        internal int CloseCount { get; private set; }

        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            Frames.Add(frame);
            SendCount++;
            NextFrame.TrySetResult(frame);
            if (Frames.Count == 2)
                TwoFrames.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
        {
            CloseCount++;
            Closed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RpcDiagnostics : IV2RpcDiagnosticSink
    {
        public void RecordUnexpected(V2RpcUnexpectedDiagnostic diagnostic) { }
        public void RecordNotificationSuppressed(V2RpcNotificationSuppressionDiagnostic diagnostic) { }
    }

    private sealed class InboundDiagnostics : IV2InboundDiagnosticSink
    {
        public void RecordUnexpected(string correlationId, Exception exception) { }
        public void RecordBinaryFault(BinaryFrameReadResult readResult) { }
    }

    private sealed class FakeFileApplication : IFileApplication
    {
        internal UploadSession UploadSession { get; } = new(Guid.NewGuid(), 1024, DateTimeOffset.MaxValue);
        internal DownloadSession DownloadSession { get; } = new(Guid.NewGuid(), 10, "hash", 1024, DateTimeOffset.MaxValue);
        internal ImmutableArray<byte> DownloadData { get; set; } = [4, 5, 6];
        internal DaemonError? UploadWriteError { get; set; }
        internal bool BlockWrite { get; set; }
        internal int UploadCancelCalls { get; private set; }
        internal TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken token) =>
            Task.FromResult(Result.Ok<UploadSession, DaemonError>(UploadSession));
        public async Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest request, CancellationToken token)
        {
            WriteEntered.TrySetResult();
            if (BlockWrite)
                await WaitForCancellationAsync(token);
            return UploadWriteError is null
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(UploadWriteError);
        }
        public Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid id, CancellationToken token) =>
            Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        public Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid id, CancellationToken token)
        {
            UploadCancelCalls++;
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }
        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken token) =>
            Task.FromResult(Result.Ok<DownloadSession, DaemonError>(DownloadSession));
        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken token) =>
            Task.FromResult(Result.Ok<DownloadChunk, DaemonError>(new DownloadChunk(request.Offset, DownloadData, true)));
        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid id, CancellationToken token) =>
            Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));

        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest r, CancellationToken t) => throw new NotSupportedException();

        private static async Task WaitForCancellationAsync(CancellationToken token)
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelled.TrySetCanceled(token));
            await cancelled.Task;
        }
    }
}
