using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.State;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace MCServerLauncher.Daemon.ApiTests.State;

public sealed class StatePublisherTests
{
    [Fact]
    public void InitialState_HasVersionZero()
    {
        var value = new CounterState(0);
        var publisher = new StatePublisher<CounterState>(value);

        var current = publisher.Current;

        Assert.Equal(0, current.Version);
        Assert.Same(value, current.Value);
    }

    [Fact]
    public void Update_PublishesNewStateWithoutMutatingHistoricalState()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(1));
        var historical = publisher.Current;

        var current = publisher.Update(value => value with { Value = 2 });

        Assert.Equal(0, historical.Version);
        Assert.Equal(1, historical.Value.Value);
        Assert.Equal(1, current.Version);
        Assert.Equal(2, current.Value.Value);
        Assert.Same(current, publisher.Current);
    }

    [Fact]
    public void Update_WhenUpdaterThrows_LeavesPublishedStateUnchanged()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(1));
        var before = publisher.Current;

        Assert.Throws<InvalidOperationException>(() => publisher.Update(_ => throw new InvalidOperationException("failure")));

        Assert.Same(before, publisher.Current);
        Assert.Equal(0, publisher.Current.Version);
        Assert.Equal(1, publisher.Current.Value.Value);
    }

    [Fact]
    public void Update_WhenUpdaterReentersUpdate_RejectsReentrancyWithoutPublishing()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        var before = publisher.Current;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            publisher.Update(_ =>
            {
                publisher.Update(value => value with { Value = value.Value + 1 });
                return new CounterState(1);
            }));

        Assert.Contains("reenter", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(before, publisher.Current);
    }

    [Fact]
    public void Update_WhenUpdaterReentersAuthoritativePublish_RejectsReentrancyWithoutPublishing()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        var before = publisher.Current;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            publisher.Update(_ =>
            {
                publisher.Publish(42, new CounterState(42));
                return new CounterState(1);
            }));

        Assert.Contains("reenter", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(before, publisher.Current);
    }

    [Fact]
    public void Publish_AcceptsNonContiguousAuthoritativeVersions()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(0));

        var first = publisher.Publish(42, new CounterState(42));
        var second = publisher.Publish(1_024, new CounterState(1_024));

        Assert.Equal(42, first.Version);
        Assert.Equal(42, first.Value.Value);
        Assert.Equal(1_024, second.Version);
        Assert.Equal(1_024, second.Value.Value);
        Assert.Same(second, publisher.Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    [InlineData(42)]
    public void Publish_WhenVersionIsNotNewer_LeavesPublishedStateUnchanged(long version)
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        var published = publisher.Publish(42, new CounterState(42));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => publisher.Publish(version, new CounterState(version)));

        Assert.Equal("version", exception.ParamName);
        Assert.Same(published, publisher.Current);
        Assert.Equal(42, publisher.Current.Version);
        Assert.Equal(42, publisher.Current.Value.Value);
    }

    [Fact]
    public void Publish_WhenValueIsNull_LeavesPublishedStateUnchanged()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        var before = publisher.Current;

        Assert.Throws<ArgumentNullException>(() => publisher.Publish(42, null!));

        Assert.Same(before, publisher.Current);
    }

    [Fact]
    public void Current_IsAllocationFreeAfterWarmup()
    {
        var publisher = new StatePublisher<CounterState>(new CounterState(1));
        _ = publisher.Current;

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        PublishedState<CounterState>? current = null;
        for (var index = 0; index < 10_000; index++)
        {
            current = publisher.Current;
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Same(publisher.Current, current);
        Assert.Equal(0, allocatedAfter - allocatedBefore);
    }

    [Fact]
    public async Task ConcurrentReadersAndWriters_PublishMonotonicCompleteStates()
    {
        const int readerCount = 32;
        const int writerCount = 4;
        const int updatesPerWriter = 128;

        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        using var start = new Barrier(readerCount + writerCount);
        using var writersCompleted = new CountdownEvent(writerCount);
        using var readersObservedFirstPublish = new CountdownEvent(readerCount);
        using var firstPublishCompleted = new ManualResetEventSlim(false);
        var failures = new ConcurrentQueue<Exception>();
        var tasks = new List<Task>(readerCount + writerCount);
        long observationCount = 0;

        for (var writer = 0; writer < writerCount; writer++)
        {
            var writerIndex = writer;
            tasks.Add(Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                        var firstUpdate = writerIndex == 0 ? 1 : 0;
                        if (firstUpdate == 1)
                        {
                            publisher.Update(value => value with { Value = value.Value + 1 });
                            firstPublishCompleted.Set();
                        }

                        Assert.True(firstPublishCompleted.Wait(TimeSpan.FromSeconds(10)));
                        Assert.True(readersObservedFirstPublish.Wait(TimeSpan.FromSeconds(10)));

                        for (var update = firstUpdate; update < updatesPerWriter; update++)
                        {
                            publisher.Update(value => value with { Value = value.Value + 1 });
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                    finally
                    {
                        writersCompleted.Signal();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        }

        for (var reader = 0; reader < readerCount; reader++)
        {
            tasks.Add(Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                        Assert.True(firstPublishCompleted.Wait(TimeSpan.FromSeconds(10)));
                        var observedVersion = -1L;
                        var hasObservedFirstPublish = false;
                        while (!writersCompleted.IsSet)
                        {
                            var current = publisher.Current;
                            Interlocked.Increment(ref observationCount);
                            if (current.Version < observedVersion || current.Version != current.Value.Value)
                            {
                                failures.Enqueue(new InvalidOperationException("Reader observed an invalid published state."));
                                return;
                            }

                            observedVersion = current.Version;
                            if (!hasObservedFirstPublish)
                            {
                                readersObservedFirstPublish.Signal();
                                hasObservedFirstPublish = true;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue(exception);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(failures);
        Assert.True(Volatile.Read(ref observationCount) >= readerCount);

        var current = publisher.Current;
        Assert.Equal(writerCount * updatesPerWriter, current.Version);
        Assert.Equal(current.Version, current.Value.Value);
    }

    [Fact]
    public async Task ConcurrentUpdatesAndAuthoritativePublishes_ReturnUniqueVersionsAndEndAtTheMaximum()
    {
        const int updateCount = 256;
        const int authoritativePublishCount = 32;

        var publisher = new StatePublisher<CounterState>(new CounterState(0));
        var failures = new ConcurrentQueue<Exception>();
        var publishedVersions = new ConcurrentQueue<long>();

        var updateTask = Task.Run(() =>
        {
            try
            {
                for (var index = 0; index < updateCount; index++)
                {
                    var published = publisher.Update(value => value with { Value = value.Value + 1 });
                    publishedVersions.Enqueue(published.Version);
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        var publishTask = Task.Run(() =>
        {
            try
            {
                for (var index = 1; index <= authoritativePublishCount; index++)
                {
                    var published = publisher.Publish(index * 1_000_000L, new CounterState(index));
                    publishedVersions.Enqueue(published.Version);
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        await Task.WhenAll(updateTask, publishTask).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(failures);
        Assert.Equal(updateCount + authoritativePublishCount, publishedVersions.Count);
        Assert.Equal(publishedVersions.Count, publishedVersions.Distinct().Count());
        Assert.Equal(publishedVersions.Max(), publisher.Current.Version);
    }

    [Fact]
    public void InstanceSnapshots_AreImmutableAndCatalogUpdatesReturnNewCatalogs()
    {
        var instanceId = Guid.NewGuid();
        var snapshot = new InstanceSnapshot(
            instanceId,
            "Example",
            InstanceType.MCJava,
            "1.21.8",
            InstanceStatus.Stopped,
            ReadyTimedOut: false);
        var mutableSource = new Dictionary<Guid, InstanceSnapshot> { [instanceId] = snapshot };
        var catalog = new InstanceCatalogSnapshot(mutableSource);

        var changedSnapshot = snapshot with { Status = InstanceStatus.Running };
        var changedCatalog = new InstanceCatalogSnapshot(ImmutableDictionary<Guid, InstanceSnapshot>.Empty.Add(instanceId, changedSnapshot));
        mutableSource.Clear();

        Assert.Equal(InstanceStatus.Stopped, catalog.Instances[instanceId].Status);
        Assert.Single(catalog.Instances);
        Assert.Equal(InstanceStatus.Running, changedCatalog.Instances[instanceId].Status);
        Assert.NotSame(catalog.Instances, changedCatalog.Instances);
        Assert.Throws<ArgumentNullException>(() => new InstanceCatalogSnapshot(null!));
    }

    [Fact]
    public void InstanceCatalogSnapshot_RejectsInvalidEntries()
    {
        var instanceId = Guid.NewGuid();
        var snapshot = new InstanceSnapshot(
            instanceId,
            "Example",
            InstanceType.MCJava,
            "1.21.8",
            InstanceStatus.Stopped,
            ReadyTimedOut: false);

        Assert.Throws<ArgumentException>(() => new InstanceCatalogSnapshot([
            new KeyValuePair<Guid, InstanceSnapshot>(instanceId, null!)
        ]));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogSnapshot([
            new KeyValuePair<Guid, InstanceSnapshot>(Guid.NewGuid(), snapshot)
        ]));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogSnapshot([
            new KeyValuePair<Guid, InstanceSnapshot>(Guid.NewGuid(), snapshot with { Id = Guid.Empty })
        ]));
        Assert.Throws<ArgumentException>(() => new InstanceCatalogSnapshot([
            new KeyValuePair<Guid, InstanceSnapshot>(instanceId, snapshot),
            new KeyValuePair<Guid, InstanceSnapshot>(instanceId, snapshot)
        ]));
    }

    private sealed record CounterState(long Value);
}
