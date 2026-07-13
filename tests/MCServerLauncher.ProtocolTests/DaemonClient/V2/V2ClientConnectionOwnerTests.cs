using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.Protocol;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientConnectionOwnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task InitialReadinessUsesHandshakeCatalogAndRegistryWithoutAuthRpc()
    {
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory);

        var connecting = owner.ConnectAsync();
        var session = await factory.NextAsync();
        Assert.Equal(V2ClientConnectionOwnerState.Connecting, owner.State);
        session.CompleteConnect();

        var subscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        session.RouteSuccess(subscribe);
        var catalog = await session.Transport.NextAsync();
        Assert.Equal("mcsl.instance.catalog.get", catalog.Method);
        session.RouteSuccess(catalog, Catalog(0));

        Assert.True((await connecting.WaitAsync(Timeout)).IsOk(out _));
        Assert.True(owner.IsReady);
        Assert.True(owner.TryGetReadyCore(out var core));
        Assert.Same(session.Coordinator.Core, core);
        Assert.DoesNotContain(session.Transport.Requests, request => request.Method == "mcsl.auth.permissions.get");
        Assert.Equal(["connect", "mcsl.event.subscribe", "mcsl.instance.catalog.get"], session.Operations);
    }

    [Fact]
    public async Task InitialCallerTokenIsDetachedAfterFirstReady()
    {
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory);
        using var cancellation = new CancellationTokenSource();
        var connecting = owner.ConnectAsync(cancellation.Token);
        var session = await factory.NextAsync();
        await MakeReadyAsync(session, connecting, Catalog(0));

        cancellation.Cancel();

        Assert.True(owner.IsReady);
        Assert.Equal(1, factory.CreatedCount);
        Assert.Equal(0, session.CloseCount);
    }

    [Fact]
    public async Task InitialCancellationCleansEpochAndAllowsExplicitRetry()
    {
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory);
        using var cancellation = new CancellationTokenSource();

        var firstConnect = owner.ConnectAsync(cancellation.Token);
        var first = await factory.NextAsync();
        await first.ConnectStarted.Task.WaitAsync(Timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstConnect.WaitAsync(Timeout));
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.Created);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, factory.CreatedCount);

        var retry = owner.ConnectAsync();
        var replacement = await factory.NextAsync();
        await MakeReadyAsync(replacement, retry, Catalog(0));
        Assert.True(owner.IsReady);
        Assert.Equal(2, factory.CreatedCount);
    }

    [Fact]
    public async Task CallerCancellationWinsRegistryBindInvalidationRace()
    {
        var time = new ThrowOnceManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var initial = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, initial, Catalog(0));
        var handle = await SubscribeLogAsync(owner, first);
        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.Created);

        using var cancellation = new CancellationTokenSource();
        var reconnecting = owner.ConnectAsync(cancellation.Token);
        var replacement = await factory.NextAsync();
        replacement.CompleteConnect();
        await CompleteCatalogAsync(replacement, Catalog(1));
        var replay = await replacement.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", replay.Method);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconnecting.WaitAsync(Timeout));
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.Created);
        Assert.Equal(1, replacement.CloseCount);
        await handle.DisposeAsync().AsTask().WaitAsync(Timeout);
    }

    [Fact]
    public async Task FactoryFailureReturnsTypedErrorAndLeavesOwnerRetryable()
    {
        await using var owner = new V2ClientConnectionOwner(
            new ThrowingSessionFactory(),
            TimeProvider.System,
            ReconnectDelay);

        var result = await owner.ConnectAsync().WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.session_failed", error!.Code);
        Assert.Equal(V2ClientConnectionOwnerState.Created, owner.State);
    }

    [Fact]
    public async Task LossPreemptsCancellationIgnoringConnectAndConvergesStage()
    {
        var factory = new ControlledSessionFactory((_, session) =>
            session.IgnoreConnectCancellation = true);
        await using var owner = Owner(factory);
        var connecting = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);

        session.Lose("transport.connect_lost");
        var result = await connecting.WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.connect_lost", error!.Code);
        Assert.Equal(V2ClientConnectionOwnerState.Created, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task LossPreemptsCoordinatorSynchronization()
    {
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory);
        var connecting = owner.ConnectAsync();
        var session = await factory.NextAsync();
        session.CompleteConnect();
        var subscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);

        session.Lose("transport.synchronization_lost");
        var result = await connecting.WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.synchronization_lost", error!.Code);
        Assert.Equal(V2ClientConnectionOwnerState.Created, owner.State);
        Assert.Equal(1, session.CloseCount);
    }

    [Fact]
    public async Task LossPreemptsRegistryBindingAndReplacementPolicyContinues()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));
        var handle = await SubscribeLogAsync(owner, first);

        first.Lose("transport.first_lost");
        await time.TimerCreated.Task.WaitAsync(Timeout);
        time.Advance(ReconnectDelay);
        var binding = await factory.NextAsync();
        binding.CompleteConnect();
        await CompleteCatalogAsync(binding, Catalog(1));
        var replay = await binding.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", replay.Method);

        binding.Lose("transport.binding_lost");
        await WaitUntilAsync(() => binding.CloseCount == 1 && time.TimerCount == 2);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(2), replaySubscriptions: 1);

        Assert.True(owner.IsReady);
        Assert.Equal(3, factory.CreatedCount);
        await DisposeLogAsync(owner, replacement, handle);
    }

    [Fact]
    public async Task UnexpectedOperationCanceledExceptionReturnsTypedSessionFailure()
    {
        var factory = new ControlledSessionFactory((_, session) =>
            session.ThrowUnexpectedCancellation = true);
        await using var owner = Owner(factory);

        var result = await owner.ConnectAsync().WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.session_failed", error!.Code);
        Assert.Equal(V2ClientConnectionOwnerState.Created, owner.State);
    }

    [Fact]
    public async Task UnexpectedCancellationDuringReplacementContinuesReconnectPolicy()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory((index, session) =>
        {
            if (index == 2)
                session.ThrowUnexpectedCancellation = true;
        });
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));

        first.Lose("transport.peer_closed");
        await time.TimerCreated.Task.WaitAsync(Timeout);
        time.Advance(ReconnectDelay);
        var canceled = await factory.NextAsync();
        await canceled.ConnectStarted.Task.WaitAsync(Timeout);
        await WaitUntilAsync(() => canceled.CloseCount == 1 && time.TimerCount == 2);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(1));

        Assert.True(owner.IsReady);
        Assert.Equal(3, factory.CreatedCount);
    }

    [Fact]
    public async Task UnexpectedReconnectDelayFaultLeavesExplicitRetryableState()
    {
        var time = new ThrowOnceManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var initial = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, initial, Catalog(0));

        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.Created);

        Assert.False(owner.IsReady);
        Assert.Equal("transport.session_failed", owner.LastFailure!.Code);
        var retry = owner.ConnectAsync();
        var replacement = await factory.NextAsync();
        await MakeReadyAsync(replacement, retry, Catalog(1));
        Assert.True(owner.IsReady);
        Assert.Null(owner.LastFailure);
    }

    [Fact]
    public void ReconnectDelayOutsideTaskDelayRangeFailsAtConstruction()
    {
        var factory = new ControlledSessionFactory();
        var oversized = TimeSpan.FromMilliseconds(uint.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new V2ClientConnectionOwner(factory, TimeProvider.System, oversized));
    }

    [Fact]
    public async Task NaturalLossObservesDetachAndCancellationFailuresExactlyOnce()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory((_, session) =>
        {
            session.IgnoreConnectCancellation = true;
            session.ThrowOnCancellation = true;
            session.RetainCancellationRegistration = true;
        });
        await using var owner = new V2ClientConnectionOwner(factory, time, ReconnectDelay, diagnostics.Add);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);
        session.ThrowNextCoordinatorAccess = true;

        session.Lose("transport.peer_closed");
        Assert.True((await connect.WaitAsync(Timeout)).IsErr(out _));

        Assert.Equal(2, diagnostics.Count(diagnostic =>
            diagnostic.Message.StartsWith("V2 epoch cleanup failed:", StringComparison.Ordinal)));
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task CancellationCallbackMayReenterCloseWithoutDeadlock()
    {
        var factory = new ControlledSessionFactory((_, session) =>
        {
            session.IgnoreConnectCancellation = true;
            session.RetainCancellationRegistration = true;
        });
        var owner = Owner(factory);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);
        var reentered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? reentrantClose = null;
        session.CancellationReentry = () =>
        {
            reentrantClose = owner.CloseAsync();
            reentered.TrySetResult();
        };

        session.Lose("transport.peer_closed");
        await reentered.Task.WaitAsync(Timeout);
        await reentrantClose!.WaitAsync(Timeout);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
        Assert.True((await connect.WaitAsync(Timeout)).IsErr(out _));
    }

    [Fact]
    public async Task CloseAggregatesCancellationAndCleanupFailuresButStillConverges()
    {
        var factory = new ControlledSessionFactory((_, session) =>
        {
            session.IgnoreConnectCancellation = true;
            session.ThrowOnCancellation = true;
            session.ThrowOnClose = true;
            session.ThrowOnDispose = true;
            session.CompletionNeverTerminates = true;
        });
        var owner = Owner(factory);
        var connecting = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => owner.CloseAsync().WaitAsync(Timeout));
        var messages = exception.Flatten().InnerExceptions.Select(static failure => failure.Message).ToArray();

        Assert.Contains("cancellation callback failed", messages);
        Assert.Contains("session close failed", messages);
        Assert.Contains("session dispose failed", messages);
        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
        Assert.True((await connecting.WaitAsync(Timeout)).IsErr(out _));
    }

    [Fact]
    public async Task MisbehavingCompletionMonitorIsCanceledAndAwaitedOnClose()
    {
        var factory = new ControlledSessionFactory((_, session) =>
            session.CompletionNeverTerminates = true);
        var owner = Owner(factory);
        var connecting = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await MakeReadyAsync(session, connecting, Catalog(0));

        await owner.CloseAsync().WaitAsync(Timeout);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentConnectIsRejectedWithoutChangingActiveAttempt()
    {
        var factory = new ControlledSessionFactory();
        var owner = Owner(factory);
        var first = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);
        Assert.Equal(V2ClientConnectionOwnerState.Connecting, owner.State);
        using var barrier = new Barrier(2);
        var second = Task.Factory.StartNew(
            () =>
            {
                Assert.True(barrier.SignalAndWait(Timeout));
                return Record.Exception((Action)(() => _ = owner.ConnectAsync()));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(barrier.SignalAndWait(Timeout));

        Assert.IsType<InvalidOperationException>(await second.WaitAsync(Timeout));
        Assert.Equal(1, factory.CreatedCount);

        await owner.CloseAsync().WaitAsync(Timeout);
        Assert.True((await first.WaitAsync(Timeout)).IsErr(out _));
    }

    [Fact]
    public async Task CloseWhileFactoryIsPublishingStillConvergesCreatedEpoch()
    {
        using var factory = new BlockingSessionFactory();
        var owner = new V2ClientConnectionOwner(factory, TimeProvider.System, ReconnectDelay);
        var connecting = owner.ConnectAsync();
        await factory.CreateEntered.Task.WaitAsync(Timeout);

        var closing = owner.CloseAsync();
        factory.ReleaseCreate.Set();
        await closing.WaitAsync(Timeout);
        var session = await factory.Created.Task.WaitAsync(Timeout);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
        Assert.True((await connecting.WaitAsync(Timeout)).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
    }

    [Fact]
    public async Task LossClearsReadinessThenReconnectsOnceAfterInjectedDelay()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));

        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.WaitingToReconnect);
        Assert.False(owner.IsReady);
        Assert.False(owner.TryGetReadyCore(out _));
        Assert.Equal(1, factory.CreatedCount);
        await time.TimerCreated.Task.WaitAsync(Timeout);

        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(1));
        Assert.True(owner.IsReady);
        Assert.Equal(2, factory.CreatedCount);
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, first.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentInvalidationAndLossCoalesceAndStaleEpochCannotDropReplacement()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));
        var handle = await SubscribeLogAsync(owner, first);

        using var barrier = new Barrier(3);
        var invalidation = StartBarrierAction(barrier, first.RouteMalformedLog);
        var loss = StartBarrierAction(barrier, () => first.Lose("transport.concurrent_loss"));
        Assert.True(barrier.SignalAndWait(Timeout));
        await Task.WhenAll(invalidation, loss).WaitAsync(Timeout);
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.WaitingToReconnect);
        await time.TimerCreated.Task.WaitAsync(Timeout);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(1), replaySubscriptions: 1);

        first.RouteMalformedLog();
        first.Lose("transport.stale");
        Assert.True(owner.IsReady);
        Assert.Equal(2, factory.CreatedCount);
        Assert.Equal(1, first.CloseCount);
        await DisposeLogAsync(owner, replacement, handle);
    }

    [Fact]
    public async Task LiveSubscriptionReplaysButDisposedHandleDoesNot()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));
        var live = await SubscribeLogAsync(owner, first);
        var disposed = await SubscribeLogAsync(owner, first, Guid.NewGuid());
        await DisposeLogAsync(owner, first, disposed);

        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.WaitingToReconnect);
        await time.TimerCreated.Task.WaitAsync(Timeout);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(1), replaySubscriptions: 1);

        Assert.Equal(3, replacement.Transport.Requests.Count);
        Assert.Equal(1, replacement.Transport.Requests.Count(request => request.Method == "mcsl.event.subscribe" &&
            Params(request).GetProperty("event").GetString() == "mcsl.event.instance.log"));
        await DisposeLogAsync(owner, replacement, live);
    }

    [Fact]
    public async Task ReplayFailureClosesCandidateAndRetriesWithFreshEpoch()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));
        var handle = await SubscribeLogAsync(owner, first);

        first.Lose("transport.peer_closed");
        await time.TimerCreated.Task.WaitAsync(Timeout);
        time.Advance(ReconnectDelay);
        var failed = await factory.NextAsync();
        failed.CompleteConnect();
        await CompleteCatalogAsync(failed, Catalog(1));
        var replay = await failed.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", replay.Method);
        failed.RouteError(replay, "subscription.denied", "permission");

        await WaitUntilAsync(() => failed.CloseCount == 1);
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.WaitingToReconnect);
        await WaitUntilAsync(() => time.TimerCount == 2);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(2), replaySubscriptions: 1);

        Assert.True(owner.IsReady);
        Assert.Equal(3, factory.CreatedCount);
        Assert.NotSame(failed.Coordinator, replacement.Coordinator);
        await DisposeLogAsync(owner, replacement, handle);
    }

    [Fact]
    public async Task CleanupFailuresAfterReadyAreDiagnosedAndReconnectContinues()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory((index, session) =>
        {
            if (index == 1)
            {
                session.ThrowOnClose = true;
                session.ThrowOnDispose = true;
            }
        });
        await using var owner = new V2ClientConnectionOwner(
            factory,
            time,
            ReconnectDelay,
            diagnostics.Add);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(0));

        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => time.TimerCount == 1);
        time.Advance(ReconnectDelay);
        var replacement = await factory.NextAsync();
        await MakeReplacementReadyAsync(owner, replacement, Catalog(1));

        Assert.True(owner.IsReady);
        Assert.Equal(2, diagnostics.Count(diagnostic =>
            diagnostic.Message.StartsWith("V2 epoch cleanup failed:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MirrorHandleAndSnapshotRemainReadableWhileDisconnected()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var first = await factory.NextAsync();
        await MakeReadyAsync(first, connect, Catalog(4, Item("retained")));
        var published = owner.Mirror.Current;

        first.Lose("transport.peer_closed");
        await WaitUntilAsync(() => owner.State == V2ClientConnectionOwnerState.WaitingToReconnect);

        Assert.Same(published, owner.Mirror.Current);
        Assert.Equal("retained", owner.Mirror.Current.Value.Instances[InstanceId].Name);
        Assert.False(owner.TryGetReadyCore(out _));
    }

    [Fact]
    public async Task EpochLossCompletesPendingRequestWithTypedTransportError()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        await using var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await MakeReadyAsync(session, connect, Catalog(0));
        Assert.True(owner.TryGetReadyCore(out var core));

        var pending = core.InvokeAsync(V2ClientProtocol.GetAuthPermissions, new EmptyRequest());
        await session.Transport.NextAsync();
        session.Lose("transport.peer_closed");
        var result = await pending.WaitAsync(Timeout);

        Assert.True(result.IsErr(out var error));
        Assert.Equal(DaemonErrorKind.Transport, error!.Kind);
        Assert.Equal("connection.closed", error.Code);
    }

    [Fact]
    public async Task ExplicitCloseDuringConnectingIsTerminalAndIdempotent()
    {
        var factory = new ControlledSessionFactory();
        var owner = Owner(factory);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await session.ConnectStarted.Task.WaitAsync(Timeout);

        var first = owner.CloseAsync();
        var second = owner.CloseAsync();
        Assert.Same(first, second);
        await first.WaitAsync(Timeout);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
        Assert.True((await connect.WaitAsync(Timeout)).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task ExplicitCloseDuringReconnectDelayPreventsReplacement()
    {
        var time = new ManualTimeProvider();
        var factory = new ControlledSessionFactory();
        var owner = Owner(factory, time);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await MakeReadyAsync(session, connect, Catalog(0));
        session.Lose("transport.peer_closed");
        await time.TimerCreated.Task.WaitAsync(Timeout);

        await owner.CloseAsync().WaitAsync(Timeout);
        time.Advance(ReconnectDelay);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, factory.CreatedCount);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task ExplicitCloseWhileReadyCleansEpochOnceAndClosesMirror()
    {
        var factory = new ControlledSessionFactory();
        var owner = Owner(factory);
        var connect = owner.ConnectAsync();
        var session = await factory.NextAsync();
        await MakeReadyAsync(session, connect, Catalog(0));

        await Task.WhenAll(owner.CloseAsync(), owner.CloseAsync()).WaitAsync(Timeout);

        Assert.Equal(V2ClientConnectionOwnerState.Closed, owner.State);
        Assert.Equal(1, session.CloseCount);
        Assert.Equal(1, session.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => owner.Mirror.BeginReconciliation());
        Assert.True((await owner.ConnectAsync()).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await owner.DisposeAsync();
    }

    private static V2ClientConnectionOwner Owner(
        ControlledSessionFactory factory,
        TimeProvider? timeProvider = null) =>
        new(factory, timeProvider ?? TimeProvider.System, ReconnectDelay);

    private static async Task MakeReadyAsync(
        ControlledSession session,
        Task<Result<Unit, DaemonError>> connect,
        string catalog)
    {
        session.CompleteConnect();
        await CompleteCatalogAsync(session, catalog);
        Assert.True((await connect.WaitAsync(Timeout)).IsOk(out _));
    }

    private static async Task MakeReplacementReadyAsync(
        V2ClientConnectionOwner owner,
        ControlledSession session,
        string catalog,
        int replaySubscriptions = 0)
    {
        session.CompleteConnect();
        await CompleteCatalogAsync(session, catalog);
        for (var index = 0; index < replaySubscriptions; index++)
        {
            var replay = await session.Transport.NextAsync();
            Assert.Equal("mcsl.event.subscribe", replay.Method);
            session.RouteSuccess(replay);
        }

        await WaitUntilAsync(() => session.Coordinator.IsReady && owner.IsReady);
    }

    private static async Task CompleteCatalogAsync(ControlledSession session, string catalog)
    {
        var subscribe = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        session.RouteSuccess(subscribe);
        var read = await session.Transport.NextAsync();
        Assert.Equal("mcsl.instance.catalog.get", read.Method);
        session.RouteSuccess(read, catalog);
    }

    private static async Task<IAsyncDisposable> SubscribeLogAsync(
        V2ClientConnectionOwner owner,
        ControlledSession session,
        Guid? instanceId = null)
    {
        var filter = instanceId is { } id
            ? V2ClientEventFilter<InstanceLogEventMeta>.Exact(new(id))
            : V2ClientEventFilter<InstanceLogEventMeta>.Wildcard;
        var subscribing = owner.Subscriptions.SubscribeAsync(V2ClientProtocol.InstanceLog, filter, _ => { });
        var request = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", request.Method);
        session.RouteSuccess(request);
        return (await subscribing.WaitAsync(Timeout)).Unwrap();
    }

    private static async Task DisposeLogAsync(
        V2ClientConnectionOwner owner,
        ControlledSession session,
        IAsyncDisposable handle)
    {
        var disposing = handle.DisposeAsync().AsTask();
        var request = await session.Transport.NextAsync();
        Assert.Equal("mcsl.event.unsubscribe", request.Method);
        session.RouteSuccess(request);
        await disposing.WaitAsync(Timeout);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = Task.Delay(Timeout);
        while (!predicate())
        {
            if (timeout.IsCompleted)
                throw new TimeoutException("The expected owner state was not reached.");
            await Task.Yield();
        }
    }

    private static Task StartBarrierAction(Barrier barrier, Action action) =>
        Task.Factory.StartNew(
            () =>
            {
                Assert.True(barrier.SignalAndWait(Timeout));
                action();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static JsonElement Params(SentRequest request)
    {
        using var document = JsonDocument.Parse(request.Json);
        return document.RootElement.GetProperty("params").Clone();
    }

    private static string Catalog(long version, params string[] items) =>
        $"{{\"version\":{version},\"items\":[{string.Join(',', items)}]}}";

    private static string Item(string name) =>
        $"{{\"instance_id\":\"{InstanceId:D}\",\"name\":\"{name}\",\"instance_type\":\"universal\",\"version\":\"1\",\"status\":\"running\"}}";

    private sealed record SentRequest(string Method, string IdJson, string Json);

    private sealed class ControlledSessionFactory : IV2ClientConnectionSessionFactory
    {
        private readonly ConcurrentQueue<ControlledSession> _sessions = new();
        private readonly SemaphoreSlim _available = new(0);
        private readonly Action<int, ControlledSession>? _configure;
        private int _createdCount;

        internal ControlledSessionFactory(Action<int, ControlledSession>? configure = null)
        {
            _configure = configure;
        }

        internal int CreatedCount => Volatile.Read(ref _createdCount);

        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null)
        {
            var session = new ControlledSession(mirror, routeEvent, diagnostic);
            var index = Interlocked.Increment(ref _createdCount);
            _configure?.Invoke(index, session);
            _sessions.Enqueue(session);
            _available.Release();
            return session;
        }

        internal async Task<ControlledSession> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_sessions.TryDequeue(out var session));
            return session!;
        }
    }

    private sealed class ThrowingSessionFactory : IV2ClientConnectionSessionFactory
    {
        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null) =>
            throw new InvalidOperationException("factory failed");
    }

    private sealed class BlockingSessionFactory : IV2ClientConnectionSessionFactory, IDisposable
    {
        internal TaskCompletionSource CreateEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseCreate { get; } = new();
        internal TaskCompletionSource<ControlledSession> Created { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null)
        {
            CreateEntered.TrySetResult();
            if (!ReleaseCreate.Wait(Timeout))
                throw new TimeoutException("The blocking session factory was not released.");
            var session = new ControlledSession(mirror, routeEvent, diagnostic);
            Created.TrySetResult(session);
            return session;
        }

        public void Dispose() => ReleaseCreate.Dispose();
    }

    private sealed class ControlledSession : IV2ClientConnectionSession
    {
        private readonly TaskCompletionSource<Result<Unit, DaemonError>> _connect =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<DaemonError> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closeCount;
        private int _disposeCount;
        private int _throwNextCoordinatorAccess;
        private CancellationTokenRegistration _persistentCancellationRegistration;
        private readonly V2ClientConnectionCoordinator _coordinator;

        internal ControlledSession(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic)
        {
            Transport = new ScriptedTransport(Operations);
            var next = 0;
            _coordinator = new V2ClientConnectionCoordinator(
                Transport,
                mirror,
                TimeProvider.System,
                TimeSpan.FromMinutes(1),
                () => JsonRpcRequestId.FromString($"owner-{Interlocked.Increment(ref next)}"),
                diagnostic,
                routeEvent);
        }

        internal ScriptedTransport Transport { get; }
        internal List<string> Operations { get; } = [];
        internal TaskCompletionSource ConnectStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CloseCount => Volatile.Read(ref _closeCount);
        internal int DisposeCount => Volatile.Read(ref _disposeCount);
        internal bool IgnoreConnectCancellation { get; set; }
        internal bool ThrowUnexpectedCancellation { get; set; }
        internal bool ThrowOnCancellation { get; set; }
        internal bool ThrowOnClose { get; set; }
        internal bool ThrowOnDispose { get; set; }
        internal bool CompletionNeverTerminates { get; set; }
        internal bool RetainCancellationRegistration { get; set; }
        internal Action? CancellationReentry { get; set; }
        internal bool ThrowNextCoordinatorAccess
        {
            set => Volatile.Write(ref _throwNextCoordinatorAccess, value ? 1 : 0);
        }
        public V2ClientConnectionCoordinator Coordinator
        {
            get
            {
                if (Interlocked.Exchange(ref _throwNextCoordinatorAccess, 0) != 0)
                    throw new InvalidOperationException("coordinator access failed");
                return _coordinator;
            }
        }
        public Task<DaemonError> Completion => _completion.Task;

        public async Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken)
        {
            Operations.Add("connect");
            ConnectStarted.TrySetResult();
            if (ThrowUnexpectedCancellation)
            {
                using var unexpected = new CancellationTokenSource();
                unexpected.Cancel();
                return await Task.FromCanceled<Result<Unit, DaemonError>>(unexpected.Token);
            }

            var registration = (ThrowOnCancellation || RetainCancellationRegistration)
                ? cancellationToken.Register(() =>
                {
                    CancellationReentry?.Invoke();
                    if (ThrowOnCancellation)
                        throw new InvalidOperationException("cancellation callback failed");
                })
                : default;
            if (RetainCancellationRegistration)
                _persistentCancellationRegistration = registration;
            try
            {
                return IgnoreConnectCancellation
                    ? await _connect.Task
                    : await _connect.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                if (!RetainCancellationRegistration)
                    registration.Dispose();
            }
        }

        public Task CloseAsync()
        {
            Interlocked.Increment(ref _closeCount);
            _connect.TrySetResult(Result.Err<Unit, DaemonError>(
                new TransportDaemonError("connection.closed", "closed")));
            if (!CompletionNeverTerminates)
                _completion.TrySetResult(new TransportDaemonError("connection.closed", "closed"));
            return ThrowOnClose
                ? Task.FromException(new IOException("session close failed"))
                : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _persistentCancellationRegistration.Dispose();
            return ThrowOnDispose
                ? new ValueTask(Task.FromException(new IOException("session dispose failed")))
                : ValueTask.CompletedTask;
        }

        internal void CompleteConnect() =>
            _connect.TrySetResult(Result.Ok<Unit, DaemonError>(Unit.Default));

        internal void Lose(string code) =>
            _completion.TrySetResult(new TransportDaemonError(code, "lost"));

        internal void RouteSuccess(SentRequest request, string result = "{}") =>
            Coordinator.Core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}"));

        internal void RouteError(SentRequest request, string code, string kind) =>
            Coordinator.Core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"error\":{{\"code\":-32000,\"message\":\"failed\",\"data\":{{\"daemon_error_code\":\"{code}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"test\"}}}}}}"));

        internal void RouteMalformedLog() =>
            Coordinator.Core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{\"sequence\":1,\"timestamp\":2,\"data\":{}}}"));

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    }

    private sealed class ScriptedTransport(List<string> operations) : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _requests = new();
        private readonly ConcurrentQueue<SentRequest> _history = new();
        private readonly SemaphoreSlim _available = new(0);
        internal IReadOnlyCollection<SentRequest> Requests => _history.ToArray();

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(utf8Json.AsSpan());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var request = new SentRequest(
                root.GetProperty("method").GetString()!,
                root.GetProperty("id").GetRawText(),
                json);
            _requests.Enqueue(request);
            _history.Enqueue(request);
            operations.Add(request.Method);
            _available.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This text-only test transport does not support binary frames.");

        internal async Task<SentRequest> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_requests.TryDequeue(out var request));
            return request!;
        }
    }

    private class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private int _timerCount;
        internal TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int TimerCount => Volatile.Read(ref _timerCount);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            lock (_timers)
            {
                _timers.Add(timer);
            }
            Interlocked.Increment(ref _timerCount);
            TimerCreated.TrySetResult();
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            ManualTimer[] timers;
            lock (_timers)
            {
                timers = _timers.ToArray();
            }
            foreach (var timer in timers)
                timer.Advance(elapsed);
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan remaining) : ITimer
        {
            private int _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                remaining = dueTime;
                return Volatile.Read(ref _disposed) == 0;
            }
            internal void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (remaining <= TimeSpan.Zero && Interlocked.Exchange(ref _disposed, 1) == 0)
                    callback(state);
            }
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowOnceManualTimeProvider : ManualTimeProvider
    {
        private int _throwsRemaining = 1;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Exchange(ref _throwsRemaining, 0) != 0)
                throw new InvalidOperationException("timer creation failed");
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
