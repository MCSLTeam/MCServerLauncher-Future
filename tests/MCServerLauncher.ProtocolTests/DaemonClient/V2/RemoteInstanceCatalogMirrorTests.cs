using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.DaemonClient.State;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class RemoteInstanceCatalogMirrorTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void InitialState_IsEmptyVersionZero()
    {
        var mirror = new RemoteInstanceCatalogMirror();

        Assert.Equal(0, mirror.Current.Version);
        Assert.Empty(mirror.Current.Value.Instances);
        Assert.False(mirror.TryGet(FirstId, out _));
    }

    [Fact]
    public void NonEmptyFullVersionZero_ReplacesPublisherWithoutMutatingHistoricalHandle()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var historical = mirror.Current;
        var generation = mirror.BeginReconciliation();

        var transition = mirror.ApplyFullSnapshot(generation, Full(0, Item(FirstId, "one")));

        Assert.Equal(RemoteInstanceCatalogTransition.Ready, transition);
        Assert.Equal(0, mirror.Current.Version);
        Assert.True(mirror.TryGet(FirstId, out var current));
        Assert.Equal("one", current.Name);
        Assert.Empty(historical.Value.Instances);
        Assert.NotSame(historical, mirror.Current);
    }

    [Fact]
    public void BufferedChangesAndFullSnapshot_PublishOnlyFinalCompleteState()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var historical = mirror.Current;
        var generation = mirror.BeginReconciliation();

        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "one")));
        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(2, SecondId, "two")));
        Assert.Same(historical, mirror.Current);

        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(generation, Full(0)));

        Assert.Equal(2, mirror.Current.Version);
        Assert.Equal(2, mirror.Current.Value.Instances.Count);
        Assert.Empty(historical.Value.Instances);
    }

    [Fact]
    public void FullSnapshot_IsAuthoritativeAndRemovesAbsentInstances()
    {
        var mirror = ReadyMirror(4, Item(FirstId, "one"), Item(SecondId, "two"));
        var generation = mirror.BeginReconciliation();

        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(generation, Full(5, Item(SecondId, "updated"))));

        Assert.False(mirror.TryGet(FirstId, out _));
        Assert.True(mirror.TryGet(SecondId, out var remaining));
        Assert.Equal("updated", remaining.Name);
    }

    [Fact]
    public void Reconciliation_CanPublishSameAndLowerAuthoritativeVersionsExactly()
    {
        var mirror = ReadyMirror(5, Item(FirstId, "five"));

        var sameGeneration = mirror.BeginReconciliation();
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(sameGeneration, Full(5, Item(FirstId, "same"))));
        Assert.Equal(5, mirror.Current.Version);
        Assert.Equal("same", mirror.Current.Value.Instances[FirstId].Name);

        var lowerGeneration = mirror.BeginReconciliation();
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(lowerGeneration, Full(2, Item(SecondId, "restart"))));
        Assert.Equal(2, mirror.Current.Version);
        Assert.False(mirror.TryGet(FirstId, out _));
        Assert.Equal("restart", mirror.Current.Value.Instances[SecondId].Name);
    }

    [Fact]
    public void FullSnapshotOverlap_DiscardsOlderAndAcceptsMatchingSameVersionEffect()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var generation = mirror.BeginReconciliation();

        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "obsolete")));
        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(2, SecondId, "represented")));

        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(generation, Full(2, Item(SecondId, "represented"))));
        Assert.Equal(2, mirror.Current.Version);
        Assert.False(mirror.TryGet(FirstId, out _));
        Assert.True(mirror.TryGet(SecondId, out _));
    }

    [Fact]
    public void FullSnapshotOverlap_WithConflictingSameVersionEffectNeedsResyncWithoutPublication()
    {
        var mirror = ReadyMirror(7, Item(FirstId, "stale-publication"));
        var before = mirror.Current;
        var generation = mirror.BeginReconciliation();
        mirror.ReceiveChange(generation, Upsert(3, SecondId, "event-value"));

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ApplyFullSnapshot(generation, Full(3, Item(SecondId, "full-value"))));
        Assert.Same(before, mirror.Current);
        Assert.Equal("stale-publication", mirror.Current.Value.Instances[FirstId].Name);
    }

    [Fact]
    public void BufferedDuplicate_SamePayloadIsIgnoredAndDifferentPayloadNeedsResync()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var generation = mirror.BeginReconciliation();

        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "one")));
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "one")));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "different")));
    }

    [Fact]
    public void ReadyDuplicate_SamePayloadIsIgnoredAndDifferentPayloadNeedsResync()
    {
        var mirror = ReadyMirror(out var generation, 0);
        var change = Upsert(1, FirstId, "one");

        Assert.Equal(RemoteInstanceCatalogTransition.Applied, mirror.ReceiveChange(generation, change));
        var published = mirror.Current;
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "one")));
        Assert.Same(published, mirror.Current);
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "different")));
        Assert.Same(published, mirror.Current);
    }

    [Fact]
    public void CurrentVersionWithoutHistory_AcceptsOnlyEffectRepresentedByFullSnapshot()
    {
        var mirror = ReadyMirror(out var generation, 5, Item(FirstId, "represented"));

        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            mirror.ReceiveChange(generation, Upsert(5, FirstId, "represented")));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(5, FirstId, "different")));
    }

    [Fact]
    public void ReadyGap_NeedsResyncWithoutPartialPublication()
    {
        var mirror = ReadyMirror(out var generation, 0);
        var before = mirror.Current;

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(2, FirstId, "gap")));
        Assert.Same(before, mirror.Current);
        Assert.False(mirror.TryGet(FirstId, out _));
    }

    [Fact]
    public void NeedsResync_RejectsSameGenerationUntilNewReconciliation()
    {
        var mirror = ReadyMirror(out var failedGeneration, 0);
        var before = mirror.Current;

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(failedGeneration, Upsert(2, FirstId, "gap")));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ApplyFullSnapshot(failedGeneration, Full(1, Item(FirstId, "same-generation"))));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(failedGeneration, Upsert(1, FirstId, "same-generation")));
        Assert.Same(before, mirror.Current);

        var recoveryGeneration = mirror.BeginReconciliation();
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredStaleGeneration,
            mirror.ReceiveChange(failedGeneration, Upsert(1, FirstId, "stale")));
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(recoveryGeneration, Full(1, Item(FirstId, "recovered"))));
        Assert.Equal(1, mirror.Current.Version);
        Assert.Equal("recovered", mirror.Current.Value.Instances[FirstId].Name);
    }

    [Fact]
    public void BufferedPrefixFailures_NeverPublishPartialCandidate()
    {
        var scenarios = new (string Name, Action<RemoteInstanceCatalogMirror, long> Arrange)[]
        {
            ("gap", (mirror, generation) =>
                mirror.ReceiveChange(generation, Upsert(3, SecondId, "gap"))),
            ("unknown-remove", (mirror, generation) =>
                mirror.ReceiveChange(generation, Remove(2, SecondId))),
            ("same-version-conflict", (mirror, generation) =>
            {
                mirror.ReceiveChange(generation, Upsert(2, SecondId, "first"));
                mirror.ReceiveChange(generation, Upsert(2, SecondId, "conflict"));
            })
        };

        foreach (var scenario in scenarios)
        {
            var mirror = ReadyMirror(9, Item(FirstId, "published"));
            var before = mirror.Current;
            var beforeValue = before.Value;
            var generation = mirror.BeginReconciliation();
            Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
                mirror.ReceiveChange(generation, Upsert(1, FirstId, "valid-prefix")));
            scenario.Arrange(mirror, generation);

            Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
                mirror.ApplyFullSnapshot(generation, Full(0)));
            Assert.Same(before, mirror.Current);
            Assert.Same(beforeValue, mirror.Current.Value);
            Assert.Equal(9, mirror.Current.Version);
            Assert.Equal("published", mirror.Current.Value.Instances[FirstId].Name);
        }
    }

    [Fact]
    public void BufferedGap_NeedsResyncWithoutPublishingFullCandidate()
    {
        var mirror = ReadyMirror(9, Item(FirstId, "old"));
        var before = mirror.Current;
        var generation = mirror.BeginReconciliation();
        mirror.ReceiveChange(generation, Upsert(2, SecondId, "gap"));

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ApplyFullSnapshot(generation, Full(0)));
        Assert.Same(before, mirror.Current);
    }

    [Fact]
    public void UnknownRemove_IsAnInconsistencyInBufferedAndReadyModes()
    {
        var buffered = new RemoteInstanceCatalogMirror();
        var bufferedGeneration = buffered.BeginReconciliation();
        buffered.ReceiveChange(bufferedGeneration, Remove(1, FirstId));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            buffered.ApplyFullSnapshot(bufferedGeneration, Full(0)));

        var ready = ReadyMirror(out var readyGeneration, 0);
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            ready.ReceiveChange(readyGeneration, Remove(1, FirstId)));
        Assert.Equal(0, ready.Current.Version);
    }

    [Fact]
    public void SuccessfulRemoveDeltas_RemoveInstancesInReadyAndBufferedReplayModes()
    {
        var ready = ReadyMirror(out var readyGeneration, 0, Item(FirstId, "first"), Item(SecondId, "second"));

        Assert.Equal(RemoteInstanceCatalogTransition.Applied,
            ready.ReceiveChange(readyGeneration, Remove(1, FirstId)));
        Assert.Equal(1, ready.Current.Version);
        Assert.False(ready.TryGet(FirstId, out _));
        Assert.True(ready.TryGet(SecondId, out var readyRemaining));
        Assert.Equal("second", readyRemaining.Name);

        var buffered = new RemoteInstanceCatalogMirror();
        var bufferedGeneration = buffered.BeginReconciliation();
        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            buffered.ReceiveChange(bufferedGeneration, Remove(1, FirstId)));

        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            buffered.ApplyFullSnapshot(bufferedGeneration, Full(0, Item(FirstId, "first"), Item(SecondId, "second"))));
        Assert.Equal(1, buffered.Current.Version);
        Assert.False(buffered.TryGet(FirstId, out _));
        Assert.True(buffered.TryGet(SecondId, out var bufferedRemaining));
        Assert.Equal("second", bufferedRemaining.Name);
    }

    [Fact]
    public void BufferedReplay_RemembersFingerprintForVerifiedDuplicate()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var generation = mirror.BeginReconciliation();
        var replayed = Upsert(1, FirstId, "version-one");

        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, replayed));
        Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
            mirror.ReceiveChange(generation, Upsert(2, FirstId, "version-two")));
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(generation, Full(0)));
        var published = mirror.Current;

        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            mirror.ReceiveChange(generation, replayed));
        Assert.Same(published, mirror.Current);
        Assert.Equal(2, mirror.Current.Version);
        Assert.Equal("version-two", mirror.Current.Value.Instances[FirstId].Name);
    }

    [Fact]
    public void Upsert_CreatesAndUpdatesInstances()
    {
        var mirror = ReadyMirror(out var generation, 0);

        Assert.Equal(RemoteInstanceCatalogTransition.Applied,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "created")));
        Assert.Equal(RemoteInstanceCatalogTransition.Applied,
            mirror.ReceiveChange(generation, Upsert(2, FirstId, "updated")));

        Assert.Single(mirror.Current.Value.Instances);
        Assert.Equal("updated", mirror.Current.Value.Instances[FirstId].Name);
    }

    [Fact]
    public void BufferOverflow_NeedsResyncAndRejectsFurtherChanges()
    {
        var mirror = new RemoteInstanceCatalogMirror();
        var generation = mirror.BeginReconciliation();

        for (var version = 1; version <= RemoteInstanceCatalogMirror.MaximumBufferedChanges; version++)
        {
            Assert.Equal(RemoteInstanceCatalogTransition.Buffered,
                mirror.ReceiveChange(generation, Upsert(version, Id(version), $"item-{version}")));
        }

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(
                RemoteInstanceCatalogMirror.MaximumBufferedChanges + 1,
                Id(RemoteInstanceCatalogMirror.MaximumBufferedChanges + 1),
                "overflow")));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(generation, Upsert(1, FirstId, "ignored")));
        Assert.Empty(mirror.Current.Value.Instances);
    }

    [Fact]
    public void FingerprintHistory_AtCapacityRetainsOldestAndAfterOverflowEvictsOnlyOldest()
    {
        var atCapacitySame = BuildHistory(RemoteInstanceCatalogMirror.MaximumFingerprintHistory);
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            atCapacitySame.Mirror.ReceiveChange(
                atCapacitySame.Generation,
                Upsert(1, FirstId, "value-1")));

        var atCapacityDifferent = BuildHistory(RemoteInstanceCatalogMirror.MaximumFingerprintHistory);
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            atCapacityDifferent.Mirror.ReceiveChange(
                atCapacityDifferent.Generation,
                Upsert(1, FirstId, "different")));

        var afterOverflow = BuildHistory(RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1);
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            afterOverflow.Mirror.ReceiveChange(
                afterOverflow.Generation,
                Upsert(2, FirstId, "value-2")));
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            afterOverflow.Mirror.ReceiveChange(
                afterOverflow.Generation,
                Upsert(RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1, FirstId,
                    $"value-{RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1}")));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            afterOverflow.Mirror.ReceiveChange(
                afterOverflow.Generation,
                Upsert(1, FirstId, "value-1")));

        var afterOverflowLatestConflict = BuildHistory(RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1);
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            afterOverflowLatestConflict.Mirror.ReceiveChange(
                afterOverflowLatestConflict.Generation,
                Upsert(RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1, FirstId, "different")));

        var afterOverflowSecondConflict = BuildHistory(RemoteInstanceCatalogMirror.MaximumFingerprintHistory + 1);
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            afterOverflowSecondConflict.Mirror.ReceiveChange(
                afterOverflowSecondConflict.Generation,
                Upsert(2, FirstId, "different")));
    }

    [Fact]
    public void SameVersionUpsertFingerprint_ConflictsOnEveryStructuralField()
    {
        var canonical = Upsert(
            1,
            FirstId,
            "name",
            "version",
            InstanceType.MCJava,
            InstanceStatus.Stopped);
        var variants = new[]
        {
            Upsert(1, FirstId, "different-name", "version", InstanceType.MCJava, InstanceStatus.Stopped),
            Upsert(1, FirstId, "name", "version", InstanceType.Universal, InstanceStatus.Stopped),
            Upsert(1, FirstId, "name", "different-version", InstanceType.MCJava, InstanceStatus.Stopped),
            Upsert(1, FirstId, "name", "version", InstanceType.MCJava, InstanceStatus.Running),
            Upsert(1, SecondId, "name", "version", InstanceType.MCJava, InstanceStatus.Stopped)
        };

        foreach (var variant in variants)
        {
            var mirror = ReadyMirror(out var generation, 0);
            Assert.Equal(RemoteInstanceCatalogTransition.Applied,
                mirror.ReceiveChange(generation, canonical));
            Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
                mirror.ReceiveChange(generation, canonical));
            var beforeConflict = mirror.Current;

            Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
                mirror.ReceiveChange(generation, variant));
            Assert.Same(beforeConflict, mirror.Current);
        }
    }

    [Fact]
    public void SameVersionRemoveFingerprint_DistinguishesRepresentedEffectAndConflict()
    {
        var represented = ReadyMirror(out var representedGeneration, 5);
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            represented.ReceiveChange(representedGeneration, Remove(5, FirstId)));
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredDuplicate,
            represented.ReceiveChange(representedGeneration, Remove(5, FirstId)));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            represented.ReceiveChange(representedGeneration, Remove(5, SecondId)));

        var conflicting = ReadyMirror(out var conflictingGeneration, 5, Item(FirstId, "present"));
        var before = conflicting.Current;
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            conflicting.ReceiveChange(conflictingGeneration, Remove(5, FirstId)));
        Assert.Same(before, conflicting.Current);
    }

    [Fact]
    public void MalformedRuntimeFields_NeedResyncWithoutReflectionOrPublication()
    {
        var malformedItems = new[]
        {
            new InstanceCatalogItem(SecondId, null!, InstanceType.MCJava, "1", InstanceStatus.Stopped),
            new InstanceCatalogItem(SecondId, "name", InstanceType.MCJava, null!, InstanceStatus.Stopped)
        };

        foreach (var malformed in malformedItems)
        {
            var mirror = ReadyMirror(3, Item(FirstId, "stable"));
            var before = mirror.Current;
            var generation = mirror.BeginReconciliation();

            Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
                mirror.ApplyFullSnapshot(generation, Full(4, malformed)));
            Assert.Same(before, mirror.Current);
        }

        var malformedEventMirror = ReadyMirror(out var eventGeneration, 0);
        var malformedEvent = new InstanceCatalogChangedEventData(
            1,
            InstanceCatalogChangeOperation.Upsert,
            FirstId,
            new InstanceCatalogItem(FirstId, null!, InstanceType.MCJava, "1", InstanceStatus.Stopped));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            malformedEventMirror.ReceiveChange(eventGeneration, malformedEvent));

        Assert.Throws<ArgumentException>(() => Full(0, (InstanceCatalogItem)null!));
        Assert.Throws<ArgumentException>(() =>
            new InstanceCatalogChangedEventData(
                1,
                InstanceCatalogChangeOperation.Upsert,
                Guid.Empty,
                Item(FirstId, "mismatched")));
    }

    [Fact]
    public void BeginReconciliation_ClearsOldBufferAndHistoryAndIsolatesGenerations()
    {
        var mirror = ReadyMirror(out var firstGeneration, 0);
        Assert.Equal(RemoteInstanceCatalogTransition.Applied,
            mirror.ReceiveChange(firstGeneration, Upsert(1, FirstId, "first")));

        var secondGeneration = mirror.BeginReconciliation();
        mirror.ReceiveChange(secondGeneration, Upsert(2, SecondId, "discarded"));
        var thirdGeneration = mirror.BeginReconciliation();

        Assert.True(thirdGeneration > secondGeneration);
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredStaleGeneration,
            mirror.ReceiveChange(secondGeneration, Upsert(2, SecondId, "stale")));
        Assert.Equal(RemoteInstanceCatalogTransition.IgnoredStaleGeneration,
            mirror.ApplyFullSnapshot(secondGeneration, Full(2, Item(SecondId, "stale"))));
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(thirdGeneration, Full(2)));
        Assert.Empty(mirror.Current.Value.Instances);

        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(thirdGeneration, Upsert(1, FirstId, "first")));
    }

    [Fact]
    public void Close_IsIdempotentRejectsMutationsAndPreservesPublication()
    {
        var mirror = ReadyMirror(out var generation, 1, Item(FirstId, "one"));
        var before = mirror.Current;

        mirror.Close();
        mirror.Close();

        Assert.Equal(RemoteInstanceCatalogTransition.Closed,
            mirror.ReceiveChange(generation, Upsert(2, FirstId, "two")));
        Assert.Equal(RemoteInstanceCatalogTransition.Closed,
            mirror.ApplyFullSnapshot(generation, Full(2, Item(FirstId, "two"))));
        Assert.Throws<ObjectDisposedException>(() => mirror.BeginReconciliation());
        Assert.Same(before, mirror.Current);
    }

    [Fact]
    public void BeginReceiveFullAndCloseRace_OldGenerationNeverPublishesAndCloseIsTerminal()
    {
        var mirror = ReadyMirror(out var oldGeneration, 0, Item(FirstId, "stable"));
        Assert.Equal(RemoteInstanceCatalogTransition.NeedsResync,
            mirror.ReceiveChange(oldGeneration, Upsert(2, FirstId, "force-resync")));
        var before = mirror.Current;
        const int participantCount = 4;
        using var ready = new CountdownEvent(participantCount);
        using var start = new ManualResetEventSlim();
        using var finished = new CountdownEvent(participantCount);
        var failures = new ConcurrentQueue<Exception>();
        var transitions = new ConcurrentQueue<RemoteInstanceCatalogTransition>();
        long newGeneration = 0;
        var beginClosed = 0;

        Thread CreateParticipant(ThreadStart operation)
        {
            return new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    if (!start.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The catalog race start gate was not released.");
                    operation();
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
                finally
                {
                    finished.Signal();
                }
            })
            {
                IsBackground = true,
                Name = "RemoteInstanceCatalogMirror race participant"
            };
        }

        var threads = new[]
        {
            CreateParticipant(() =>
            {
                try
                {
                    Volatile.Write(ref newGeneration, mirror.BeginReconciliation());
                }
                catch (ObjectDisposedException)
                {
                    Volatile.Write(ref beginClosed, 1);
                }
            }),
            CreateParticipant(() => transitions.Enqueue(
                mirror.ReceiveChange(oldGeneration, Upsert(1, FirstId, "old-generation")))),
            CreateParticipant(() => transitions.Enqueue(
                mirror.ApplyFullSnapshot(oldGeneration, Full(1, Item(FirstId, "old-generation"))))),
            CreateParticipant(mirror.Close)
        };

        var allFinished = false;
        var allJoined = false;
        try
        {
            foreach (var thread in threads)
                thread.Start();

            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Race participants did not become ready.");
            start.Set();
            allFinished = finished.Wait(TimeSpan.FromSeconds(10));
        }
        finally
        {
            start.Set();
            allFinished = finished.Wait(TimeSpan.FromSeconds(10)) || allFinished;
            allJoined = JoinAll(threads, TimeSpan.FromSeconds(2));
        }

        Assert.True(allFinished, "Race participants did not finish within the bounded timeout.");
        Assert.True(allJoined, "Race participant threads did not terminate within the bounded timeout.");
        Assert.Empty(failures);
        Assert.Equal(2, transitions.Count);
        Assert.All(transitions, transition => Assert.Contains(
            transition,
            new[]
            {
                RemoteInstanceCatalogTransition.NeedsResync,
                RemoteInstanceCatalogTransition.IgnoredStaleGeneration,
                RemoteInstanceCatalogTransition.Closed
            }));
        Assert.True(Volatile.Read(ref beginClosed) == 1 || Volatile.Read(ref newGeneration) > oldGeneration);
        Assert.Same(before, mirror.Current);
        Assert.Equal("stable", mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(RemoteInstanceCatalogTransition.Closed,
            mirror.ReceiveChange(oldGeneration, Upsert(1, FirstId, "after-close")));
        Assert.Equal(RemoteInstanceCatalogTransition.Closed,
            mirror.ApplyFullSnapshot(oldGeneration, Full(1, Item(FirstId, "after-close"))));
        Assert.Throws<ObjectDisposedException>(() => mirror.BeginReconciliation());
        Assert.Same(before, mirror.Current);
    }

    [Fact]
    public void CurrentAndTryGet_ExposeImmutableHistoricalHandles()
    {
        var mirror = ReadyMirror(out var generation, 0, Item(FirstId, "zero"));
        var historical = mirror.Current;

        mirror.ReceiveChange(generation, Upsert(1, FirstId, "one"));
        var current = mirror.Current;

        Assert.Equal("zero", historical.Value.Instances[FirstId].Name);
        Assert.Equal("one", current.Value.Instances[FirstId].Name);
        Assert.True(mirror.TryGet(FirstId, out var lookup));
        Assert.Same(current.Value.Instances[FirstId], lookup);
        Assert.NotSame(historical.Value, current.Value);
    }

    [Fact]
    public void ConcurrentReadersWithFullDeltaAndReconnect_NeverObserveTornCatalogs()
    {
        const int readerCount = 32;
        const int cycles = 80;
        var mirror = ReadyMirror(
            0,
            Item(FirstId, Marker(0), Marker(0)),
            Item(SecondId, Marker(0), Marker(0)));
        using var ready = new CountdownEvent(readerCount);
        using var start = new ManualResetEventSlim();
        using var initialRead = new CountdownEvent(readerCount);
        using var stop = new ManualResetEventSlim();
        using var finished = new CountdownEvent(readerCount);
        var failures = new ConcurrentQueue<Exception>();
        var observedPublicationChange = 0;
        var readers = Enumerable.Range(0, readerCount).Select(readerIndex => new Thread(() =>
        {
            var initialReadSignaled = false;
            try
            {
                ready.Signal();
                if (!start.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException($"Reader {readerIndex} did not receive the start signal.");

                var startingHandle = mirror.Current;
                var startingVersion = startingHandle.Version;
                AssertCompleteHandle(startingHandle);
                initialRead.Signal();
                initialReadSignaled = true;
                var observed = false;
                while (!stop.IsSet)
                {
                    var handle = mirror.Current;
                    AssertCompleteHandle(handle);
                    if (!observed && handle.Version != startingVersion)
                    {
                        observed = true;
                        Interlocked.Increment(ref observedPublicationChange);
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
            finally
            {
                if (!initialReadSignaled)
                {
                    try
                    {
                        initialRead.Signal();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                finished.Signal();
            }
        })
        {
            IsBackground = true,
            Name = $"RemoteInstanceCatalogMirror reader {readerIndex}"
        }).ToArray();

        Exception? writerFailure = null;
        var allFinished = false;
        var allJoined = false;
        try
        {
            foreach (var reader in readers)
                reader.Start();

            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Readers did not become ready.");
            start.Set();
            Assert.True(initialRead.Wait(TimeSpan.FromSeconds(10)), "Readers did not capture the initial publication.");

            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                var generation = mirror.BeginReconciliation();
                var fullVersion = cycle * 2L;
                var marker = Marker(cycle);
                Assert.Equal(RemoteInstanceCatalogTransition.Ready,
                    mirror.ApplyFullSnapshot(generation, Full(
                        fullVersion,
                        Item(FirstId, marker, marker),
                        Item(SecondId, marker, marker))));

                var deltaMarker = Marker(cycle + 10_000);
                Assert.Equal(RemoteInstanceCatalogTransition.Applied,
                    mirror.ReceiveChange(generation, Upsert(fullVersion + 1, FirstId, deltaMarker, deltaMarker)));
                Assert.Equal(RemoteInstanceCatalogTransition.Applied,
                    mirror.ReceiveChange(generation, Upsert(fullVersion + 2, SecondId, deltaMarker, deltaMarker)));
            }

            Assert.True(
                SpinWait.SpinUntil(
                    () => Volatile.Read(ref observedPublicationChange) == readerCount,
                    TimeSpan.FromSeconds(10)),
                "Every reader must overlap the writer and observe a publication change.");
        }
        catch (Exception exception)
        {
            writerFailure = exception;
        }
        finally
        {
            start.Set();
            stop.Set();
            allFinished = finished.Wait(TimeSpan.FromSeconds(10));
            allJoined = JoinAll(readers, TimeSpan.FromSeconds(2));
        }

        Assert.True(allFinished, "Reader threads did not finish within the bounded timeout.");
        Assert.True(allJoined, "Reader threads did not terminate within the bounded timeout.");
        if (writerFailure is not null)
            ExceptionDispatchInfo.Capture(writerFailure).Throw();
        Assert.Empty(failures);
        Assert.Equal(readerCount, Volatile.Read(ref observedPublicationChange));
        Assert.Equal(Marker(cycles + 10_000), mirror.Current.Value.Instances[FirstId].Name);
        Assert.Equal(Marker(cycles + 10_000), mirror.Current.Value.Instances[SecondId].Name);
    }

    private static RemoteInstanceCatalogMirror ReadyMirror(
        long version,
        params InstanceCatalogItem[] items)
    {
        return ReadyMirror(out _, version, items);
    }

    private static RemoteInstanceCatalogMirror ReadyMirror(
        out long generation,
        long version,
        params InstanceCatalogItem[] items)
    {
        var mirror = new RemoteInstanceCatalogMirror();
        generation = mirror.BeginReconciliation();
        Assert.Equal(RemoteInstanceCatalogTransition.Ready,
            mirror.ApplyFullSnapshot(generation, Full(version, items)));
        return mirror;
    }

    private static (RemoteInstanceCatalogMirror Mirror, long Generation) BuildHistory(int count)
    {
        var mirror = ReadyMirror(out var generation, 0);
        for (var version = 1; version <= count; version++)
        {
            Assert.Equal(RemoteInstanceCatalogTransition.Applied,
                mirror.ReceiveChange(generation, Upsert(version, FirstId, $"value-{version}")));
        }

        return (mirror, generation);
    }

    private static void AssertCompleteHandle(PublishedState<InstanceCatalogSnapshot> handle)
    {
        var values = handle.Value.Instances;
        if (values.Count != 2 ||
            !values.TryGetValue(FirstId, out var first) ||
            !values.TryGetValue(SecondId, out var second) ||
            first.Id != FirstId || second.Id != SecondId ||
            first.Name != first.Version || second.Name != second.Version)
        {
            throw new InvalidOperationException($"Observed a torn publication at version {handle.Version}.");
        }
    }

    private static bool JoinAll(IEnumerable<Thread> threads, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var allJoined = true;
        foreach (var thread in threads)
        {
            var remaining = timeout - stopwatch.Elapsed;
            allJoined &= remaining > TimeSpan.Zero && thread.Join(remaining);
        }
        return allJoined;
    }

    private static InstanceCatalogResult Full(long version, params InstanceCatalogItem[] items) =>
        new(version, items.ToImmutableArray());

    private static InstanceCatalogItem Item(
        Guid id,
        string name,
        string version = "1",
        InstanceType instanceType = InstanceType.MCJava,
        InstanceStatus status = InstanceStatus.Stopped) =>
        new(id, name, instanceType, version, status);

    private static InstanceCatalogChangedEventData Upsert(
        long version,
        Guid id,
        string name,
        string itemVersion = "1",
        InstanceType instanceType = InstanceType.MCJava,
        InstanceStatus status = InstanceStatus.Stopped) =>
        new(version, InstanceCatalogChangeOperation.Upsert, id,
            Item(id, name, itemVersion, instanceType, status));

    private static InstanceCatalogChangedEventData Remove(long version, Guid id) =>
        new(version, InstanceCatalogChangeOperation.Remove, id, null);

    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    private static string Marker(int value) => $"marker-{value:D5}";
}
