using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.State;

namespace MCServerLauncher.DaemonClient.State;

internal enum RemoteInstanceCatalogTransition
{
    Buffered,
    Applied,
    IgnoredDuplicate,
    IgnoredStaleGeneration,
    Ready,
    NeedsResync,
    Closed
}

/// <summary>
/// Reconciles an authoritative remote instance catalog into immutable published state.
/// </summary>
internal sealed class RemoteInstanceCatalogMirror : IInstanceSnapshotSource
{
    internal const int MaximumBufferedChanges = 256;
    internal const int MaximumFingerprintHistory = 512;

    private readonly Lock _gate = new();
    private readonly SortedDictionary<long, ChangeFingerprint> _buffer = [];
    private readonly Dictionary<long, ChangeFingerprint> _history = [];
    private readonly Queue<long> _historyOrder = [];
    private StatePublisher<InstanceCatalogSnapshot> _publisher =
        new(InstanceCatalogSnapshot.Empty);
    private MirrorMode _mode = MirrorMode.AwaitingSnapshot;
    private long _generation;

    public PublishedState<InstanceCatalogSnapshot> Current
    {
        get
        {
            var publisher = Volatile.Read(ref _publisher);
            return publisher.Current;
        }
    }

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot)
    {
        var current = Current;
        return current.Value.TryGet(instanceId, out snapshot);
    }

    internal long BeginReconciliation()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_mode == MirrorMode.Closed, this);
            _generation = checked(_generation + 1);
            _buffer.Clear();
            ClearHistory();
            _mode = MirrorMode.AwaitingSnapshot;
            return _generation;
        }
    }

    internal bool IsCurrentGeneration(long generation)
    {
        lock (_gate)
        {
            return _mode != MirrorMode.Closed && generation == _generation;
        }
    }

    internal RemoteInstanceCatalogTransition ReceiveChange(
        long generation,
        InstanceCatalogChangedEventData change)
    {
        lock (_gate)
        {
            if (_mode == MirrorMode.Closed)
                return RemoteInstanceCatalogTransition.Closed;

            if (generation != _generation)
                return RemoteInstanceCatalogTransition.IgnoredStaleGeneration;

            if (_mode == MirrorMode.NeedsResync)
                return RemoteInstanceCatalogTransition.NeedsResync;

            if (!TryCreateFingerprint(change, out var fingerprint))
                return EnterNeedsResync();

            if (_mode == MirrorMode.AwaitingSnapshot)
                return BufferChange(fingerprint);

            return ApplyReadyChange(fingerprint);
        }
    }

    internal RemoteInstanceCatalogTransition ApplyFullSnapshot(
        long generation,
        InstanceCatalogResult fullSnapshot)
    {
        lock (_gate)
        {
            if (_mode == MirrorMode.Closed)
                return RemoteInstanceCatalogTransition.Closed;

            if (generation != _generation)
                return RemoteInstanceCatalogTransition.IgnoredStaleGeneration;

            if (_mode == MirrorMode.NeedsResync)
                return RemoteInstanceCatalogTransition.NeedsResync;

            if (_mode != MirrorMode.AwaitingSnapshot ||
                !TryCreateCatalog(fullSnapshot, out var candidate, out var version))
            {
                return EnterNeedsResync();
            }

            var replayedHistory = new List<ChangeFingerprint>();
            var expectedVersion = version;
            foreach (var pair in _buffer)
            {
                var change = pair.Value;
                if (change.Version < version)
                    continue;

                if (change.Version == version)
                {
                    if (!IsEffectRepresented(candidate, change))
                        return EnterNeedsResync();

                    replayedHistory.Add(change);
                    continue;
                }

                if (expectedVersion == long.MaxValue || change.Version != expectedVersion + 1)
                    return EnterNeedsResync();

                if (!TryApplyChange(candidate, change, out candidate))
                    return EnterNeedsResync();

                expectedVersion = change.Version;
                replayedHistory.Add(change);
            }

            var replacement = CreatePublisher(expectedVersion, candidate);
            _buffer.Clear();
            ClearHistory();
            foreach (var fingerprint in replayedHistory)
                Remember(fingerprint);

            Volatile.Write(ref _publisher, replacement);
            _mode = MirrorMode.Ready;
            return RemoteInstanceCatalogTransition.Ready;
        }
    }

    internal void Close()
    {
        lock (_gate)
        {
            if (_mode == MirrorMode.Closed)
                return;

            _buffer.Clear();
            ClearHistory();
            _mode = MirrorMode.Closed;
        }
    }

    private RemoteInstanceCatalogTransition BufferChange(ChangeFingerprint change)
    {
        if (_buffer.TryGetValue(change.Version, out var existing))
        {
            return existing == change
                ? RemoteInstanceCatalogTransition.IgnoredDuplicate
                : EnterNeedsResync();
        }

        if (_buffer.Count >= MaximumBufferedChanges)
            return EnterNeedsResync();

        _buffer.Add(change.Version, change);
        return RemoteInstanceCatalogTransition.Buffered;
    }

    private RemoteInstanceCatalogTransition ApplyReadyChange(ChangeFingerprint change)
    {
        var current = _publisher.Current;
        if (change.Version <= current.Version)
        {
            if (_history.TryGetValue(change.Version, out var remembered))
            {
                return remembered == change
                    ? RemoteInstanceCatalogTransition.IgnoredDuplicate
                    : EnterNeedsResync();
            }

            if (change.Version == current.Version && IsEffectRepresented(current.Value, change))
            {
                Remember(change);
                return RemoteInstanceCatalogTransition.IgnoredDuplicate;
            }

            return EnterNeedsResync();
        }

        if (current.Version == long.MaxValue || change.Version != current.Version + 1)
            return EnterNeedsResync();

        if (!TryApplyChange(current.Value, change, out var candidate))
            return EnterNeedsResync();

        _publisher.Publish(change.Version, candidate);
        Remember(change);
        return RemoteInstanceCatalogTransition.Applied;
    }

    private RemoteInstanceCatalogTransition EnterNeedsResync()
    {
        _buffer.Clear();
        _mode = MirrorMode.NeedsResync;
        return RemoteInstanceCatalogTransition.NeedsResync;
    }

    private static StatePublisher<InstanceCatalogSnapshot> CreatePublisher(
        long version,
        InstanceCatalogSnapshot value)
    {
        if (version == 0)
            return new StatePublisher<InstanceCatalogSnapshot>(value);

        var publisher = new StatePublisher<InstanceCatalogSnapshot>(InstanceCatalogSnapshot.Empty);
        publisher.Publish(version, value);
        return publisher;
    }

    private static bool TryCreateCatalog(
        InstanceCatalogResult? result,
        out InstanceCatalogSnapshot catalog,
        out long version)
    {
        catalog = InstanceCatalogSnapshot.Empty;
        version = 0;
        if (result is null || result.Version < 0 || result.Items.IsDefault)
            return false;

        var builder = ImmutableDictionary.CreateBuilder<Guid, InstanceSnapshot>();
        foreach (var item in result.Items)
        {
            if (!TryCreateSnapshot(item, out var snapshot) || !builder.TryAdd(snapshot.Id, snapshot))
                return false;
        }

        catalog = new InstanceCatalogSnapshot(builder);
        version = result.Version;
        return true;
    }

    private static bool TryCreateFingerprint(
        InstanceCatalogChangedEventData? change,
        out ChangeFingerprint fingerprint)
    {
        fingerprint = default;
        if (change is null || change.Version < 0 || change.InstanceId == Guid.Empty ||
            !Enum.IsDefined(change.Operation))
        {
            return false;
        }

        SnapshotFingerprint? snapshot = null;
        switch (change.Operation)
        {
            case InstanceCatalogChangeOperation.Upsert:
                if (!TryCreateSnapshotFingerprint(change.Snapshot, out var upsertSnapshot) ||
                    upsertSnapshot.Id != change.InstanceId)
                {
                    return false;
                }

                snapshot = upsertSnapshot;
                break;

            case InstanceCatalogChangeOperation.Remove:
                if (change.Snapshot is not null)
                    return false;
                break;

            default:
                return false;
        }

        fingerprint = new ChangeFingerprint(
            change.Version,
            change.Operation,
            change.InstanceId,
            snapshot);
        return true;
    }

    private static bool TryCreateSnapshot(
        InstanceCatalogItem? item,
        out InstanceSnapshot snapshot)
    {
        snapshot = null!;
        if (!TryCreateSnapshotFingerprint(item, out var fingerprint))
            return false;

        snapshot = fingerprint.ToSnapshot();
        return true;
    }

    private static bool TryCreateSnapshotFingerprint(
        InstanceCatalogItem? item,
        out SnapshotFingerprint fingerprint)
    {
        fingerprint = default;
        if (item is null || item.InstanceId == Guid.Empty || item.Name is null || item.Version is null ||
            !Enum.IsDefined(item.InstanceType) || !Enum.IsDefined(item.Status))
        {
            return false;
        }

        fingerprint = new SnapshotFingerprint(
            item.InstanceId,
            item.Name,
            item.InstanceType,
            item.Version,
            item.Status);
        return true;
    }

    private static bool TryApplyChange(
        InstanceCatalogSnapshot current,
        ChangeFingerprint change,
        out InstanceCatalogSnapshot candidate)
    {
        switch (change.Operation)
        {
            case InstanceCatalogChangeOperation.Upsert when change.Snapshot is { } fingerprint:
                candidate = new InstanceCatalogSnapshot(
                    current.Instances.SetItem(change.InstanceId, fingerprint.ToSnapshot()));
                return true;

            case InstanceCatalogChangeOperation.Remove when current.Instances.ContainsKey(change.InstanceId):
                candidate = new InstanceCatalogSnapshot(current.Instances.Remove(change.InstanceId));
                return true;

            default:
                candidate = current;
                return false;
        }
    }

    private static bool IsEffectRepresented(
        InstanceCatalogSnapshot current,
        ChangeFingerprint change)
    {
        return change.Operation switch
        {
            InstanceCatalogChangeOperation.Upsert when change.Snapshot is { } snapshot =>
                current.TryGet(change.InstanceId, out var existing) && existing == snapshot.ToSnapshot(),
            InstanceCatalogChangeOperation.Remove => !current.Instances.ContainsKey(change.InstanceId),
            _ => false
        };
    }

    private void Remember(ChangeFingerprint fingerprint)
    {
        if (_history.TryGetValue(fingerprint.Version, out var existing))
        {
            if (existing != fingerprint)
                throw new InvalidOperationException("A catalog version cannot have two applied fingerprints.");
            return;
        }

        _history.Add(fingerprint.Version, fingerprint);
        _historyOrder.Enqueue(fingerprint.Version);
        if (_historyOrder.Count <= MaximumFingerprintHistory)
            return;

        var evictedVersion = _historyOrder.Dequeue();
        _history.Remove(evictedVersion);
    }

    private void ClearHistory()
    {
        _history.Clear();
        _historyOrder.Clear();
    }

    private enum MirrorMode
    {
        AwaitingSnapshot,
        Ready,
        NeedsResync,
        Closed
    }

    private readonly record struct ChangeFingerprint(
        long Version,
        InstanceCatalogChangeOperation Operation,
        Guid InstanceId,
        SnapshotFingerprint? Snapshot);

    private readonly record struct SnapshotFingerprint(
        Guid Id,
        string Name,
        Common.ProtoType.Instance.InstanceType InstanceType,
        string Version,
        Common.ProtoType.Instance.InstanceStatus Status)
    {
        internal InstanceSnapshot ToSnapshot() => new(Id, Name, InstanceType, Version, Status);
    }
}
