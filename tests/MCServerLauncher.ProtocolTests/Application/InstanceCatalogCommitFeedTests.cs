using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceCatalogCommitFeedTests
{
    [Fact]
    public async Task ConcurrentAuthoritativeCommits_ArePublishedOnceInStrictVersionOrder_AndDrainOnStop()
    {
        const int writerCount = 64;
        using var domainEvents = DomainEventPortTestHost.Create();
        var feed = new InstanceCatalogCommitFeed();
        var source = new AuthoritativeInstanceSnapshotSource([], feed);
        var bridge = new InstanceCatalogDomainEventBridge(feed, domainEvents.Port);
        var observed = new ConcurrentQueue<InstanceCatalogChangedEventData>();
        var owner = domainEvents.Port.CreateOwner(nameof(InstanceCatalogCommitFeedTests));
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(owner, (domainEvent, _) =>
        {
            observed.Enqueue(domainEvent.Data);
            return ValueTask.CompletedTask;
        });
        bridge.Start();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var instances = Enumerable.Range(0, writerCount)
            .Select(index => new StubInstance(CreateConfig(index)))
            .ToArray();
        var writers = instances.Select(instance => Task.Run(async () =>
        {
            await start.Task;
            source.Upsert(instance);
        })).ToArray();
        start.TrySetResult();
        await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(10));

        await Task.WhenAll(instances.Where((_, index) => index % 2 == 0)
                .Select(instance => Task.Run(() => source.Remove(instance.Config.Uuid))))
            .WaitAsync(TimeSpan.FromSeconds(10));
        feed.CompleteProduction();
        await bridge.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var changes = observed.ToArray();
        Assert.Equal(writerCount + writerCount / 2, changes.Length);
        Assert.Equal(source.Current.Version, changes[^1].Version);
        Assert.Equal(
            Enumerable.Range(1, changes.Length).Select(static version => (long)version),
            changes.Select(static change => change.Version));
        Assert.Equal(changes.Length, changes.Select(static change => change.Version).Distinct().Count());

        var upserts = changes.Where(static change => change.Operation == InstanceCatalogChangeOperation.Upsert).ToArray();
        Assert.Equal(writerCount, upserts.Length);
        Assert.All(upserts, change =>
        {
            Assert.NotNull(change.Snapshot);
            Assert.Equal(change.InstanceId, change.Snapshot.InstanceId);
        });

        var removals = changes.Where(static change => change.Operation == InstanceCatalogChangeOperation.Remove).ToArray();
        Assert.Equal(writerCount / 2, removals.Length);
        Assert.All(removals, static change => Assert.Null(change.Snapshot));
        Assert.Equal(
            instances.Where((_, index) => index % 2 == 0).Select(static instance => instance.Config.Uuid).Order(),
            removals.Select(static change => change.InstanceId).Order());
        domainEvents.Port.DisposeOwner(owner);
    }

    [Fact]
    public async Task DrainAsync_WaitsForQueuedSubscriberWork_WithoutShutdownCancellation()
    {
        using var domainEvents = DomainEventPortTestHost.Create();
        var feed = new InstanceCatalogCommitFeed();
        var source = new AuthoritativeInstanceSnapshotSource([], feed);
        var bridge = new InstanceCatalogDomainEventBridge(feed, domainEvents.Port);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = domainEvents.Port.CreateOwner(nameof(DrainAsync_WaitsForQueuedSubscriberWork_WithoutShutdownCancellation));
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(owner, async (_, cancellationToken) =>
        {
            Assert.False(cancellationToken.CanBeCanceled);
            await release.Task;
            handled.TrySetResult();
        });
        bridge.Start();
        source.Upsert(new StubInstance(CreateConfig(1)));
        feed.CompleteProduction();

        var drain = bridge.DrainAsync();
        Assert.False(drain.IsCompleted);
        release.TrySetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        domainEvents.Port.DisposeOwner(owner);
    }

    [Fact]
    public void CompletedFeed_RejectsFurtherCommitsExplicitly()
    {
        var feed = new InstanceCatalogCommitFeed();
        var source = new AuthoritativeInstanceSnapshotSource([], feed);
        feed.CompleteProduction();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            source.Upsert(new StubInstance(CreateConfig(1))));

        Assert.Contains("production has stopped", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.Current.Version);
        Assert.Empty(source.Current.Value.Instances);
    }

    [Fact]
    public async Task Start_WithPrefilledCommit_AllowsSubscriberToReenterStart()
    {
        using var domainEvents = DomainEventPortTestHost.Create();
        var feed = new InstanceCatalogCommitFeed();
        var source = new AuthoritativeInstanceSnapshotSource([], feed);
        var bridge = new InstanceCatalogDomainEventBridge(feed, domainEvents.Port);
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = domainEvents.Port.CreateOwner(nameof(Start_WithPrefilledCommit_AllowsSubscriberToReenterStart));
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(owner, (_, _) =>
        {
            bridge.Start();
            handled.TrySetResult();
            return ValueTask.CompletedTask;
        });
        source.Upsert(new StubInstance(CreateConfig(1)));
        feed.CompleteProduction();

        bridge.Start();
        await bridge.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        domainEvents.Port.DisposeOwner(owner);
    }

    [Fact]
    public async Task ShutdownAdmission_WaitsForLastAdmittedCommit_ThenPublishesItExactlyOnce()
    {
        using var domainEvents = DomainEventPortTestHost.Create();
        var feed = new InstanceCatalogCommitFeed();
        var source = new AuthoritativeInstanceSnapshotSource([], feed);
        var bridge = new InstanceCatalogDomainEventBridge(feed, domainEvents.Port);
        var gate = new InstanceMutationAdmissionGate();
        var observed = new ConcurrentQueue<InstanceCatalogChangedEventData>();
        var owner = domainEvents.Port.CreateOwner(nameof(ShutdownAdmission_WaitsForLastAdmittedCommit_ThenPublishesItExactlyOnce));
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(owner, (domainEvent, _) =>
        {
            observed.Enqueue(domainEvent.Data);
            return ValueTask.CompletedTask;
        });
        bridge.Start();
        var admitted = gate.EnterExternal();
        var shutdownDrain = gate.StopExternalAdmissionAndDrainAsync();

        Assert.False(shutdownDrain.IsCompleted);
        source.Upsert(new StubInstance(CreateConfig(1)));
        admitted.Dispose();
        await shutdownDrain.WaitAsync(TimeSpan.FromSeconds(10));
        feed.CompleteProduction();
        await bridge.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var change = Assert.Single(observed);
        Assert.Equal(1, change.Version);
        Assert.Equal(source.Current.Version, change.Version);
        domainEvents.Port.DisposeOwner(owner);
    }

    [Fact]
    public async Task ShutdownAdmission_RejectsNewRemoveAndUpdateBeforeRuntimeStateChanges()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(1);
        var instance = new StubInstance(config);
        var instanceDirectory = config.GetWorkingDirectory();
        var markerPath = Path.Combine(instanceDirectory, "admission-marker.txt");
        Directory.CreateDirectory(instanceDirectory);
        await File.WriteAllTextAsync(markerPath, "unchanged");
        try
        {
            manager.ReplaceInstance(config.Uuid, instance);
            var beforeVersion = manager.InstanceSnapshotSource.Current.Version;
            await manager.MutationAdmission.StopExternalAdmissionAndDrainAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.TryRemoveInstance(config.Uuid));
            Assert.Throws<InvalidOperationException>(() =>
                manager.ReplaceInstance(config.Uuid, new StubInstance(CreateConfig(2))));
            var application = new LocalInstanceApplication(manager);
            var update = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "rejected-update",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    "rejected-version",
                    null,
                    false),
                CancellationToken.None);

            Assert.True(update.IsErr(out _));
            Assert.Same(instance, manager.Instances[config.Uuid]);
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(beforeVersion, manager.InstanceSnapshotSource.Current.Version);
            Assert.Equal("unchanged", await File.ReadAllTextAsync(markerPath));
        }
        finally
        {
            if (Directory.Exists(instanceDirectory))
                Directory.Delete(instanceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProducerDrain_WaitsForActiveStatusCallback_AndRejectsLateStatusMutation()
    {
        using var domainEvents = DomainEventPortTestHost.Create();
        var manager = new InstanceManager();
        var instance = new ControlledStatusInstance(CreateConfig(1));
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        using var instanceBridge = new MCServerLauncher.Daemon.Bootstrap.InstanceDomainEventBridge(
            manager,
            domainEvents.Port);
        var catalogBridge = new InstanceCatalogDomainEventBridge(manager.CatalogCommitFeed, domainEvents.Port);
        var catalogEvents = new ConcurrentQueue<InstanceCatalogChangedEventData>();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = domainEvents.Port.CreateOwner(nameof(ProducerDrain_WaitsForActiveStatusCallback_AndRejectsLateStatusMutation));
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(owner, (domainEvent, _) =>
        {
            catalogEvents.Enqueue(domainEvent.Data);
            return ValueTask.CompletedTask;
        });
        domainEvents.Port.Subscribe<InstanceStatusChangedDomainEvent>(owner, async (_, _) =>
        {
            callbackEntered.TrySetResult();
            await releaseCallback.Task;
        });
        catalogBridge.Start();

        var activeStatus = instance.RaiseStatusAsync(InstanceStatus.Running);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var producerDrain = manager.MutationAdmission.StopProducerAdmissionAndDrainAsync();
        Assert.False(producerDrain.IsCompleted);

        releaseCallback.TrySetResult();
        await activeStatus.WaitAsync(TimeSpan.FromSeconds(10));
        await producerDrain.WaitAsync(TimeSpan.FromSeconds(10));
        var versionAfterDrain = manager.InstanceSnapshotSource.Current.Version;
        Assert.True(manager.InstanceSnapshotSource.TryGet(instance.Config.Uuid, out var snapshotAfterDrain));
        Assert.Equal(InstanceStatus.Running, snapshotAfterDrain.Status);

        await instance.RaiseStatusAsync(InstanceStatus.Stopped).WaitAsync(TimeSpan.FromSeconds(10));
        manager.CatalogCommitFeed.CompleteProduction();
        await catalogBridge.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, versionAfterDrain);
        Assert.Equal(versionAfterDrain, manager.InstanceSnapshotSource.Current.Version);
        Assert.True(manager.InstanceSnapshotSource.TryGet(instance.Config.Uuid, out var finalSnapshot));
        Assert.Equal(InstanceStatus.Running, finalSnapshot.Status);
        Assert.Equal(2, catalogEvents.Count);
        Assert.Equal([1L, 2L], catalogEvents.Select(static change => change.Version).ToArray());
        domainEvents.Port.DisposeOwner(owner);
    }

    [Fact]
    public void BuiltInProtocolEventInventory_MapsExactlyFourAuthoritativeSources()
    {
        var inventory = BuiltInProtocolEventSourceInventory.All;

        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, inventory.Length);
        Assert.Collection(
            inventory,
            item => AssertSource<InstanceCatalogChangedEventData, AuthoritativeInstanceSnapshotSource, InstanceCatalogChangedDomainEvent>(item),
            item => AssertSource<DaemonReportEventData, DaemonReportPublisher, DaemonReportDomainEvent>(item),
            item => AssertSource<InstanceLogEventData, MCServerLauncher.Daemon.Bootstrap.InstanceDomainEventBridge, InstanceLogDomainEvent>(item),
            item => AssertSource<NotificationEventData, MCServerLauncher.Daemon.Remote.Event.EventTriggerService, ClientNotificationDomainEvent>(item));
        Assert.DoesNotContain(inventory, static item => item.ProjectionType == typeof(InstanceStatusChangedDomainEvent));
    }

    private static void AssertSource<TData, TSource, TProjection>(BuiltInProtocolEventSource item)
    {
        var descriptor = BuiltInProtocolDefinitions.Events.Single(candidate => candidate.DataTypeInfo.Type == typeof(TData));
        Assert.Same(descriptor, item.Descriptor);
        Assert.Equal(typeof(TSource), item.AuthoritativeSourceType);
        Assert.Equal(typeof(TProjection), item.ProjectionType);
    }

    private static InstanceConfig CreateConfig(int index)
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = $"catalog-{index}",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            Version = index.ToString(),
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private sealed class StubInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => null;
        public InstanceStatus Status => InstanceStatus.Stopped;
        public int ServerProcessId => -1;
        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }

        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegacyInstanceReport(Status, Config, [], [], default));

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) => Task.FromResult(false);

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private sealed class ControlledStatusInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => null;
        public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
        public int ServerProcessId => -1;
        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }
        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new LegacyInstanceReport(Status, Config, [], [], default));

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) => Task.FromResult(false);

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }

        internal Task RaiseStatusAsync(InstanceStatus status)
        {
            Status = status;
            return OnStatusChanged?.Invoke(Config.Uuid, status, CancellationToken.None) ?? Task.CompletedTask;
        }
    }
}
