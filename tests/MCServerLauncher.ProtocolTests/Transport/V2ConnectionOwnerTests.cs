using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.ProtocolTests.Transport;

public sealed class V2ConnectionOwnerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void OutboundMessages_AreImmutableAndRejectInvalidGroups()
    {
        var source = new byte[] { 1, 2, 3 };
        var copied = V2OutboundFrame.CopyText(source);
        source[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, copied.Payload.ToArray());
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.Single(default));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.Single(V2OutboundFrame.CopyText([])));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.Single(V2OutboundFrame.CopyBinary([1])));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.TextThenBinary(
            V2OutboundFrame.CopyBinary([1]),
            V2OutboundFrame.CopyText([2])));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.TextThenBinary(
            V2OutboundFrame.CopyText([]),
            V2OutboundFrame.CopyBinary([2])));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.TextThenBinary(
            V2OutboundFrame.CopyText([1]),
            V2OutboundFrame.CopyBinary([])));
        Assert.Throws<ArgumentException>(() => V2OutboundMessage.TextThenBinary(
            default,
            V2OutboundFrame.CopyBinary([2])));

        var sharedPayload = ImmutableArray.Create<byte>(4, 5, 6);
        var first = V2OutboundFrame.Text(sharedPayload);
        var second = V2OutboundFrame.Text(sharedPayload);
        Assert.Equal(first.Payload, second.Payload);
        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(first.Payload),
            ImmutableCollectionsMarshal.AsArray(second.Payload));

        var grouped = V2OutboundMessage.TextThenBinary(
            first,
            V2OutboundFrame.CopyBinary([7]));
        Assert.Equal([V2OutboundFrameKind.Text, V2OutboundFrameKind.Binary], grouped.Frames.Select(static frame => frame.Kind));
    }

    [Fact]
    public async Task Pump_PreservesMessageFifoAndGroupContiguity()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        var response = Frame(V2OutboundFrameKind.Text, 1);
        var binary = Frame(V2OutboundFrameKind.Binary, 2);

        Assert.True(owner.TryEnqueue(V2OutboundMessage.Single(Frame(V2OutboundFrameKind.Text, 0))));
        Assert.True(owner.TryEnqueue(V2OutboundMessage.TextThenBinary(response, binary)));
        Assert.True(owner.TryEnqueue(V2OutboundMessage.Single(Frame(V2OutboundFrameKind.Text, 3))));

        _ = owner.Start();
        await owner.CompleteAsync().WaitAsync(TestTimeout);

        Assert.Equal([0, 1, 2, 3], sender.Sent.Select(static frame => frame.Payload[0]));
        Assert.Equal(
            [V2OutboundFrameKind.Text, V2OutboundFrameKind.Text, V2OutboundFrameKind.Binary, V2OutboundFrameKind.Text],
            sender.Sent.Select(static frame => frame.Kind));
        Assert.Equal([V2ConnectionCloseReason.Graceful], sender.CloseReasons);
    }

    [Fact]
    public async Task ConcurrentProducers_CannotInterleaveAFrameGroup()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        using var barrier = new Barrier(3);
        var groupedProducer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var index = 0; index < 100; index++)
            {
                Assert.True(owner.TryEnqueue(V2OutboundMessage.TextThenBinary(
                    Frame(V2OutboundFrameKind.Text, 0xA0),
                    Frame(V2OutboundFrameKind.Binary, 0xA1))));
            }
        });
        var singleProducer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var index = 0; index < 100; index++)
                Assert.True(owner.TryEnqueue(Message(0xB0)));
        });
        barrier.SignalAndWait();
        await Task.WhenAll(groupedProducer, singleProducer).WaitAsync(TestTimeout);

        _ = owner.Start();
        await owner.CompleteAsync().WaitAsync(TestTimeout);

        var sent = sender.Sent;
        for (var index = 0; index < sent.Length; index++)
        {
            if (sent[index].Payload[0] != 0xA0)
                continue;

            Assert.True(index + 1 < sent.Length);
            Assert.Equal(V2OutboundFrameKind.Binary, sent[index + 1].Kind);
            Assert.Equal(0xA1, sent[index + 1].Payload[0]);
        }
    }

    [Fact]
    public async Task Queue_AcceptsExactly256BeforeSlowConsumerClose()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);

        for (var index = 0; index < V2ConnectionOwner.OutboundCapacity; index++)
            Assert.True(owner.TryEnqueue(Message(index)));

        Assert.False(owner.TryEnqueue(Message(256)));
        await sender.Closed.Task.WaitAsync(TestTimeout);

        Assert.Equal(V2ConnectionState.Closed, owner.State);
        Assert.Equal(V2ConnectionCloseReason.SlowConsumer, owner.CloseReason);
        Assert.Equal([V2ConnectionCloseReason.SlowConsumer], sender.CloseReasons);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Complete_DrainsAcceptedMessagesAndIsIdempotent()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        var firstStart = owner.Start();
        var secondStart = owner.Start();
        Assert.Same(firstStart, secondStart);

        for (var index = 0; index < 25; index++)
            Assert.True(owner.TryEnqueue(Message(index)));

        var firstComplete = owner.CompleteAsync();
        var secondComplete = owner.CompleteAsync();
        await Task.WhenAll(firstComplete, secondComplete).WaitAsync(TestTimeout);

        Assert.Equal(Enumerable.Range(0, 25).Select(static value => (byte)value), sender.Sent.Select(static frame => frame.Payload[0]));
        Assert.False(owner.TryEnqueue(Message(99)));
        Assert.Equal(1, sender.CloseCount);

        await owner.DisposeAsync();
        await owner.DisposeAsync();
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task CompleteBeforeStart_DrainsWithoutASeparateStartCall()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        Assert.True(owner.TryEnqueue(Message(1)));
        Assert.True(owner.TryEnqueue(Message(2)));

        await owner.CompleteAsync().WaitAsync(TestTimeout);

        Assert.Equal([1, 2], sender.Sent.Select(static frame => frame.Payload[0]));
        Assert.Equal(V2ConnectionCloseReason.Graceful, owner.CloseReason);
        Assert.False(sender.CloseWhileSendActive);
    }

    [Fact]
    public async Task GracefulCompleteAndFullHaveStableWinningReasons()
    {
        var fullSender = new RecordingSender();
        await using (var fullOwner = new V2ConnectionOwner(fullSender))
        {
            for (var index = 0; index < V2ConnectionOwner.OutboundCapacity; index++)
                Assert.True(fullOwner.TryEnqueue(Message(index)));
            Assert.False(fullOwner.TryEnqueue(Message(256)));

            await fullOwner.CompleteAsync().WaitAsync(TestTimeout);
            Assert.Equal(V2ConnectionCloseReason.SlowConsumer, fullOwner.CloseReason);
            Assert.Equal(1, fullSender.CloseCount);
        }

        var gracefulSender = new RecordingSender();
        await using var gracefulOwner = new V2ConnectionOwner(gracefulSender);
        Assert.True(gracefulOwner.TryEnqueue(Message(1)));
        var completing = gracefulOwner.CompleteAsync();
        Assert.False(gracefulOwner.TryEnqueue(Message(2)));
        await completing.WaitAsync(TestTimeout);

        Assert.Equal(V2ConnectionCloseReason.Graceful, gracefulOwner.CloseReason);
        Assert.Equal(1, gracefulSender.CloseCount);
    }

    [Fact]
    public async Task QueueFull_AbortsBlockedSendAndDoesNotDrainBacklog()
    {
        var sendEntered = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, cancellationToken) =>
            {
                sendEntered.TrySetResult();
                await WaitForCancellationAsync(cancellationToken);
            }
        };
        await using var owner = new V2ConnectionOwner(sender);
        _ = owner.Start();
        Assert.True(owner.TryEnqueue(Message(0)));
        await sendEntered.Task.WaitAsync(TestTimeout);

        for (var index = 0; index < V2ConnectionOwner.OutboundCapacity; index++)
            Assert.True(owner.TryEnqueue(Message(index + 1)));
        Assert.False(owner.TryEnqueue(Message(255)));

        await sender.Closed.Task.WaitAsync(TestTimeout);
        Assert.Single(sender.SentAttempts);
        Assert.Equal(V2ConnectionCloseReason.SlowConsumer, owner.CloseReason);
    }

    [Fact]
    public async Task Abort_CancelsCurrentSendAndDoesNotDrainReadyBacklog()
    {
        var sendEntered = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, cancellationToken) =>
            {
                sendEntered.TrySetResult();
                await WaitForCancellationAsync(cancellationToken);
            }
        };
        await using var owner = new V2ConnectionOwner(sender);
        _ = owner.Start();
        Assert.True(owner.TryEnqueue(Message(0)));
        await sendEntered.Task.WaitAsync(TestTimeout);
        for (var index = 1; index < 100; index++)
            Assert.True(owner.TryEnqueue(Message(index)));

        await owner.AbortAsync().WaitAsync(TestTimeout);

        Assert.Single(sender.SentAttempts);
        Assert.Empty(sender.Sent);
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task StopRequestedInsideAGroup_AllowsSuccessfulFramesToRemainContiguous()
    {
        V2ConnectionOwner? owner = null;
        var sender = new RecordingSender
        {
            SendHandler = (frame, _cancellationToken) =>
            {
                if (frame.Payload[0] == 1)
                    _ = owner!.AbortAsync(V2ConnectionCloseReason.Peer);
                return ValueTask.CompletedTask;
            }
        };
        owner = new V2ConnectionOwner(sender);
        Assert.True(owner.TryEnqueue(V2OutboundMessage.TextThenBinary(
            Frame(V2OutboundFrameKind.Text, 1),
            Frame(V2OutboundFrameKind.Binary, 2))));
        Assert.True(owner.TryEnqueue(Message(3)));

        _ = owner.Start();
        await sender.Closed.Task.WaitAsync(TestTimeout);

        Assert.Equal([1, 2], sender.Sent.Select(static frame => frame.Payload[0]));
        Assert.Equal(V2ConnectionCloseReason.Peer, owner.CloseReason);
        Assert.False(sender.CloseWhileSendActive);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task SendTimeout_CancelsLifetimeAndNeverStartsNextFrame()
    {
        var time = new ManualTimeProvider();
        var sendEntered = NewSignal();
        var sendCanceled = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, cancellationToken) =>
            {
                sendEntered.TrySetResult();
                try
                {
                    await WaitForCancellationAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    sendCanceled.TrySetResult();
                    throw;
                }
            }
        };
        await using var owner = new V2ConnectionOwner(sender, timeProvider: time);
        Assert.True(owner.TryEnqueue(Message(1)));
        Assert.True(owner.TryEnqueue(Message(2)));
        _ = owner.Start();
        await sendEntered.Task.WaitAsync(TestTimeout);
        await time.TimerCreated.Task.WaitAsync(TestTimeout);

        time.Advance(V2ConnectionOwner.FrameSendTimeout);

        await sender.Closed.Task.WaitAsync(TestTimeout);
        await sendCanceled.Task.WaitAsync(TestTimeout);
        Assert.Single(sender.SentAttempts);
        Assert.Equal(V2ConnectionCloseReason.SlowConsumer, owner.CloseReason);
        Assert.Equal(V2ConnectionStopCause.SendTimeout, owner.DiagnosticStopCause);
        Assert.Equal(1, sender.CloseCount);
        Assert.Equal([V2ConnectionCloseReason.SlowConsumer], sender.CloseReasons);
    }

    [Fact]
    public async Task HungSendIgnoringCancellation_AbortDetachesThenClosesTransport()
    {
        var sendEntered = NewSignal();
        var releaseHungSend = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, _) =>
            {
                sendEntered.TrySetResult();
                await releaseHungSend.Task;
            },
            CloseHandler = (_, _) =>
            {
                releaseHungSend.TrySetException(new IOException("transport aborted"));
                return ValueTask.CompletedTask;
            }
        };
        var owner = new V2ConnectionOwner(sender);
        _ = owner.Start();
        Assert.True(owner.TryEnqueue(Message(1)));
        Assert.True(owner.TryEnqueue(Message(2)));
        await sendEntered.Task.WaitAsync(TestTimeout);

        await owner.AbortAsync().WaitAsync(TestTimeout);

        Assert.Single(sender.SentAttempts);
        Assert.Empty(sender.Sent);
        Assert.True(sender.CloseWhileSendActive);
        Assert.Equal(1, sender.CloseCount);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task HungSendIgnoringCancellation_TimeoutDetachesAndSendsNoNextFrame()
    {
        var time = new ManualTimeProvider();
        var sendEntered = NewSignal();
        var releaseHungSend = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, _) =>
            {
                sendEntered.TrySetResult();
                await releaseHungSend.Task;
            },
            CloseHandler = (_, _) =>
            {
                releaseHungSend.TrySetException(new IOException("transport aborted"));
                return ValueTask.CompletedTask;
            }
        };
        var owner = new V2ConnectionOwner(sender, timeProvider: time);
        _ = owner.Start();
        Assert.True(owner.TryEnqueue(Message(1)));
        Assert.True(owner.TryEnqueue(Message(2)));
        await sendEntered.Task.WaitAsync(TestTimeout);
        await time.TimerCreated.Task.WaitAsync(TestTimeout);

        time.Advance(V2ConnectionOwner.FrameSendTimeout);
        await sender.Closed.Task.WaitAsync(TestTimeout);

        Assert.Single(sender.SentAttempts);
        Assert.Empty(sender.Sent);
        Assert.True(sender.CloseWhileSendActive);
        Assert.Equal(V2ConnectionCloseReason.SlowConsumer, owner.CloseReason);
        Assert.Equal(V2ConnectionStopCause.SendTimeout, owner.DiagnosticStopCause);
        Assert.Equal([V2ConnectionCloseReason.SlowConsumer], sender.CloseReasons);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task SendFailure_ClosesOnceAndStopsTheGroup()
    {
        var sender = new RecordingSender
        {
            SendHandler = static (_, _) => throw new IOException("send failed")
        };
        await using var owner = new V2ConnectionOwner(sender);
        Assert.True(owner.TryEnqueue(V2OutboundMessage.TextThenBinary(
            Frame(V2OutboundFrameKind.Text, 1),
            Frame(V2OutboundFrameKind.Binary, 2))));

        _ = owner.Start();
        await sender.Closed.Task.WaitAsync(TestTimeout);

        Assert.Single(sender.SentAttempts);
        Assert.Equal(V2ConnectionCloseReason.SendFailure, owner.CloseReason);
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task ConcurrentEnqueueAndComplete_SendEveryAcceptedMessage()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        _ = owner.Start();
        using var barrier = new Barrier(9);
        var accepted = new ConcurrentBag<byte>();
        var producers = Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            barrier.SignalAndWait();
            var value = (byte)index;
            if (owner.TryEnqueue(Message(value)))
                accepted.Add(value);
        })).ToArray();
        var completing = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await owner.CompleteAsync();
        });

        await Task.WhenAll([.. producers, completing]).WaitAsync(TestTimeout);

        Assert.Equal(accepted.Order(), sender.Sent.Select(static frame => frame.Payload[0]).Order());
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task LifecycleRacers_NeverDoubleCloseOrLeakThePump()
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var sender = new RecordingSender();
            var owner = new V2ConnectionOwner(sender);
            _ = owner.Start();
            using var barrier = new Barrier(5);

            var enqueue = Task.Run(() =>
            {
                barrier.SignalAndWait();
                _ = owner.TryEnqueue(Message(iteration));
            });
            var complete = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await owner.CompleteAsync();
            });
            var abort = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await owner.AbortAsync(V2ConnectionCloseReason.Peer);
            });
            var dispose = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await owner.DisposeAsync();
            });

            barrier.SignalAndWait();
            await Task.WhenAll(enqueue, complete, abort, dispose).WaitAsync(TestTimeout);
            await owner.DisposeAsync();

            Assert.Equal(V2ConnectionState.Closed, owner.State);
            Assert.Equal(1, sender.CloseCount);
        }
    }

    [Fact]
    public async Task IndependentOwners_DoNotShareBackpressure()
    {
        var blocked = NewSignal();
        var firstSender = new RecordingSender
        {
            SendHandler = async (_, cancellationToken) =>
            {
                blocked.TrySetResult();
                await WaitForCancellationAsync(cancellationToken);
            }
        };
        var secondSender = new RecordingSender();
        await using var first = new V2ConnectionOwner(firstSender);
        await using var second = new V2ConnectionOwner(secondSender);
        _ = first.Start();
        _ = second.Start();
        Assert.True(first.TryEnqueue(Message(1)));
        await blocked.Task.WaitAsync(TestTimeout);

        Assert.True(second.TryEnqueue(Message(2)));
        await second.CompleteAsync().WaitAsync(TestTimeout);

        Assert.Equal([2], secondSender.Sent.Select(static frame => frame.Payload[0]));
        Assert.Empty(firstSender.Sent);
        await first.AbortAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task ExternalConnectionCancellation_AbortsBlockedSend()
    {
        using var connectionCancellation = new CancellationTokenSource();
        var sendEntered = NewSignal();
        var sender = new RecordingSender
        {
            SendHandler = async (_, cancellationToken) =>
            {
                sendEntered.TrySetResult();
                await WaitForCancellationAsync(cancellationToken);
            }
        };
        await using var owner = new V2ConnectionOwner(
            sender,
            connectionCancellation: connectionCancellation.Token);
        _ = owner.Start();
        Assert.True(owner.TryEnqueue(Message(1)));
        await sendEntered.Task.WaitAsync(TestTimeout);

        await connectionCancellation.CancelAsync();
        await sender.Closed.Task.WaitAsync(TestTimeout);

        Assert.Equal(V2ConnectionCloseReason.Abort, owner.CloseReason);
        Assert.Equal(1, sender.CloseCount);
    }

    [Fact]
    public async Task NormalShutdown_NeverOverlapsSenderCloseWithSend()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        _ = owner.Start();
        for (var index = 0; index < 100; index++)
            Assert.True(owner.TryEnqueue(Message(index)));

        await owner.CompleteAsync().WaitAsync(TestTimeout);

        Assert.Equal(1, sender.MaxConcurrentSends);
        Assert.False(sender.CloseWhileSendActive);
        Assert.Equal(100, sender.Sent.Length);
    }

    [Fact]
    public async Task Cleanup_RunsExactlyOnceOutsideTheOwnerLock()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        var cleanupEntered = NewSignal();
        var cleanup = new DelegateCleanup(cancellationToken =>
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            cleanupEntered.TrySetResult();
            Assert.False(owner.TryRegisterCleanup(new DelegateCleanup(static _ => ValueTask.CompletedTask)));
            _ = owner.State;
            return ValueTask.CompletedTask;
        });
        Assert.True(owner.TryRegisterCleanup(cleanup));

        var closes = Enumerable.Range(0, 8)
            .Select(_ => owner.AbortAsync(V2ConnectionCloseReason.Peer))
            .ToArray();
        await Task.WhenAll(closes).WaitAsync(TestTimeout);
        await cleanupEntered.Task.WaitAsync(TestTimeout);

        Assert.Equal(1, cleanup.CallCount);
        Assert.Equal(1, sender.CloseCount);
        Assert.Equal(V2ConnectionCloseReason.Peer, owner.CloseReason);
    }

    [Fact]
    public async Task CleanupUnregistrationUsesExactIdentityAndCannotRemoveSnapshottedCleanup()
    {
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender);
        var retained = new DelegateCleanup(static _ => ValueTask.CompletedTask);
        var removed = new DelegateCleanup(static _ => ValueTask.CompletedTask);
        var equalButDifferent = new DelegateCleanup(static _ => ValueTask.CompletedTask);
        Assert.True(owner.TryRegisterCleanup(retained));
        Assert.True(owner.TryRegisterCleanup(removed));
        Assert.Equal(2, owner.CleanupRegistrationCount);

        Assert.False(owner.TryUnregisterCleanup(equalButDifferent));
        Assert.True(owner.TryUnregisterCleanup(removed));
        Assert.False(owner.TryUnregisterCleanup(removed));
        Assert.Equal(1, owner.CleanupRegistrationCount);

        var close = owner.AbortAsync();
        Assert.Equal(0, owner.CleanupRegistrationCount);
        Assert.False(owner.TryUnregisterCleanup(retained));
        await close.WaitAsync(TestTimeout);

        Assert.Equal(1, retained.CallCount);
        Assert.Equal(0, removed.CallCount);
        Assert.Equal(0, equalButDifferent.CallCount);
    }

    [Fact]
    public async Task CleanupFailures_AreIsolatedAndAllCleanupsRunOnce()
    {
        var sender = new RecordingSender();
        var owner = new V2ConnectionOwner(sender);
        var first = new DelegateCleanup(static _ => ValueTask.CompletedTask);
        var failing = new DelegateCleanup(static _ => ValueTask.FromException(new IOException("cleanup failed")));
        var last = new DelegateCleanup(static _ => ValueTask.CompletedTask);
        Assert.True(owner.TryRegisterCleanup(first));
        Assert.True(owner.TryRegisterCleanup(failing));
        Assert.True(owner.TryRegisterCleanup(last));

        await Assert.ThrowsAsync<AggregateException>(
            () => owner.AbortAsync().WaitAsync(TestTimeout));

        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, failing.CallCount);
        Assert.Equal(1, last.CallCount);
        Assert.Equal(1, sender.CloseCount);
        Assert.Equal(V2ConnectionState.Closed, owner.State);
        await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task SenderCloseFailure_StillReachesTerminalStateAndDoesNotRetry()
    {
        var sender = new RecordingSender
        {
            CloseHandler = static (_, _) => ValueTask.FromException(new IOException("close failed"))
        };
        var owner = new V2ConnectionOwner(sender);

        await Assert.ThrowsAsync<AggregateException>(
            () => owner.AbortAsync().WaitAsync(TestTimeout));

        Assert.Equal(V2ConnectionState.Closed, owner.State);
        Assert.Equal(1, sender.CloseCount);
        await Assert.ThrowsAsync<AggregateException>(
            () => owner.AbortAsync().WaitAsync(TestTimeout));
        Assert.Equal(1, sender.CloseCount);
        await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Permissions_AreNormalizedAndImmutable()
    {
        var input = new List<string> { " Instance.Read ", "instance.read", "FILE.READ" };
        var sender = new RecordingSender();
        await using var owner = new V2ConnectionOwner(sender, input);
        input.Clear();

        Assert.Equal(new[] { "file.read", "instance.read" }, owner.Permissions.ToArray());
        await owner.AbortAsync().WaitAsync(TestTimeout);
    }

    private static V2OutboundMessage Message(int value) =>
        V2OutboundMessage.Single(Frame(V2OutboundFrameKind.Text, (byte)value));

    private static V2OutboundFrame Frame(V2OutboundFrameKind kind, byte value) =>
        kind == V2OutboundFrameKind.Text
            ? V2OutboundFrame.CopyText([value])
            : V2OutboundFrame.CopyBinary([value]);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var cancellation = NewSignal();
        using var registration = cancellationToken.Register(
            () => cancellation.TrySetCanceled(cancellationToken));
        await cancellation.Task;
    }

    private sealed class RecordingSender : IV2OutboundSender
    {
        private readonly object _gate = new();
        private readonly List<V2OutboundFrame> _sent = [];
        private readonly List<V2OutboundFrame> _attempts = [];
        private readonly List<V2ConnectionCloseReason> _closeReasons = [];
        private int _activeSends;
        private int _maxConcurrentSends;
        private int _closeWhileSendActive;

        internal Func<V2OutboundFrame, CancellationToken, ValueTask>? SendHandler { get; init; }

        internal Func<V2ConnectionCloseReason, CancellationToken, ValueTask>? CloseHandler { get; init; }

        internal TaskCompletionSource Closed { get; } = NewSignal();

        internal V2OutboundFrame[] Sent
        {
            get
            {
                lock (_gate)
                    return [.. _sent];
            }
        }

        internal V2OutboundFrame[] SentAttempts
        {
            get
            {
                lock (_gate)
                    return [.. _attempts];
            }
        }

        internal V2ConnectionCloseReason[] CloseReasons
        {
            get
            {
                lock (_gate)
                    return [.. _closeReasons];
            }
        }

        internal int CloseCount => CloseReasons.Length;

        internal int MaxConcurrentSends => Volatile.Read(ref _maxConcurrentSends);

        internal bool CloseWhileSendActive => Volatile.Read(ref _closeWhileSendActive) != 0;

        public async ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            lock (_gate)
                _attempts.Add(frame);

            var active = Interlocked.Increment(ref _activeSends);
            UpdateMaximum(ref _maxConcurrentSends, active);
            try
            {
                if (SendHandler is not null)
                    await SendHandler(frame, cancellationToken);

                lock (_gate)
                    _sent.Add(frame);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        public async ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _activeSends) != 0)
                Interlocked.Exchange(ref _closeWhileSendActive, 1);
            lock (_gate)
                _closeReasons.Add(reason);
            try
            {
                if (CloseHandler is not null)
                    await CloseHandler(reason, cancellationToken);
            }
            finally
            {
                Closed.TrySetResult();
            }
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class DelegateCleanup(Func<CancellationToken, ValueTask> callback) : IV2ConnectionCleanup
    {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public ValueTask CleanupAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return callback(cancellationToken);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];
        private long _timestamp;

        internal TaskCompletionSource TimerCreated { get; } = NewSignal();

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_gate)
                return _timestamp;
        }

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            ManualTimer[] due;
            lock (_gate)
            {
                _timestamp += duration.Ticks;
                due = _timers.Where(timer => timer.IsDue(_timestamp)).ToArray();
                foreach (var timer in due)
                    timer.RescheduleAfterFire(_timestamp);
            }

            foreach (var timer in due)
                timer.Fire();
        }

        private void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));
            lock (_gate)
            {
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : checked(_timestamp + dueTime.Ticks);
                timer.Period = period;
                _timers.Add(timer);
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
                _timers.Remove(timer);
        }

        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(name);
        }

        private sealed class ManualTimer(
            ManualTimeProvider provider,
            TimerCallback callback,
            object? state) : ITimer
        {
            private int _disposed;

            internal long DueAt { get; set; } = long.MaxValue;

            internal TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return false;
                provider.Schedule(this, dueTime, period);
                return true;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    provider.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal bool IsDue(long now) => Volatile.Read(ref _disposed) == 0 && DueAt <= now;

            internal void RescheduleAfterFire(long now)
            {
                DueAt = Period == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : checked(now + Period.Ticks);
            }

            internal void Fire()
            {
                if (Volatile.Read(ref _disposed) == 0)
                    callback(state);
            }
        }
    }
}
