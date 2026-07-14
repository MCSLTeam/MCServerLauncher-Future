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
    private const string SystemInfoJson = "{\"os\":{\"name\":\"Windows\",\"architecture\":\"x64\"},\"cpu\":{\"vendor\":\"vendor\",\"name\":\"cpu\",\"count\":16,\"usage\":5.5,\"core_count\":8,\"thread_count\":16},\"mem\":{\"total_kilobytes\":32768,\"free_kilobytes\":16384},\"drive\":{\"drive_format\":\"NTFS\",\"total_bytes\":1024,\"free_bytes\":512,\"name\":\"C\"},\"drives\":[],\"daemon_version\":\"2.0.0\"}";
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
            _ => Task.CompletedTask);
        var subscribe = await fixture.Transport.NextAsync();
        Assert.Equal("mcsl.event.subscribe", subscribe.Method);
        Assert.Equal(InstanceId.ToString("D"), Params(subscribe).GetProperty("meta").GetProperty("instance_id").GetString());
        fixture.Coordinator.Core.RouteText(Success(subscribe));
        var first = (await firstTask.WaitAsync(Timeout)).Unwrap();

        var second = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)),
            _ => Task.CompletedTask).WaitAsync(Timeout)).Unwrap();
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
        var allCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wildcardTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value => AddAsync(calls, "wildcard:" + value.Data.Value.Log));
        var wildcardRequest = await fixture.Transport.NextAsync();
        Assert.False(Params(wildcardRequest).TryGetProperty("meta", out _));
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "buffered"));
        Assert.Empty(calls);
        fixture.Coordinator.Core.RouteText(Success(wildcardRequest));
        var wildcard = (await wildcardTask.WaitAsync(Timeout)).Unwrap();

        var exactTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Exact(new(InstanceId)),
            value =>
            {
                calls.Add("exact:" + value.Data.Value.Log);
                allCalls.TrySetResult();
                return Task.CompletedTask;
            });
        var exactRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(exactRequest));
        var exact = (await exactTask).Unwrap();
        fixture.Coordinator.Core.RouteText(LogEvent(2, Guid.NewGuid(), "other"));
        fixture.Coordinator.Core.RouteText(LogEvent(3, InstanceId, "matching"));
        await allCalls.Task.WaitAsync(Timeout);

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
    public async Task PendingRouteBlocksLaterActiveRouteUntilAckThenGlobalFifoDeliversBoth()
    {
        var scheduler = new ControlledDeliveryScheduler();
        var fixture = await ReadyFixtureAsync(queueDeliveryDrain: scheduler.Queue);
        var calls = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ =>
            {
                calls.Add("report");
                if (calls.Count == 2)
                    completed.TrySetResult();
                return Task.CompletedTask;
            });
        var reportRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(reportRequest));
        var report = (await reportTask.WaitAsync(Timeout)).Unwrap();

        var logTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ =>
            {
                calls.Add("log");
                if (calls.Count == 2)
                    completed.TrySetResult();
                return Task.CompletedTask;
            });
        var logRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "pending"));
        Assert.Equal(0, scheduler.QueueCount);
        fixture.Coordinator.Core.RouteText(ReportEvent(2));

        Assert.Equal(0, scheduler.QueueCount);
        Assert.Empty(calls);
        fixture.Coordinator.Core.RouteText(Success(logRequest));
        var log = (await logTask.WaitAsync(Timeout)).Unwrap();
        var drain = await scheduler.NextAsync();
        drain();
        await completed.Task.WaitAsync(Timeout);

        Assert.Equal(["log", "report"], calls);
        await DisposeHandleAsync(fixture, report);
        await DisposeHandleAsync(fixture, log);
    }

    [Fact]
    public async Task PendingSubscribeFailureReleasesLaterActiveRouteWithoutInvalidatingEpoch()
    {
        var scheduler = new ControlledDeliveryScheduler();
        var fixture = await ReadyFixtureAsync(queueDeliveryDrain: scheduler.Queue);
        var reportDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var reportTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ =>
            {
                calls.Add("report");
                reportDelivered.TrySetResult();
                return Task.CompletedTask;
            });
        var reportRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(reportRequest));
        var report = (await reportTask.WaitAsync(Timeout)).Unwrap();

        var logTask = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => AddAsync(calls, "log"));
        var logRequest = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "pending"));
        Assert.Equal(0, scheduler.QueueCount);
        fixture.Coordinator.Core.RouteText(ReportEvent(2));
        Assert.Equal(0, scheduler.QueueCount);

        fixture.Coordinator.Core.RouteText(Error(logRequest, "permission.denied", "permission"));
        Assert.IsType<PermissionDaemonError>((await logTask.WaitAsync(Timeout)).UnwrapErr());
        var drain = await scheduler.NextAsync();
        drain();
        await reportDelivered.Task.WaitAsync(Timeout);

        Assert.Equal(["report"], calls);
        Assert.Empty(fixture.Invalidations);
        await DisposeHandleAsync(fixture, report);
    }

    [Fact]
    public async Task EventsArrivingDuringAckDrainRemainOrderedBeforeActivation()
    {
        var fixture = await ReadyFixtureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            async value =>
            {
                calls.Add(value.Data.Value.Log);
                if (value.Data.Value.Log == "first")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(Timeout);
                }
                else
                {
                    completed.TrySetResult();
                }
            });
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "first"));
        fixture.Coordinator.Core.RouteText(Success(request));
        await entered.Task.WaitAsync(Timeout);

        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "second"));
        Assert.Equal(["first"], calls);
        release.TrySetResult();
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        await completed.Task.WaitAsync(Timeout);
        Assert.Equal(["first", "second"], calls);
        await DisposeHandleAsync(fixture, handle);
    }

    [Fact]
    public async Task DetachDuringAckDrainStopsLaterOldEpochDeliveryWithoutDeadlock()
    {
        var fixture = await ReadyFixtureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            async value =>
            {
                calls.Add(value.Data.Value.Log);
                entered.TrySetResult();
                await release.Task.WaitAsync(Timeout);
            });
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "first"));
        fixture.Coordinator.Core.RouteText(Success(request));
        await entered.Task.WaitAsync(Timeout);

        fixture.Registry.DetachEpoch(fixture.Coordinator);
        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "stale"));
        release.TrySetResult();
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        Assert.Equal(["first"], calls);
        Assert.Empty(fixture.Invalidations);
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task ReplayCallbackMayAwaitDisposeItsHandleWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        IAsyncDisposable? handle = null;
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle = await SubscribeAsync(first, async _ =>
        {
            await handle!.DisposeAsync();
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
    public async Task ReplayCallbackMayAwaitSubscribeWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        IAsyncDisposable? reportHandle = null;
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logHandle = await SubscribeAsync(first, async _ =>
        {
            reportHandle = (await first.Registry.SubscribeAsync(
                V2ClientProtocol.DaemonReport,
                DaemonEventFilter<EmptyRequest>.Wildcard,
                _ => Task.CompletedTask)).Unwrap();
            callbackCompleted.TrySetResult();
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
        await callbackCompleted.Task.WaitAsync(Timeout);
        Assert.NotNull(reportHandle);
        await DisposeHandleAsync(replacement, logHandle);
        await DisposeHandleAsync(replacement, reportHandle!);
    }

    [Fact]
    public async Task ReplayCallbackMayAwaitDisposeRegistryWithoutMutationLaneDeadlock()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = await SubscribeAsync(first, async _ =>
        {
            await first.Registry.DisposeAsync();
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
            _ => Task.CompletedTask);
        AssertError(noReady, V2ClientSubscriptionRegistry.NotReadyCode);

        var fixture = await ReadyFixtureAsync();
        var catalog = await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceCatalogChanged,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => Task.CompletedTask);
        AssertError(catalog, V2ClientSubscriptionRegistry.UnsupportedEventCode);
        var invalidFilter = await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.ExplicitNull,
            _ => Task.CompletedTask);
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
            _ => Task.CompletedTask,
            cancellation.Token);
        await expected.Transport.NextAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(Timeout));
        var invalidation = Assert.Single(expected.Invalidations);
        Assert.Same(expected.Coordinator, invalidation.Epoch);
        Assert.Equal("client.subscription_canceled", invalidation.Error.Code);
    }

    [Fact]
    public async Task InvalidationOwnerHookMayStartRegistryDisposeWithoutLaneDeadlock()
    {
        V2ClientSubscriptionRegistry? registry = null;
        Task? dispose = null;
        var hookInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry = new V2ClientSubscriptionRegistry((_, _) =>
        {
            dispose = registry!.DisposeAsync().AsTask();
            hookInvoked.TrySetResult();
        });
        _registries.Add(registry);
        var fixture = await ReadyFixtureAsync(registry: registry);
        using var cancellation = new CancellationTokenSource();
        var subscription = registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => Task.CompletedTask,
            cancellation.Token);
        await fixture.Transport.NextAsync();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.WaitAsync(Timeout));
        await hookInvoked.Task.WaitAsync(Timeout);
        await dispose!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task DetachRacingPendingSubscribeDropsBufferAndCompletesWithoutInvalidation()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = 0;
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => IncrementAsync(() => calls++));
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
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await SubscribeAsync(fixture, _ =>
        {
            calls.Add("first");
            throw new InvalidOperationException("secret");
        });
        var second = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ =>
            {
                calls.Add("second");
                completed.TrySetResult();
                return Task.CompletedTask;
            })).Unwrap();

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "line"));
        await completed.Task.WaitAsync(Timeout);
        Assert.Equal(["first", "second"], calls);
        var diagnostic = Assert.Single(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ConsumerFault);
        Assert.DoesNotContain("secret", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [Fact]
    public async Task RouteReturnsBeforeAwaitedCallbackCompletesAndTwoEventsRemainFifo()
    {
        var fixture = await ReadyFixtureAsync();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var handle = await SubscribeAsync(fixture, async value =>
        {
            calls.Add(value.Data.Value.Log);
            if (value.Data.Value.Log == "first")
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            else
            {
                secondCompleted.TrySetResult();
            }
        });

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "first"));
        await firstEntered.Task.WaitAsync(Timeout);
        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "second"));

        Assert.False(secondCompleted.Task.IsCompleted);
        Assert.Equal(["first"], calls);
        releaseFirst.TrySetResult();
        await secondCompleted.Task.WaitAsync(Timeout);
        Assert.Equal(["first", "second"], calls);
        await DisposeHandleAsync(fixture, handle);
    }

    [Fact]
    public async Task CallbackFailuresAreDiagnosedOnceEachAndDoNotStopLaterHandles()
    {
        var diagnostics = new ConcurrentQueue<V2ClientDiagnostic>();
        var fixture = await ReadyFixtureAsync(diagnostics.Enqueue);
        var synchronous = await SubscribeAsync(fixture, _ => throw new InvalidOperationException("sync"));
        var asynchronous = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => Task.FromException(new InvalidOperationException("async")))).Unwrap();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellation = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => Task.FromCanceled(canceled.Token))).Unwrap();
        var continued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var final = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ =>
            {
                continued.TrySetResult();
                return Task.CompletedTask;
            })).Unwrap();

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "line"));
        await continued.Task.WaitAsync(Timeout);

        Assert.Equal(3, diagnostics.Count(item => item.Kind == V2ClientDiagnosticKind.ConsumerFault));
        await synchronous.DisposeAsync();
        await asynchronous.DisposeAsync();
        await cancellation.DisposeAsync();
        await DisposeHandleAsync(fixture, final);
    }

    [Fact]
    public async Task DisposeSkipsQueuedHandleButAllowsStartedHandleToFinish()
    {
        var fixture = await ReadyFixtureAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queuedCalls = 0;
        var first = await SubscribeAsync(fixture, async _ =>
        {
            started.TrySetResult();
            await release.Task;
            finished.TrySetResult();
        });
        var queued = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ => IncrementAsync(() => queuedCalls++))).Unwrap();
        var survivorCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var survivor = (await fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            _ =>
            {
                survivorCalled.TrySetResult();
                return Task.CompletedTask;
            })).Unwrap();

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "line"));
        await started.Task.WaitAsync(Timeout);
        await first.DisposeAsync();
        await queued.DisposeAsync();
        release.TrySetResult();

        await finished.Task.WaitAsync(Timeout);
        await survivorCalled.Task.WaitAsync(Timeout);
        Assert.Equal(0, queuedCalls);
        await DisposeHandleAsync(fixture, survivor);
    }

    [Fact]
    public async Task DetachSkipsQueuedOldEpochDeliveryAndNewEpochStillDelivers()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var first = await ReadyFixtureAsync(mirror: mirror);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var handle = await SubscribeAsync(first, async value =>
        {
            calls.Add(value.Data.Value.Log);
            if (value.Data.Value.Log == "old-started")
            {
                entered.TrySetResult();
                await release.Task;
            }
            else if (value.Data.Value.Log == "new")
            {
                newDelivered.TrySetResult();
            }
        });

        first.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "old-started"));
        await entered.Task.WaitAsync(Timeout);
        first.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "old-queued"));
        first.Registry.DetachEpoch(first.Coordinator);

        var replacement = await ReadyFixtureAsync(mirror: mirror, registry: first.Registry, bind: false);
        var bind = first.Registry.BindReadyEpochAsync(replacement.Coordinator);
        await RespondNextSuccessAsync(replacement, "mcsl.event.subscribe");
        Assert.True((await bind.WaitAsync(Timeout)).IsOk(out _));
        release.TrySetResult();
        replacement.Coordinator.Core.RouteText(LogEvent(3, InstanceId, "new"));
        await newDelivered.Task.WaitAsync(Timeout);

        Assert.Equal(["old-started", "new"], calls);
        await DisposeHandleAsync(replacement, handle);
    }

    [Fact]
    public async Task ActiveDeliveryQueueExceedingPendingCapacityDoesNotInvalidateEpoch()
    {
        var fixture = await ReadyFixtureAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var expected = V2ClientSubscriptionRegistry.PendingEventCapacity + 1;
        var handle = await SubscribeAsync(fixture, async _ =>
        {
            var current = Interlocked.Increment(ref calls);
            if (current == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            if (current == expected)
                completed.TrySetResult();
        });

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "line-0"));
        await entered.Task.WaitAsync(Timeout);
        for (var index = 1; index < expected; index++)
            fixture.Coordinator.Core.RouteText(LogEvent(index + 1, InstanceId, $"line-{index}"));
        Assert.Empty(fixture.Invalidations);

        release.TrySetResult();
        await completed.Task.WaitAsync(Timeout);
        Assert.Equal(expected, calls);
        Assert.Empty(fixture.Invalidations);
        await DisposeHandleAsync(fixture, handle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeliverySchedulerFailureDropsCapturedHandlersAndLaterRouteRecovers(bool throws)
    {
        var attempts = 0;
        bool Scheduler(Action drain)
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                if (throws)
                    throw new InvalidOperationException("schedule");
                return false;
            }

            return ThreadPool.QueueUserWorkItem(static callback => callback(), drain, preferLocal: false);
        }

        var fixture = await ReadyFixtureAsync(queueDeliveryDrain: Scheduler);
        var calls = new List<string>();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = await SubscribeAsync(fixture, value =>
        {
            calls.Add(value.Data.Value.Log);
            recovered.TrySetResult();
            return Task.CompletedTask;
        });

        fixture.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "dropped"));
        fixture.Coordinator.Core.RouteText(LogEvent(2, InstanceId, "recovered"));
        await recovered.Task.WaitAsync(Timeout);

        Assert.Equal(["recovered"], calls);
        await DisposeHandleAsync(fixture, handle);
    }

    [Fact]
    public async Task LastDisposeRemovesLocallyThenUnsubscribeErrorInvalidates()
    {
        var fixture = await ReadyFixtureAsync();
        var calls = 0;
        var handle = await SubscribeAsync(fixture, _ => IncrementAsync(() => calls++));
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
            _ => Task.CompletedTask);
        AssertError(rejected, V2ClientSubscriptionRegistry.NotReadyCode);
    }

    [Fact]
    public async Task LocalUnsubscribeTimeoutInvalidatesEpochWithoutCallerCancellation()
    {
        var time = new ManualTimeProvider();
        var fixture = await ReadyFixtureAsync(timeProvider: time, requestTimeout: TimeSpan.FromSeconds(1));
        var handle = await SubscribeAsync(fixture, _ => Task.CompletedTask);
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
        var handle = await SubscribeAsync(first, _ => Task.CompletedTask);
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
            _ => Task.CompletedTask,
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
        var log = await SubscribeAsync(first, _ => Task.CompletedTask);
        var notificationTask = first.Registry.SubscribeAsync(
            V2ClientProtocol.Notification,
            DaemonEventFilter<NotificationEventMeta>.Wildcard,
            _ => Task.CompletedTask);
        var notificationRequest = await first.Transport.NextAsync();
        first.Coordinator.Core.RouteText(Success(notificationRequest));
        var notification = (await notificationTask).Unwrap();
        var reportSubscription = first.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => Task.CompletedTask);
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
        var ackRaceDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = await SubscribeAsync(first, value =>
        {
            calls.Add(value.Data.Value.Log);
            if (value.Data.Value.Log == "ack-race")
                ackRaceDelivered.TrySetResult();
            else if (value.Data.Value.Log == "current")
                currentDelivered.TrySetResult();
            return Task.CompletedTask;
        });
        var reportTask = first.Registry.SubscribeAsync(
            V2ClientProtocol.DaemonReport,
            DaemonEventFilter<EmptyRequest>.Wildcard,
            _ => Task.CompletedTask);
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
        await ackRaceDelivered.Task.WaitAsync(Timeout);
        Assert.Equal(["ack-race"], calls);
        first.Coordinator.Core.RouteText(LogEvent(1, InstanceId, "stale"));
        current.Coordinator.Core.RouteText(LogEvent(3, InstanceId, "current"));
        await currentDelivered.Task.WaitAsync(Timeout);
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
            _ => IncrementAsync(() => calls++));
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
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liveDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            value =>
            {
                calls.Add(value.Data.Value.Log);
                if (calls.Count == V2ClientSubscriptionRegistry.PendingEventCapacity)
                    drained.TrySetResult();
                if (value.Data.Value.Log == "live")
                    liveDelivered.TrySetResult();
                return Task.CompletedTask;
            });
        var request = await fixture.Transport.NextAsync();
        for (var index = 0; index < V2ClientSubscriptionRegistry.PendingEventCapacity; index++)
            fixture.Coordinator.Core.RouteText(LogEvent(index + 1, InstanceId, $"line-{index}"));
        Assert.Empty(fixture.Invalidations);

        fixture.Coordinator.Core.RouteText(Success(request));
        var handle = (await task.WaitAsync(Timeout)).Unwrap();
        await drained.Task.WaitAsync(Timeout);
        Assert.Equal(V2ClientSubscriptionRegistry.PendingEventCapacity, calls.Count);
        Assert.Equal("line-0", calls[0]);
        Assert.Equal($"line-{V2ClientSubscriptionRegistry.PendingEventCapacity - 1}", calls[^1]);
        Assert.Equal(
            Enumerable.Range(0, V2ClientSubscriptionRegistry.PendingEventCapacity).Select(index => $"line-{index}"),
            calls);
        fixture.Coordinator.Core.RouteText(LogEvent(300, InstanceId, "live"));
        await liveDelivered.Task.WaitAsync(Timeout);
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
        TimeSpan? requestTimeout = null,
        Func<Action, bool>? queueDeliveryDrain = null)
    {
        var invalidations = new List<Invalidation>();
        registry ??= Registry(invalidations, diagnostic, queueDeliveryDrain);
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

    private V2ClientSubscriptionRegistry Registry(
        List<Invalidation> invalidations,
        Action<V2ClientDiagnostic>? diagnostic = null,
        Func<Action, bool>? queueDeliveryDrain = null)
    {
        var registry = new V2ClientSubscriptionRegistry(
            (epoch, error) => invalidations.Add(new(epoch, error)),
            diagnostic,
            queueDeliveryDrain);
        _registries.Add(registry);
        return registry;
    }

    private static async Task<IAsyncDisposable> SubscribeAsync(
        Fixture fixture,
        Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task> callback)
    {
        var task = fixture.Registry.SubscribeAsync(
            V2ClientProtocol.InstanceLog,
            DaemonEventFilter<InstanceLogEventMeta>.Wildcard,
            callback);
        var request = await fixture.Transport.NextAsync();
        fixture.Coordinator.Core.RouteText(Success(request));
        return (await task.WaitAsync(Timeout)).Unwrap();
    }

    private static Task AddAsync(List<string> values, string value)
    {
        values.Add(value);
        return Task.CompletedTask;
    }

    private static Task IncrementAsync(Action increment)
    {
        increment();
        return Task.CompletedTask;
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
    private static byte[] ReportEvent(long sequence) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{{\"sequence\":{sequence},\"timestamp\":{sequence},\"data\":{{\"system_info\":{SystemInfoJson},\"start_timestamp\":1}}}}}}");
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed record Invalidation(V2ClientConnectionCoordinator Epoch, DaemonError Error);
    private sealed record Fixture(V2ClientSubscriptionRegistry Registry, V2ClientConnectionCoordinator Coordinator, ScriptedTransport Transport, List<Invalidation> Invalidations);
    private sealed record SentRequest(string Method, string IdJson, string Json);

    private sealed class CallbackOwner
    {
        internal int Count { get; private set; }
        internal Task Accept(DaemonEvent<InstanceLogEventData, InstanceLogEventMeta> value)
        {
            Count++;
            return Task.CompletedTask;
        }
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

    private sealed class ControlledDeliveryScheduler
    {
        private readonly ConcurrentQueue<Action> _queued = new();
        private readonly SemaphoreSlim _available = new(0);

        internal int QueueCount => _queued.Count;

        internal bool Queue(Action drain)
        {
            _queued.Enqueue(drain);
            _available.Release();
            return true;
        }

        internal async Task<Action> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_queued.TryDequeue(out var drain));
            return drain!;
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
