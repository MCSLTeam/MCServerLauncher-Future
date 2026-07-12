using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.ProtocolTests.Rpc.Events;

public sealed class V2RemoteEventBridgeTests
{
    [Fact]
    public async Task ProjectsOnlyFourBuiltInEventsWithCatalogShapeAndBridgeClock()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var sender = new RecordingSender();
        await using var owner = Attach(registry, sender, "projection", out var entry);
        SubscribeAll(entry);
        _ = owner.Start();
        using var bridge = new V2RemoteEventBridge(
            host.Port,
            catalog,
            registry,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_783_677_000_000)));

        var instanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ruleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var snapshot = new InstanceCatalogItem(instanceId, "test", InstanceType.MCJava, "1.21.8", InstanceStatus.Running);
        await host.Port.PublishAsync(new InstanceCatalogChangedDomainEvent(
            new InstanceCatalogChangedEventData(7, InstanceCatalogChangeOperation.Upsert, instanceId, snapshot)));
        await host.Port.PublishAsync(new DaemonReportDomainEvent(CreateSystemInfo(), 123));
        await host.Port.PublishAsync(new InstanceLogDomainEvent(instanceId, "ready"));
        await host.Port.PublishAsync(new ClientNotificationDomainEvent(
            "title", "message", "info", instanceId, ruleId, 1));
        await host.Port.PublishAsync(new InstanceStatusChangedDomainEvent(instanceId, InstanceStatus.Stopped));

        var frames = await sender.TakeAsync(4);
        var notifications = frames.Select(Parse).ToArray();
        Assert.Equal(
            new[]
            {
                "mcsl.event.instance.catalog.changed",
                "mcsl.event.daemon.report",
                "mcsl.event.instance.log",
                "mcsl.event.notification"
            },
            notifications.Select(static item => item.Method));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, notifications.Select(static item => item.Params.Sequence));
        Assert.All(notifications, item => Assert.Equal(1_783_677_000_000, item.Params.Timestamp));
        Assert.Equal(JsonRpcOptionalPayloadKind.Missing, notifications[0].Params.Meta.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.Missing, notifications[1].Params.Meta.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.Value, notifications[2].Params.Meta.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.Value, notifications[3].Params.Meta.Kind);
        Assert.All(notifications, item => Assert.Equal(JsonRpcOptionalPayloadKind.Value, item.Params.Data.Kind));
        Assert.Contains("\"instance_id\":\"11111111-1111-1111-1111-111111111111\"", Utf8(frames[2]));
        Assert.Contains("\"rule_id\":\"22222222-2222-2222-2222-222222222222\"", Utf8(frames[3]));
        using var notificationJson = JsonDocument.Parse(
            ImmutableCollectionsMarshal.AsArray(frames[3].Payload)!);
        var notificationData = notificationJson.RootElement.GetProperty("params").GetProperty("data");
        Assert.False(notificationData.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task FilteringReusesOneImmutablePayloadForExactAndWildcardOwners()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var exactSender = new RecordingSender();
        var wildcardSender = new RecordingSender();
        var nonmatchSender = new RecordingSender();
        await using var exactOwner = Attach(registry, exactSender, "exact", out var exact);
        await using var wildcardOwner = Attach(registry, wildcardSender, "wildcard", out var wildcard);
        await using var nonmatchOwner = Attach(registry, nonmatchSender, "nonmatch", out var nonmatch);
        var target = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SubscribeExact(exact, target);
        Subscribe(wildcard, "mcsl.event.instance.log");
        SubscribeExact(nonmatch, Guid.Parse("44444444-4444-4444-4444-444444444444"));
        _ = exactOwner.Start();
        _ = wildcardOwner.Start();
        _ = nonmatchOwner.Start();
        using var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, TimeProvider.System);

        await host.Port.PublishAsync(new InstanceLogDomainEvent(target, "same payload"));

        var exactFrame = Assert.Single(await exactSender.TakeAsync(1));
        var wildcardFrame = Assert.Single(await wildcardSender.TakeAsync(1));
        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(exactFrame.Payload),
            ImmutableCollectionsMarshal.AsArray(wildcardFrame.Payload));
        Assert.Empty(nonmatchSender.Frames);
    }

    [Fact]
    public async Task ConcurrentMixedPublishHasOneGlobalSequenceAndSamePerConnectionOrder()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var firstSender = new RecordingSender();
        var secondSender = new RecordingSender();
        await using var firstOwner = Attach(registry, firstSender, "first", out var first);
        await using var secondOwner = Attach(registry, secondSender, "second", out var second);
        SubscribeAll(first);
        SubscribeAll(second);
        _ = firstOwner.Start();
        _ = secondOwner.Start();
        using var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, TimeProvider.System);
        using var barrier = new Barrier(3);
        var instanceId = Guid.NewGuid();

        var logs = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            for (var index = 0; index < 32; index++)
                await host.Port.PublishAsync(new InstanceLogDomainEvent(instanceId, $"log-{index}"));
        });
        var reports = Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            for (var index = 0; index < 32; index++)
                await host.Port.PublishAsync(new DaemonReportDomainEvent(CreateSystemInfo(), index));
        });
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
        await Task.WhenAll(logs, reports).WaitAsync(TimeSpan.FromSeconds(20));

        var firstSequence = (await firstSender.TakeAsync(64)).Select(Parse).Select(static item => item.Params.Sequence).ToArray();
        var secondSequence = (await secondSender.TakeAsync(64)).Select(Parse).Select(static item => item.Params.Sequence).ToArray();
        Assert.Equal(Enumerable.Range(1, 64).Select(static value => (long)value), firstSequence);
        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public async Task DisposeIsLinearizedAndLeavesNoDomainEventSubscriptions()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var sender = new RecordingSender();
        await using var owner = Attach(registry, sender, "dispose", out var entry);
        Subscribe(entry, "mcsl.event.instance.log");
        _ = owner.Start();
        var clock = new BlockingTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_783_677_000_000));
        var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, clock);
        Assert.Equal(4, host.Port.ActiveSubscriptionCount);
        var publish = Task.Run(async () =>
            await host.Port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "race")));
        await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispose = Task.Factory.StartNew(
            () =>
            {
                disposeStarted.TrySetResult();
                bridge.Dispose();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await dispose.WaitAsync(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            clock.Release();
        }

        await Task.WhenAll(publish, dispose).WaitAsync(TimeSpan.FromSeconds(20));
        await sender.TakeAsync(1);
        Assert.Equal(0, host.Port.ActiveSubscriptionCount);
        var deliveredAtDispose = sender.Frames.Count;
        await host.Port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "after"));
        Assert.Equal(deliveredAtDispose, sender.Frames.Count);
    }

    [Fact]
    public async Task HungSenderDoesNotBlockDomainEventPublish()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var sender = new BlockingSender();
        await using var owner = new V2ConnectionOwner(sender, ["*"]);
        Assert.Equal(V2EventConnectionAttachResult.Attached, registry.TryAttach("hung", owner, out var entry));
        Subscribe(entry!, "mcsl.event.instance.log");
        _ = owner.Start();
        using var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, TimeProvider.System);

        await host.Port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "nonblocking"))
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await sender.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sender.Release();
    }

    [Fact]
    public async Task SlowConsumerCloseCleansRegistryAndDoesNotBlockOtherConnection()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        var slowSender = new RecordingSender();
        var healthySender = new RecordingSender();
        await using var slowOwner = Attach(registry, slowSender, "slow", out var slow);
        await using var healthyOwner = Attach(registry, healthySender, "healthy", out var healthy);
        Subscribe(slow, "mcsl.event.instance.log");
        Subscribe(healthy, "mcsl.event.instance.log");
        _ = healthyOwner.Start();
        using var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, TimeProvider.System);

        for (var index = 0; index <= V2ConnectionOwner.OutboundCapacity; index++)
            await host.Port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), $"line-{index}"));

        Assert.Equal(
            V2ConnectionCloseReason.SlowConsumer,
            await slowSender.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(registry.TryGet("slow", out _));
        Assert.Equal(0, slow.Ledger.Count);
        Assert.Equal(1, slow.CleanupCount);
        Assert.True(registry.TryGet("healthy", out _));
        Assert.Equal(V2ConnectionOwner.OutboundCapacity + 1, (await healthySender.TakeAsync(257)).Length);
    }

    [Fact]
    public async Task PublicationWithoutMatchesStillAdvancesGlobalSequence()
    {
        using var host = DomainEventPortTestHost.Create();
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        using var bridge = new V2RemoteEventBridge(host.Port, catalog, registry, TimeProvider.System);

        await host.Port.PublishAsync(new DaemonReportDomainEvent(CreateSystemInfo(), 1));

        var sender = new RecordingSender();
        await using var owner = Attach(registry, sender, "late", out var entry);
        Subscribe(entry, "mcsl.event.daemon.report");
        _ = owner.Start();
        await host.Port.PublishAsync(new DaemonReportDomainEvent(CreateSystemInfo(), 2));

        Assert.Equal(2, Parse(Assert.Single(await sender.TakeAsync(1))).Params.Sequence);
    }

    [Fact]
    public void ConstructorSubscriptionFailureRollsBackOwnerAndPartialSubscriptions()
    {
        var port = new ThrowingSubscribeDomainEventPort(throwOnSubscription: 3);
        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);

        Assert.Throws<InvalidOperationException>(() =>
            new V2RemoteEventBridge(port, catalog, registry, TimeProvider.System));

        Assert.Equal(3, port.SubscribeAttempts);
        Assert.Equal(1, port.DisposeOwnerCalls);
        Assert.Equal(0, port.ActiveSubscriptions);
        Assert.True(port.Owner!.IsDisposed);
    }

    [Fact]
    public void SequenceAndTimestampNeverProduceNegativeWireValues()
    {
        Assert.Equal(long.MaxValue, V2RemoteEventBridge.NextSequence(long.MaxValue - 1));
        Assert.Throws<OverflowException>(() => V2RemoteEventBridge.NextSequence(long.MaxValue));

        Assert.Throws<InvalidOperationException>(() => V2RemoteEventBridge.GetTimestamp(
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(-1))));
    }

    private static FrozenProtocolCatalog CreateCatalog()
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("bridge", "1.0.0"));
        Register<InstanceCatalogChangedEventData>(builder, "mcsl.event.instance.catalog.changed");
        Register<DaemonReportEventData>(builder, "mcsl.event.daemon.report");
        Register<InstanceLogEventData, InstanceLogEventMeta>(builder, "mcsl.event.instance.log");
        Register<NotificationEventData, NotificationEventMeta>(builder, "mcsl.event.notification");
        return builder.Freeze();
    }

    private static void Register<TData>(ProtocolCatalogBuilder builder, string name)
    {
        var descriptor = BuiltInProtocolDefinitions.Events.Single(item => item.Name.Value == name);
        builder.RegisterBuiltInEvent(descriptor, new EventBinding<TData>(ProtocolExecutionOwner.BuiltIn));
    }

    private static void Register<TData, TMeta>(ProtocolCatalogBuilder builder, string name)
    {
        var descriptor = BuiltInProtocolDefinitions.Events.Single(item => item.Name.Value == name);
        builder.RegisterBuiltInEvent(descriptor, new EventBinding<TData, TMeta>(ProtocolExecutionOwner.BuiltIn));
    }

    private static V2ConnectionOwner Attach(
        V2EventConnectionRegistry registry,
        RecordingSender sender,
        string id,
        out V2EventConnectionRegistry.V2EventConnectionEntry entry)
    {
        var owner = new V2ConnectionOwner(sender, ["*"]);
        Assert.Equal(V2EventConnectionAttachResult.Attached, registry.TryAttach(id, owner, out var attached));
        entry = attached!;
        return owner;
    }

    private static void SubscribeAll(V2EventConnectionRegistry.V2EventConnectionEntry entry)
    {
        foreach (var descriptor in BuiltInProtocolDefinitions.Events)
            Subscribe(entry, descriptor.Name.Value);
    }

    private static void Subscribe(V2EventConnectionRegistry.V2EventConnectionEntry entry, string name) =>
        Assert.True(entry.Ledger.Subscribe(new EventSubscriptionRequest(name)).IsOk(out _));

    private static void SubscribeExact(V2EventConnectionRegistry.V2EventConnectionEntry entry, Guid instanceId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new InstanceLogEventMeta(instanceId),
            BuiltInProtocolJsonContext.Default.InstanceLogEventMeta);
        Assert.True(entry.Ledger.Subscribe(new EventSubscriptionRequest(
            "mcsl.event.instance.log",
            EventMetaFilter.FromObject(bytes))).IsOk(out _));
    }

    private static JsonRpcRemoteEventNotification Parse(V2OutboundFrame frame) =>
        JsonRpcWireParser.ParseRemoteEventNotification(frame.Payload.AsSpan());

    private static string Utf8(V2OutboundFrame frame) => System.Text.Encoding.UTF8.GetString(frame.Payload.AsSpan());

    private static SystemInfo CreateSystemInfo()
    {
        var drive = new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 100, 50, "C");
        return new SystemInfo(
            new OperatingSystemInfo("Windows", "x64"),
            new ProcessorInfo("vendor", "cpu", 1, 0, 1, 1),
            new MemoryInfo(100, 50),
            drive,
            [drive],
            "test");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class BlockingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly ManualResetEventSlim _release = new(false);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow()
        {
            Entered.TrySetResult();
            _release.Wait();
            return utcNow;
        }

        internal void Release() => _release.Set();
    }

    private sealed class RecordingSender : IV2OutboundSender
    {
        private readonly SemaphoreSlim _available = new(0);
        internal ConcurrentQueue<V2OutboundFrame> Frames { get; } = new();
        internal TaskCompletionSource<V2ConnectionCloseReason> Closed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            Frames.Enqueue(frame);
            _available.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken)
        {
            Closed.TrySetResult(reason);
            return ValueTask.CompletedTask;
        }

        internal async Task<V2OutboundFrame[]> TakeAsync(int count)
        {
            for (var index = 0; index < count; index++)
                Assert.True(await _available.WaitAsync(TimeSpan.FromSeconds(10)));
            return Frames.ToArray();
        }
    }

    private sealed class BlockingSender : IV2OutboundSender
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            SendStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        internal void Release() => _release.TrySetResult();
    }

    private sealed class ThrowingSubscribeDomainEventPort(int throwOnSubscription) : IDomainEventPort
    {
        private int _activeSubscriptions;

        internal int SubscribeAttempts { get; private set; }
        internal int DisposeOwnerCalls { get; private set; }
        internal int ActiveSubscriptions => Volatile.Read(ref _activeSubscriptions);
        internal DomainEventOwner? Owner { get; private set; }

        public DomainEventOwner CreateOwner(string name)
        {
            Owner = new DomainEventOwner(name);
            return Owner;
        }

        public void Subscribe<TEvent>(
            DomainEventOwner owner,
            Func<TEvent, CancellationToken, ValueTask> handler)
            where TEvent : IDomainEvent
        {
            SubscribeAttempts++;
            if (SubscribeAttempts == throwOnSubscription)
                throw new InvalidOperationException("Injected subscription failure.");
            Interlocked.Increment(ref _activeSubscriptions);
        }

        public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => throw new NotSupportedException();

        public void DisposeOwner(DomainEventOwner owner)
        {
            DisposeOwnerCalls++;
            owner.TryDispose();
            Interlocked.Exchange(ref _activeSubscriptions, 0);
        }
    }
}
