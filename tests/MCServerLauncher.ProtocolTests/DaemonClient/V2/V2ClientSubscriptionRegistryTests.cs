using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.Protocol;
using MCServerLauncher.DaemonClient.State;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientSubscriptionRegistryTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly List<V2ClientConnectionCoordinator> _coordinators = [];
    private readonly List<V2ClientSubscriptionRegistry> _registries = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        List<Exception>? failures = null;
        foreach (var registry in _registries)
        {
            try
            {
                await registry.DisposeAsync().AsTask().WaitAsync(Timeout);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        foreach (var coordinator in _coordinators)
        {
            try
            {
                await coordinator.CloseAsync().WaitAsync(Timeout);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more subscription registry test cleanups failed.", failures);
    }

    [Fact]
    public void PublicFilterDefaultIsWildcardAndOtherStatesRemainDistinct()
    {
        DaemonEventFilter<InstanceLogEventMeta> wildcard = default;

        Assert.Equal(DaemonEventFieldKind.Missing, wildcard.Kind);
        Assert.Equal(DaemonEventFieldKind.ExplicitNull,
            DaemonEventFilter<InstanceLogEventMeta>.ExplicitNull.Kind);
        Assert.Equal(DaemonEventFieldKind.Value,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)).Kind);
        Assert.Throws<InvalidOperationException>(() => wildcard.Value);
    }

    [Fact]
    public async Task FirstLastAndDuplicateHandlesUseOneServerSubscription()
    {
        var fixture = await ReadyFixtureAsync();
        var firstTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)),
            _ => { });
        var subscribe = await fixture.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        Assert.Equal(InstanceId.ToString("D"), Params(subscribe).GetProperty("meta").GetProperty("instance_id").GetString());
        fixture.Coordinator.Core.RouteText(Success(subscribe));
        var first = (await firstTask.WaitAsync(Timeout)).Unwrap();

        var second = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)),
            _ => { }).WaitAsync(Timeout)).Unwrap();
        Assert.Equal(3, fixture.Transport.SendCount);
        await first.DisposeAsync();
        Assert.Equal(3, fixture.Transport.SendCount);

        await DisposeHandleAsync(fixture, second);
        await second.DisposeAsync();
        Assert.Equal(4, fixture.Transport.SendCount);
    }

    [Fact]
    public async Task AckRaceBuffersThenDrainsAndWildcardExactCallbacksUseRegistrationOrder()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = new List<string>();
        var wildcardTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value => calls.Add("wildcard:" + value.Data.Value.Log));
        var wildcardRequest = await fixture.Transport.NextAsync();
        Assert.False(Params(wildcardRequest).TryGetProperty("meta", out _));
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));
        Assert.Empty(calls);
        fixture.Coordinator.Core.RouteText(Success(wildcardRequest));
        var wildcard = (await wildcardTask.WaitAsync(Timeout)).Unwrap();

        var exactTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)),
            value => calls.Add("exact:" + value.Data.Value.Log));
        var exactRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(exactRequest));
        var exact = (await exactTask).Unwrap();
        fixture.Coordinator.Core.RouteText(LogEvent(2, Guid.NewGuid(), "other"));
        fixture.Coordinator.Core.RouteText(LogEvent(3, InstanceId, "matching"));

        Assert.Equal([
            "wildcard:buffered",
            "wildcard:other",
            "wildcard:matching",
            "exact:matching"
        ], calls);
        await DisposeHandleAsync(fixture, wildcard);
        await DisposeHandleAsync(fixture, exact);
    }

    [Fact]
    public async Task EventsArrivingDuringAckDrainRemainOrderedBeforeActivation()
    {
        var fixture = await ReadyFixtureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = new List<string>();
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value =>
            {
                calls.Add(value.Data.Value.Log);
                if (value.Data.Value.Log == "first")
                {
                    entered.TrySetResult();
                    Assert.True(release.Wait(Timeout));
                }
            });
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "first"));
        fixture.Coordinator.Core.RouteText(Success(request));
        await entered.Task.WaitAsync(Timeout);

        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "second"));
        Assert.Equal(["first"], calls);
        release.Set();
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        Assert.Equal(["first", "second"], calls);
        await DisposeHandleAsync(fixture, handle);
    }

    [Fact]
    public async Task DetachDuringAckDrainStopsLaterOldEpochDeliveryWithoutDeadlock()
    {
        var fixture = await ReadyFixtureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = new List<string>();
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value =>
            {
                calls.Add(value.Data.Value.Log);
                entered.TrySetResult();
                Assert.True(release.Wait(Timeout));
            });
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "first"));
        fixture.Coordinator.Core.RouteText(Success(request));
        await entered.Task.WaitAsync(Timeout);

        fixture.Registry.DetachEpoch(fixture.Coordinator);
        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "stale"));
        release.Set();
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        Assert.Equal(["first"], calls);
        Assert.Empty(fixture.Invalidations);
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task ReplayCallbackMaySynchronouslyDisposeItsHandleWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        IAsyncDisposable? handle = null;
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle = await SubscribeAsync(first, _ =>
        {
            Assert.True(handle!.DisposeAsync().AsTask().Wait(Timeout));
            callbackCompleted.TrySetResult();
        });
        first.Registry.DetachEpoch(first.Coordinator);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        var replay = await replacement.Transport.NextAsync();
        replacement.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));
        var unsubscribeResponder = RespondNextSuccessAsync(replacement, "mcsl.event.unsubscribe");
        replacement.Coordinator.Core.RouteText(Success(replay));

        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        await callbackCompleted.Task.WaitAsync(Timeout);
        await unsubscribeResponder.WaitAsync(Timeout);
        Assert.False(V2ClientSubscriptionRegistry.HandleRetainsResources(handle));
    }

    [Fact]
    public async Task ReplayCallbackMaySynchronouslySubscribeWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        IAsyncDisposable? reportHandle = null;
        var logHandle = await SubscribeAsync(first, _ =>
        {
            var subscription = first.Registry.SubscribeAsync(
                V2ClientProtocol.DaemonReport,
                DaemonEventFilter<EmptyRequest>.Wildcard,
                _ => { });
            Assert.True(subscription.Wait(Timeout));
            reportHandle = subscription.Result.Unwrap();
        });
        first.Registry.DetachEpoch(first.Coordinator);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        var replay = await replacement.Transport.NextAsync();
        replacement.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));
        var subscribeResponder = RespondNextSuccessAsync(replacement, "mcsl.event.subscribe");
        replacement.Coordinator.Core.RouteText(Success(replay));

        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        await subscribeResponder.WaitAsync(Timeout);
        Assert.NotNull(reportHandle);
        await DisposeHandleAsync(replacement, logHandle);
        await DisposeHandleAsync(replacement, reportHandle!);
    }

    [Fact]
    public async Task ReplayCallbackMaySynchronouslyDisposeRegistryWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = await SubscribeAsync(first, _ =>
        {
            Assert.True(first.Registry.DisposeAsync().AsTask().Wait(Timeout));
            callbackCompleted.TrySetResult();
        });
        first.Registry.DetachEpoch(first.Coordinator);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        var replay = await replacement.Transport.NextAsync();
        replacement.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));
        var unsubscribeResponder = RespondNextSuccessAsync(replacement, "mcsl.event.unsubscribe");
        replacement.Coordinator.Core.RouteText(Success(replay));

        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        await callbackCompleted.Task.WaitAsync(Timeout);
        await unsubscribeResponder.WaitAsync(Timeout);
        Assert.False(V2ClientSubscriptionRegistry.HandleRetainsResources(handle));
    }

    [Fact]
    public async Task RegistryDisposeClearsHandleResourcesAndIsIdempotent()
    {
        var fixture = await ReadyFixtureAsync();
        var callbackOwner = new CallbackOwner();
        var weakOwner = new WeakReference<CallbackOwner>(callbackOwner);
        var handle = await SubscribeAsync(fixture, callbackOwner.Accept);
        Assert.True(V2ClientSubscriptionRegistry.HandleRetainsResources(handle));
        Assert.True(weakOwner.TryGetTarget(out _));

        var dispose = fixture.Registry.DisposeAsync().AsTask();
        var unsubscribe = await fixture.Transport.NextAsync();
        Assert.Equal("mcsl.event.unsubscribe", unsubscribe.Method);
        fixture.Coordinator.Core.RouteText(Success(unsubscribe));
        await dispose.WaitAsync(Timeout);
        Assert.False(V2ClientSubscriptionRegistry.HandleRetainsResources(handle));
        Assert.False(V2ClientSubscriptionRegistry.HandleRetainsResources(handle));

        var sends = fixture.Transport.SendCount;
        await fixture.Registry.DisposeAsync().AsTask().WaitAsync(Timeout);
        await handle.DisposeAsync();
        Assert.Equal(sends, fixture.Transport.SendCount);
    }

    [Fact]
    public async Task CatalogInvalidFilterAndNoReadyEpochAreRejectedLocally()
    {
        var invalidations = new List<Invalidation>();
        var unbound = Registry(invalidations);
        var noReady = await unbound.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => { });
        AssertError(noReady, V2ClientSubscriptionRegistry.NotReadyCode);

        var fixture = await ReadyFixtureAsync();
        var catalog = await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceCatalogChanged,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => { });
        AssertError(catalog, V2ClientSubscriptionRegistry.UnsupportedEventCode);
        var invalidFilter = await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.ExplicitNull,
            _ => { });
        AssertError(invalidFilter, V2ClientSubscriptionRegistry.InvalidFilterCode);
        Assert.Equal(2, fixture.Transport.SendCount);
    }

    [Fact]
    public async Task ExpectedSubscribeFailureRollsBackButCancellationInvalidatesAmbiguousEpoch()
    {
        var expected = await ReadyFixtureAsync();
        var deniedTask = expected.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => throw new InvalidOperationException());
        var denied = await expected.Transport.NextAsync();
        expected.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "dropped"));
        expected.Coordinator.Core.RouteText(Error(denied, "permission.denied", "permission"));
        Assert.IsType<PermissionDaemonError>((await deniedTask).UnwrapErr());
        Assert.Empty(expected.Invalidations);

        using var cancellation = new CancellationTokenSource();
        var canceled = expected.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => { },
            cancellation.Token);
        await expected.Transport.NextAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(Timeout));
        var invalidation = Assert.Single(expected.Invalidations);
        Assert.Same(expected.Coordinator, invalidation.Epoch);
        Assert.Equal("client.subscription_canceled", invalidation.Error.Code);
    }

    [Fact]
    public async Task InvalidationOwnerHookMaySynchronouslyDisposeRegistryWithoutLaneDeadlock()
    {
        V2ClientSubscriptionRegistry? registry = null;
        var hookCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry = new V2ClientSubscriptionRegistry((_, _) =>
        {
            Assert.True(registry!.DisposeAsync().AsTask().Wait(Timeout));
            hookCompleted.TrySetResult();
        });
        _registries.Add(registry);
        var fixture = await ReadyFixtureAsync(registry: registry);
        using var cancellation = new CancellationTokenSource();
        var subscription = registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => { },
            cancellation.Token);
        await fixture.Transport.NextAsync();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.WaitAsync(Timeout));
        await hookCompleted.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task DetachRacingPendingSubscribeDropsBufferAndCompletesWithoutInvalidation()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = 0;
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => calls++);
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));

        fixture.Registry.DetachEpoch(fixture.Coordinator);
        fixture.Coordinator.Core.RouteText(Success(request));
        AssertError(await task.WaitAsync(Timeout), V2ClientSubscriptionRegistry.NotReadyCode);
        Assert.Equal(0, calls);
        Assert.Empty(fixture.Invalidations);
    }

    [Fact]
    public async Task ThrowingCallbackIsIsolatedAndMayReenterRegistry()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var fixture = await ReadyFixtureAsync(diagnostics.Add);
        var calls = new List<string>();
        var first = await SubscribeAsync(fixture, _ =>
        {
            calls.Add("first");
            fixture.Registry.DetachEpoch(fixture.Coordinator);
            throw new InvalidOperationException("secret");
        });
        var second = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => calls.Add("second"))).Unwrap();

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "line"));
        Assert.Equal(["first", "second"], calls);
        var diagnostic = Assert.Single(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ConsumerFault);
        Assert.DoesNotContain("secret", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [Fact]
    public async Task LastDisposeRemovesLocallyThenUnsubscribeErrorInvalidates()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = 0;
        var handle = await SubscribeAsync(fixture, _ => calls++);
        var dispose = handle.DisposeAsync().AsTask();
        var unsubscribe = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "after-remove"));
        fixture.Coordinator.Core.RouteText(Error(unsubscribe, "request.timeout", "transport"));
        await dispose.WaitAsync(Timeout);

        Assert.Equal(0, calls);
        Assert.Single(fixture.Invalidations);
        var rejected = await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => { });
        AssertError(rejected, V2ClientSubscriptionRegistry.NotReadyCode);
    }

    [Fact]
    public async Task LocalUnsubscribeTimeoutInvalidatesEpochWithoutCallerCancellation()
    {
        var time = new ManualTimeProvider();
        var fixture = await ReadyFixtureAsync(timeProvider: time, requestTimeout: TimeSpan.FromSeconds(1));
        var handle = await SubscribeAsync(fixture, _ => { });
        var dispose = handle.DisposeAsync().AsTask();
        await fixture.Transport.NextAsync();

        time.Advance(TimeSpan.FromSeconds(1));
        await dispose.WaitAsync(Timeout);
        var invalidation = Assert.Single(fixture.Invalidations);
        Assert.Equal("request.timeout", invalidation.Error.Code);
        Assert.Same(fixture.Coordinator, invalidation.Epoch);
    }

    [Fact]
    public async Task LastUnsubscribeMayFinishWhileReplacementWaitsForMutationLane()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var handle = await SubscribeAsync(first, _ => { });
        var dispose = handle.DisposeAsync().AsTask();
        var unsubscribe = await first.Transport.NextAsync();
        first.Registry.DetachEpoch(first.Coordinator);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        Assert.False(bind.IsCompleted);
        first.Coordinator.Core.RouteText(Success(unsubscribe));

        await dispose.WaitAsync(Timeout);
        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        Assert.Empty(first.Invalidations);
        Assert.Equal(2, replacement.Transport.SendCount);
    }

    [Fact]
    public async Task InvalidPhysicalEpochIdentityStaysRejectedWhileNewCoordinatorsBind()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        using var cancellation = new CancellationTokenSource();
        var subscription = first.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => { },
            cancellation.Token);
        await first.Transport.NextAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.WaitAsync(Timeout));
        Assert.Single(first.Invalidations);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        Assert.True((await first.Registry.BindReadyEpochAsync(replacement.Coordinator)).IsOk(out _));
        first.Registry.DetachEpoch(replacement.Coordinator);
        var rejected = await first.Registry.BindReadyEpochAsync(first.Coordinator);
        Assert.True(rejected.IsErr(out var error));
        Assert.Equal(V2ClientSubscriptionRegistry.NotReadyCode, error!.Code);

        var newest = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        Assert.True((await first.Registry.BindReadyEpochAsync(newest.Coordinator)).IsOk(out _));
    }

    [Fact]
    public async Task DetachHasNoRpcAndReplayRestoresOnlyLiveKeysInRegistrationOrder()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var log = await SubscribeAsync(first, _ => { });
        var notificationTask = first.Registry.SubscribeAsync(
            V2ClientProtocol.Notification,
            DaemonEventFilter<NotificationEventMeta>.Wildcard,
            _ => { });
        var notificationRequest = await first.Transport.NextAsync();
        first.Coordinator.Core.RouteText(Success(notificationRequest));
        var notification = (await notificationTask).Unwrap();
        var reportSubscription = first.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => { });
        var reportRequest = await first.Transport.NextAsync();
        first.Coordinator.Core.RouteText(Success(reportRequest));
        var report = (await reportSubscription).Unwrap();
        first.Registry.DetachEpoch(first.Coordinator);
        Assert.Equal(5, first.Transport.SendCount);
        await notification.DisposeAsync();

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        var logReplay = await replacement.Transport.NextAsync();
        Assert.Equal(V2ClientProtocol.InstanceLog.Name.Value, Params(logReplay).GetProperty("event").GetString());
        replacement.Coordinator.Core.RouteText(Success(logReplay));
        var reportReplay = await replacement.Transport.NextAsync();
        Assert.Equal(V2ClientProtocol.DaemonReport.Name.Value, Params(reportReplay).GetProperty("event").GetString());
        replacement.Coordinator.Core.RouteText(Success(reportReplay));
        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        Assert.Equal(4, replacement.Transport.SendCount);
        await DisposeHandleAsync(replacement, log);
        await DisposeHandleAsync(replacement, report);
    }

    [Fact]
    public async Task ReplayFailureRetainsHandlesAndStaleEpochIsIgnoredAfterLaterBind()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var calls = new List<string>();
        var handle = await SubscribeAsync(first, value => calls.Add(value.Data.Value.Log));
        var reportTask = first.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => { });
        var reportRequest = await first.Transport.NextAsync();
        first.Coordinator.Core.RouteText(Success(reportRequest));
        var reportHandle = (await reportTask).Unwrap();
        first.Registry.DetachEpoch(first.Coordinator);

        var failed = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var failedBind = first.Registry.BindReadyEpochAsync(failed.Coordinator);
        var successfulPrefix = await failed.Transport.NextAsync();
        Assert.Equal(V2ClientProtocol.InstanceLog.Name.Value, Params(successfulPrefix).GetProperty("event").GetString());
        failed.Coordinator.Core.RouteText(Success(successfulPrefix));
        var failedReplay = await failed.Transport.NextAsync();
        Assert.Equal(V2ClientProtocol.DaemonReport.Name.Value, Params(failedReplay).GetProperty("event").GetString());
        failed.Coordinator.Core.RouteText(Error(failedReplay, "permission.denied", "permission"));
        Assert.IsType<PermissionDaemonError>((await failedBind).UnwrapErr());
        Assert.Empty(first.Invalidations);

        var current = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(current.Coordinator);
        var replay = await current.Transport.NextAsync();
        current.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "ack-race"));
        Assert.Empty(calls);
        current.Coordinator.Core.RouteText(Success(replay));
        var reportReplay = await current.Transport.NextAsync();
        current.Coordinator.Core.RouteText(Success(reportReplay));
        Assert.True((await bind).IsOk(out _));
        Assert.Equal(["ack-race"], calls);
        first.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "stale"));
        current.Coordinator.Core.RouteText(LogEvent(3, InstanceId, "current"));
        Assert.Equal(["ack-race", "current"], calls);
        await DisposeHandleAsync(current, handle);
        await DisposeHandleAsync(current, reportHandle);
    }

    [Fact]
    public async Task PendingBufferOverflowInvalidatesWithoutPartialDelivery()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = 0;
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => calls++);
        var request = await fixture.Transport.NextAsync();
        for (var index = 0; index < V2ClientSubscriptionRegistry.PendingEventCapacity; index++)
            fixture.Coordinator.Core.RouteText(LogEvent(index + 1, InstanceId, $"line-{index}"));
        Assert.Empty(fixture.Invalidations);
        fixture.Coordinator.Core.RouteText(LogEvent(
            V2ClientSubscriptionRegistry.PendingEventCapacity + 1,
            InstanceId,
            "overflow"));
        Assert.Equal(V2ClientSubscriptionRegistry.BufferOverflowCode, Assert.Single(fixture.Invalidations).Error.Code);
        fixture.Coordinator.Core.RouteText(Success(request));
        AssertError(await task.WaitAsync(Timeout), V2ClientSubscriptionRegistry.NotReadyCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExactlyPendingCapacityDrainsInOrderAndKeepsEpochValid()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = new List<string>();
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value => calls.Add(value.Data.Value.Log));
        var request = await fixture.Transport.NextAsync();
        for (var index = 0; index < V2ClientSubscriptionRegistry.PendingEventCapacity; index++)
            fixture.Coordinator.Core.RouteText(LogEvent(index + 1, InstanceId, $"line-{index}"));
        Assert.Empty(fixture.Invalidations);

        fixture.Coordinator.Core.RouteText(Success(request));
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        Assert.Equal(V2ClientSubscriptionRegistry.PendingEventCapacity, calls.Count);
        Assert.Equal("line-0", calls[0]);
        Assert.Equal($"line-{V2ClientSubscriptionRegistry.PendingEventCapacity - 1}", calls[^1]);
        Assert.Equal(
            Enumerable.Range(0, V2ClientSubscriptionRegistry.PendingEventCapacity).Select(index => $"line-{index}"),
            calls);
        fixture.Coordinator.Core.RouteText(LogEvent(300, InstanceId, "live"));
        Assert.Equal("live", calls[^1]);
        Assert.Empty(fixture.Invalidations);
        await DisposeHandleAsync(fixture, handle);
    }

    private async Task<Fixture> ReadyFixtureAsync(
        Action<V2ClientDiagnostic>? diagnostic = null,
        RemoteInstanceCatalogMirror? mirror = null,
        V2ClientSubscriptionRegistry? registry = null,
        bool bind = true,
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
    {
        var invalidations = new List<Invalidation>();
        registry ??= Registry(invalidations, diagnostic);
        mirror ??= new RemoteInstanceCatalogMirror();
        var transport = new ScriptedTransport();
        var next = 0;
        var coordinator = new V2ClientConnectionCoordinator(
            transport, mirror, timeProvider ?? TimeProvider.System, requestTimeout ?? Timeout,
            () => JsonRpcRequestId.FromString($"request-{Interlocked.Increment(ref next)}"),
            diagnostic, registry.Route);
        _coordinators.Add(coordinator);
        var start = coordinator.StartAsync();
        var catalogSubscribe = await transport.NextAsync();
        coordinator.Core.RouteText(Success(catalogSubscribe));
        var catalogRead = await transport.NextAsync();
        coordinator.Core.RouteText(Success(catalogRead, "{\"version\":0,\"items\":[]}"));
        Assert.True((await start.WaitAsync(Timeout)).IsOk(out _));
        if (bind)
            Assert.True((await registry.BindReadyEpochAsync(coordinator).WaitAsync(Timeout)).IsOk(out _));
        return new Fixture(registry, coordinator, transport, invalidations);
    }

    private V2ClientSubscriptionRegistry Registry(List<Invalidation> invalidations, Action<V2ClientDiagnostic>? diagnostic = null)
    {
        var registry = new V2ClientSubscriptionRegistry(
            (epoch, error) => invalidations.Add(new(epoch, error)), diagnostic);
        _registries.Add(registry);
        return registry;
    }

    private static async Task<IAsyncDisposable> SubscribeAsync(Fixture fixture, Action<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>> callback)
    {
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog, DaemonEventFilter<InstanceLogEventMeta>.Wildcard, callback);
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(request));
        return (await task.WaitAsync(Timeout)).Unwrap();
    }

    private static async Task DisposeHandleAsync(Fixture fixture, IAsyncDisposable handle)
    {
        var task = handle.DisposeAsync().AsTask();
        var request = await fixture.Transport.NextAsync();
        Assert.Equal("mcsl.event.unsubscribe", request.Method);
        fixture.Coordinator.Core.RouteText(Success(request));
        await task.WaitAsync(Timeout);
    }

    private static async Task RespondNextSuccessAsync(Fixture fixture, string expectedMethod)
    {
        var request = await fixture.Transport.NextAsync();
        Assert.Equal(expectedMethod, request.Method);
        fixture.Coordinator.Core.RouteText(Success(request));
    }

    private static void AssertError(RustyOptions.Result<IAsyncDisposable, DaemonError> result, string code)
    {
        Assert.True(result.IsErr(out var error));
        Assert.Equal(code, error!.Code);
    }

    private static JsonElement Params(SentRequest request)
    {
        using var document = JsonDocument.Parse(request.Json);
        return document.RootElement.GetProperty("params").Clone();
    }

    private static byte[] Success(SentRequest request, string result = "{}") =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}");
    private static byte[] Error(SentRequest request, string code, string kind) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"error\":{{\"code\":-32000,\"message\":\"failed\",\"data\":{{\"daemon_error_code\":\"{code}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"test\"}}}}}}");
    private static byte[] LogEvent(long sequence, Guid instanceId, string log) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.instance.log\",\"params\":{{\"sequence\":{sequence},\"timestamp\":{sequence},\"meta\":{{\"instance_id\":\"{instanceId:D}\"}},\"data\":{{\"log\":{JsonSerializer.Serialize(log)}}}}}}}");
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed record Invalidation(V2ClientConnectionCoordinator Epoch, DaemonError Error);
    private sealed record Fixture(V2ClientSubscriptionRegistry Registry, V2ClientConnectionCoordinator Coordinator, ScriptedTransport Transport, List<Invalidation> Invalidations);
    private sealed record SentRequest(string Method, string IdJson, string Json);

    private sealed class CallbackOwner
    {
        internal int Count { get; private set; }
        internal void Accept(DaemonEvent<InstanceLogEventData, InstanceLogEventMeta> value) => Count++;
    }

    private sealed class ScriptedTransport : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);
        private int _sendCount;
        internal int SendCount => Volatile.Read(ref _sendCount);
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetString(utf8Json.AsSpan());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            _requests.Enqueue(new(root.GetProperty("method").GetString()!, root.GetProperty("id").GetRawText(), json));
            Interlocked.Increment(ref _sendCount);
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
}
