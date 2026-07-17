using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.Daemon.Remote.Rpc.Events;

internal enum V2EventConnectionAttachResult
{
    Attached,
    DuplicateConnectionId,
    ConnectionClosed
}

internal sealed class V2EventConnectionRegistry
{
    private readonly ConcurrentDictionary<string, V2EventConnectionEntry> _entries =
        new(StringComparer.Ordinal);
    private readonly object _snapshotGate = new();
    private readonly FrozenProtocolCatalog _catalog;
    private V2EventConnectionEntry[] _activeSnapshot = [];
    private ImmutableDictionary<FrozenEventBinding, ImmutableArray<V2EventConnectionEntry>> _subscriberSnapshot =
        ImmutableDictionary.Create<FrozenEventBinding, ImmutableArray<V2EventConnectionEntry>>(
            ReferenceEqualityComparer.Instance);

    internal V2EventConnectionRegistry(FrozenProtocolCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    internal V2EventConnectionAttachResult TryAttach(
        string connectionId,
        V2ConnectionOwner owner,
        out V2EventConnectionEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(owner);

        var candidate = new V2EventConnectionEntry(connectionId, owner, _catalog, this);
        if (!owner.TryRegisterCleanup(candidate))
        {
            candidate.Close();
            entry = null;
            return V2EventConnectionAttachResult.ConnectionClosed;
        }

        if (!_entries.TryAdd(connectionId, candidate))
        {
            candidate.Close();
            owner.TryUnregisterCleanup(candidate);
            entry = null;
            return V2EventConnectionAttachResult.DuplicateConnectionId;
        }

        if (!TryAddActiveSnapshot(candidate))
        {
            Remove(candidate);
            entry = null;
            return V2EventConnectionAttachResult.ConnectionClosed;
        }

        entry = candidate;
        return V2EventConnectionAttachResult.Attached;
    }

    internal ImmutableArray<V2EventConnectionEntry> Snapshot() =>
        ImmutableCollectionsMarshal.AsImmutableArray(Volatile.Read(ref _activeSnapshot));

    /// <summary>
    /// Returns the copy-on-write candidate set for a frozen event binding. Entries retain their
    /// own ledger match check so exact metadata filters and close races preserve their semantics.
    /// </summary>
    internal ImmutableArray<V2EventConnectionEntry> Snapshot(FrozenEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var snapshot = Volatile.Read(ref _subscriberSnapshot);
        return snapshot.TryGetValue(binding, out var entries) ? entries : [];
    }

    internal int RawEntryCount => _entries.Count;

    internal bool TryGet(string connectionId, out V2EventConnectionEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (_entries.TryGetValue(connectionId, out var found) && !found.IsClosed)
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    private bool TryAddActiveSnapshot(V2EventConnectionEntry entry)
    {
        lock (_snapshotGate)
        {
            if (entry.IsClosed ||
                !_entries.TryGetValue(entry.ConnectionId, out var current) ||
                !ReferenceEquals(current, entry))
            {
                return false;
            }

            var snapshot = Volatile.Read(ref _activeSnapshot);
            var next = new V2EventConnectionEntry[snapshot.Length + 1];
            Array.Copy(snapshot, next, snapshot.Length);
            next[^1] = entry;
            Volatile.Write(ref _activeSnapshot, next);
            return true;
        }
    }

    private void UpdateSubscriptionSnapshot(
        V2EventConnectionEntry entry,
        FrozenEventBinding binding,
        bool subscribed)
    {
        lock (_snapshotGate)
        {
            var snapshot = Volatile.Read(ref _subscriberSnapshot);
            if (subscribed)
            {
                if (entry.IsClosed ||
                    !_entries.TryGetValue(entry.ConnectionId, out var current) ||
                    !ReferenceEquals(current, entry))
                {
                    return;
                }

                var entries = snapshot.TryGetValue(binding, out var currentEntries) ? currentEntries : [];
                if (entries.IndexOf(entry) >= 0)
                    return;

                Volatile.Write(
                    ref _subscriberSnapshot,
                    snapshot.SetItem(binding, entries.Add(entry)));
                return;
            }

            if (!snapshot.TryGetValue(binding, out var existing))
                return;

            var index = existing.IndexOf(entry);
            if (index < 0)
                return;

            var next = existing.RemoveAt(index);
            Volatile.Write(
                ref _subscriberSnapshot,
                next.IsEmpty
                    ? snapshot.Remove(binding)
                    : snapshot.SetItem(binding, next));
        }
    }

    private void Remove(V2EventConnectionEntry entry)
    {
        _entries.TryRemove(new KeyValuePair<string, V2EventConnectionEntry>(entry.ConnectionId, entry));
        lock (_snapshotGate)
        {
            var active = Volatile.Read(ref _activeSnapshot);
            var activeIndex = Array.IndexOf(active, entry);
            if (activeIndex >= 0)
            {
                var next = new V2EventConnectionEntry[active.Length - 1];
                if (activeIndex > 0)
                    Array.Copy(active, 0, next, 0, activeIndex);
                if (activeIndex < active.Length - 1)
                    Array.Copy(active, activeIndex + 1, next, activeIndex, active.Length - activeIndex - 1);
                Volatile.Write(ref _activeSnapshot, next);
            }

            // Closing the ledger synchronously retracts every binding through
            // UpdateSubscriptionSnapshot before it reaches this method. Do not
            // rescan every event binding here: disconnect churn must stay
            // proportional to this entry's active snapshot removal.
        }
    }

    internal sealed class V2EventConnectionEntry : IV2ConnectionCleanup
    {
        private readonly V2EventConnectionRegistry _registry;
        private int _closed;
        private int _cleanupCount;

        internal V2EventConnectionEntry(
            string connectionId,
            V2ConnectionOwner owner,
            FrozenProtocolCatalog catalog,
            V2EventConnectionRegistry registry)
        {
            ConnectionId = connectionId;
            Owner = owner;
            _registry = registry;
            Ledger = new V2EventSubscriptionLedger(
                catalog,
                owner,
                V2EventMetaCanonicalizer.Instance,
                (binding, subscribed) => _registry.UpdateSubscriptionSnapshot(this, binding, subscribed));
        }

        internal V2EventConnectionEntry(
            string connectionId,
            V2ConnectionOwner owner,
            V2EventSubscriptionLedger ledger,
            V2EventConnectionRegistry registry)
        {
            ConnectionId = connectionId;
            Owner = owner;
            Ledger = ledger;
            _registry = registry;
        }

        internal string ConnectionId { get; }

        internal V2ConnectionOwner Owner { get; }

        internal V2EventSubscriptionLedger Ledger { get; }

        internal bool IsClosed => Volatile.Read(ref _closed) != 0;

        internal int CleanupCount => Volatile.Read(ref _cleanupCount);

        public ValueTask CleanupAsync(CancellationToken cancellationToken)
        {
            Close();
            return ValueTask.CompletedTask;
        }

        internal void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            Interlocked.Increment(ref _cleanupCount);
            Ledger.Close();
            _registry.Remove(this);
        }
    }
}
