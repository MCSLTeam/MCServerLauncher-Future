using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Connection.V2;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientDownloadCoordinatorTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void SessionAndRequestValidationAreLocalAndBounded()
    {
        var coordinator = Coordinator(out _);
        var invalid = new DownloadSession(Guid.Empty, 1, "hash", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.False(coordinator.TryRegisterSession(invalid, out var invalidError));
        Assert.Equal("protocol.download_session_invalid", invalidError!.Code);

        var session = Session(maximumChunkSize: 4);
        Assert.True(coordinator.TryRegisterSession(session, out _));
        var cases = new (DownloadChunkRequest Request, string Code)[]
        {
            (new(Guid.NewGuid(), 0, 1), "file.download.session_not_registered"),
            (new(session.SessionId, -1, 1), "file.chunk.offset.invalid"),
            (new(session.SessionId, session.Length + 1, 1), "file.chunk.offset.invalid"),
            (new(session.SessionId, 0, 0), "file.chunk.size.invalid"),
            (new(session.SessionId, 0, 5), "file.chunk.size.invalid")
        };

        foreach (var item in cases)
        {
            Assert.False(coordinator.TryAdmit(
                JsonRpcRequestId.FromString(Guid.NewGuid().ToString("D")),
                item.Request,
                static _ => { },
                out _,
                out var error));
            Assert.Equal(item.Code, error!.Code);
        }
        Assert.Equal(0, coordinator.PendingCount);
    }

    [Fact]
    public async Task MetadataThenBinaryCompletesAndSameOffsetCanBeReadAgain()
    {
        var coordinator = Coordinator(out var faults);
        var session = Session(length: 4, maximumChunkSize: 4);
        Assert.True(coordinator.TryRegisterSession(session, out _));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var drained = 0;
            var pending = Admit(coordinator, session, 0, 4, _ => drained++);
            pending.Register(CancellationToken.None);
            pending.MarkSendStarted();
            Assert.True(pending.TryRouteMetadata(Payload(new(session.SessionId, 0, 4, true))));
            Assert.False(pending.Task.IsCompleted);

            var bytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
            var route = coordinator.RouteBinary(
                new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, session.SessionId, 0, 4),
                bytes.AsSpan());

            Assert.Equal(BinaryRoute.Completed, route);
            Assert.True((await pending.Task).IsOk(out var chunk));
            Assert.Equal(0, chunk!.Offset);
            Assert.True(chunk.Data.AsSpan().SequenceEqual(bytes.AsSpan()));
            Assert.True(chunk.IsFinal);
            Assert.Equal(1, drained);
            Assert.Equal(0, coordinator.PendingCount);
        }
        Assert.Empty(faults);
    }

    [Fact]
    public async Task BinaryBeforeMetadataAndTupleMismatchAreProtocolFaults()
    {
        var coordinator = Coordinator(out var faults);
        var session = Session(length: 8, maximumChunkSize: 4);
        Assert.True(coordinator.TryRegisterSession(session, out _));
        var pending = Admit(coordinator, session, 0, 4);
        pending.MarkSendStarted();

        Assert.Equal(
            BinaryRoute.ProtocolFault,
            coordinator.RouteBinary(
                new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, session.SessionId, 0, 4),
                new byte[4]));
        Assert.True((await pending.Task).IsErr(out var binaryError));
        Assert.Equal("protocol.download_binary_mismatch", binaryError!.Code);

        var secondSession = Session(length: 8, maximumChunkSize: 4);
        Assert.True(coordinator.TryRegisterSession(secondSession, out _));
        var second = Admit(coordinator, secondSession, 0, 4);
        second.MarkSendStarted();
        Assert.True(second.TryRouteMetadata(Payload(new(secondSession.SessionId, 1, 4, false))));
        Assert.True((await second.Task).IsErr(out var metadataError));
        Assert.Equal("protocol.download_metadata_mismatch", metadataError!.Code);
        Assert.Equal(2, faults.Count);
    }

    [Fact]
    public async Task CancellationBeforeSendDoesNotPoisonAndAfterSendDrainsLatePair()
    {
        var coordinator = Coordinator(out var faults);
        var session = Session(length: 1, maximumChunkSize: 1);
        Assert.True(coordinator.TryRegisterSession(session, out _));
        using var before = new CancellationTokenSource();
        before.Cancel();
        var first = Admit(coordinator, session, 0, 1);
        first.Register(before.Token);
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Task);
        Assert.Equal(before.Token, canceled.CancellationToken);
        Assert.Equal(0, coordinator.PendingCount);

        using var after = new CancellationTokenSource();
        var abandoned = Admit(coordinator, session, 0, 1);
        abandoned.Register(after.Token);
        abandoned.MarkSendStarted();
        after.Cancel();
        canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.Task);
        Assert.Equal(after.Token, canceled.CancellationToken);
        Assert.Equal(1, coordinator.PendingCount);
        Assert.Equal(1, coordinator.AbandonedDrainCount);
        Assert.True(abandoned.TryRouteMetadata(Payload(new(session.SessionId, 0, 1, true))));
        Assert.Equal(
            BinaryRoute.Completed,
            coordinator.RouteBinary(
                new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, session.SessionId, 0, 1),
                new byte[] { 7 }));
        Assert.Equal(0, coordinator.PendingCount);
        Assert.Equal(0, coordinator.AbandonedDrainCount);
        Assert.Empty(faults);

        Assert.False(coordinator.TryAdmit(
            JsonRpcRequestId.FromString("poisoned"),
            new(session.SessionId, 0, 1),
            static _ => { },
            out _,
            out var poisoned));
        Assert.Equal("file.download.session_poisoned", poisoned!.Code);
    }

    [Fact]
    public async Task TimeoutCompletesCallerAndAbandonedDeadlineFaultsEpoch()
    {
        var time = new ManualTimeProvider();
        var faults = new List<string>();
        var coordinator = new V2ClientDownloadCoordinator(
            time,
            TimeSpan.FromSeconds(3),
            faults.Add,
            TimeSpan.FromSeconds(30));
        var session = Session();
        Assert.True(coordinator.TryRegisterSession(session, out _));
        var pending = Admit(coordinator, session, 0, 1);
        pending.Register(CancellationToken.None);
        pending.MarkSendStarted();

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True((await pending.Task).IsErr(out var timeout));
        Assert.Equal("request.timeout", timeout!.Code);
        Assert.Equal(1, coordinator.AbandonedDrainCount);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Contains(faults, value => value.Contains("deadline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AbandonedLimitAndCloseAreDeterministic()
    {
        var faults = new List<string>();
        var coordinator = new V2ClientDownloadCoordinator(
            TimeProvider.System,
            RequestTimeout,
            faults.Add,
            maximumAbandonedDrains: 1);
        var firstSession = Session();
        var secondSession = Session();
        Assert.True(coordinator.TryRegisterSession(firstSession, out _));
        Assert.True(coordinator.TryRegisterSession(secondSession, out _));

        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = Admit(coordinator, firstSession, 0, 1);
        var second = Admit(coordinator, secondSession, 0, 1);
        first.Register(firstCancellation.Token);
        second.Register(secondCancellation.Token);
        first.MarkSendStarted();
        second.MarkSendStarted();
        firstCancellation.Cancel();
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Task);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Task);

        Assert.Contains(faults, value => value.Contains("limit", StringComparison.Ordinal));
        coordinator.Close(new TransportDaemonError("connection.closed", "closed"));
        Assert.Equal(0, coordinator.PendingCount);
        Assert.Equal(0, coordinator.AbandonedDrainCount);
    }

    [Fact]
    public async Task CancellationWinsWhileAbandonedTimerCreationBlocksAndLateBinaryOnlyDrains()
    {
        var time = new BlockingAbandonedTimerTimeProvider();
        var coordinator = new V2ClientDownloadCoordinator(time, RequestTimeout, static _ => { });
        var session = Session(length: 1, maximumChunkSize: 1);
        Assert.True(coordinator.TryRegisterSession(session, out _));
        using var cancellation = new CancellationTokenSource();
        var pending = Admit(coordinator, session, 0, 1);
        pending.Register(cancellation.Token);
        pending.MarkSendStarted();

        var cancel = Task.Run(cancellation.Cancel);
        await time.AbandonedTimerEntered.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);

        Assert.True(pending.TryRouteMetadata(Payload(new(session.SessionId, 0, 1, true))));
        Assert.Equal(
            BinaryRoute.Completed,
            coordinator.RouteBinary(
                new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, session.SessionId, 0, 1),
                new byte[] { 1 }));
        canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, coordinator.PendingCount);
        Assert.Equal(0, coordinator.AbandonedDrainCount);

        time.ReleaseAbandonedTimer();
        await cancel.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TimeoutWinsWhileAbandonedTimerCreationBlocksAndLateErrorOnlyDrains()
    {
        var time = new BlockingAbandonedTimerTimeProvider();
        var coordinator = new V2ClientDownloadCoordinator(time, RequestTimeout, static _ => { });
        var session = Session();
        Assert.True(coordinator.TryRegisterSession(session, out _));
        var pending = Admit(coordinator, session, 0, 1);
        pending.Register(CancellationToken.None);
        pending.MarkSendStarted();

        var timeout = time.FireRequestTimer();
        await time.AbandonedTimerEntered.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await pending.Task).IsErr(out var timeoutError));
        Assert.Equal("request.timeout", timeoutError!.Code);

        Assert.True(pending.TryRouteError(new NotFoundDaemonError("file.session.not_found", "missing")));
        Assert.True((await pending.Task).IsErr(out timeoutError));
        Assert.Equal("request.timeout", timeoutError!.Code);
        Assert.Equal(0, coordinator.PendingCount);
        Assert.Equal(0, coordinator.AbandonedDrainCount);

        time.ReleaseAbandonedTimer();
        await timeout.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static V2ClientDownloadCoordinator Coordinator(out List<string> faults)
    {
        faults = [];
        return new(TimeProvider.System, RequestTimeout, faults.Add);
    }

    private static DownloadSession Session(long length = 16, int maximumChunkSize = 4) =>
        new(Guid.NewGuid(), length, new string('a', 64), maximumChunkSize, DateTimeOffset.UtcNow.AddMinutes(5));

    private static V2ClientDownloadCoordinator.PendingDownload Admit(
        V2ClientDownloadCoordinator coordinator,
        DownloadSession session,
        long offset,
        int maximumLength,
        Action<V2ClientDownloadCoordinator.PendingDownload>? drained = null)
    {
        Assert.True(coordinator.TryAdmit(
            JsonRpcRequestId.FromString(Guid.NewGuid().ToString("D")),
            new DownloadChunkRequest(session.SessionId, offset, maximumLength),
            drained ?? (static _ => { }),
            out var pending,
            out var error), error?.Message);
        return pending!;
    }

    private static JsonRpcObjectPayload Payload(DownloadReadResult metadata) =>
        JsonRpcObjectPayload.From(metadata, BuiltInProtocolJsonContext.Default.DownloadReadResult);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            _timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            foreach (var timer in _timers.ToArray())
                timer.Advance(elapsed);
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan remaining) : ITimer
        {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                remaining = dueTime;
                return !_disposed;
            }

            internal void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (!_disposed && remaining <= TimeSpan.Zero)
                {
                    _disposed = true;
                    callback(state);
                }
            }

            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BlockingAbandonedTimerTimeProvider : TimeProvider
    {
        private TimerCallback? _requestCallback;
        private object? _requestState;
        private int _createCount;
        private readonly ManualResetEventSlim _release = new();
        internal Task AbandonedTimerEntered => _abandonedTimerEntered.Task;
        private readonly TaskCompletionSource _abandonedTimerEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Increment(ref _createCount) == 1)
            {
                _requestCallback = callback;
                _requestState = state;
                return new NoOpTimer();
            }

            _abandonedTimerEntered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The abandoned timer creation was not released.");
            return new NoOpTimer();
        }

        internal Task FireRequestTimer() => Task.Run(() => _requestCallback!(_requestState));
        internal void ReleaseAbandonedTimer() => _release.Set();

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose()
            {
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
