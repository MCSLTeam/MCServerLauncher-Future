using System.Collections.Concurrent;
using System.Collections.Immutable;
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
    private readonly FrozenProtocolCatalog _catalog;

    internal V2EventConnectionRegistry(FrozenProtocolCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    internal V2EventConnectionAttachResult TryAttach(
        string connectionId,
        V2ConnectionOwner owner,
        out V2EventConnectionEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(owner);

        var candidate = new V2EventConnectionEntry(connectionId, owner, new V2EventSubscriptionLedger(_catalog, owner), this);
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

        if (candidate.IsClosed)
        {
            Remove(candidate);
            entry = null;
            return V2EventConnectionAttachResult.ConnectionClosed;
        }

        entry = candidate;
        return V2EventConnectionAttachResult.Attached;
    }

    internal ImmutableArray<V2EventConnectionEntry> Snapshot() =>
        _entries.Values.Where(static entry => !entry.IsClosed).ToImmutableArray();

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

    private void Remove(V2EventConnectionEntry entry) =>
        _entries.TryRemove(new KeyValuePair<string, V2EventConnectionEntry>(entry.ConnectionId, entry));

    internal sealed class V2EventConnectionEntry : IV2ConnectionCleanup
    {
        private readonly V2EventConnectionRegistry _registry;
        private int _closed;
        private int _cleanupCount;

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
