using System.Threading;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Daemon.ApplicationCore;

/// <summary>
/// Owns the daemon's immutable, copy-on-write public instance catalog.
/// </summary>
internal sealed class AuthoritativeInstanceSnapshotSource : IInstanceSnapshotSource
{
    private readonly Lock _publicationLock = new();
    private readonly StatePublisher<InstanceCatalogSnapshot> _publisher;
    private readonly InstanceCatalogCommitFeed _commitFeed;

    public AuthoritativeInstanceSnapshotSource(
        IEnumerable<KeyValuePair<Guid, IInstance>> instances,
        InstanceCatalogCommitFeed commitFeed)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(commitFeed);
        _publisher = new StatePublisher<InstanceCatalogSnapshot>(CreateCatalog(instances));
        _commitFeed = commitFeed;
    }

    public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;

    public bool TryGet(Guid instanceId, out InstanceSnapshot snapshot) =>
        Current.Value.TryGet(instanceId, out snapshot);

    internal void Upsert(IInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var snapshot = CreateSnapshot(instance);
        lock (_publicationLock)
        {
            var current = _publisher.Current.Value;
            if (current.TryGet(snapshot.Id, out var existing) && existing == snapshot)
                return;

            _commitFeed.Publish(() =>
            {
                var published = _publisher.Update(catalog =>
                    new InstanceCatalogSnapshot(catalog.Instances.SetItem(snapshot.Id, snapshot)));
                return new InstanceCatalogCommit(
                    published.Version,
                    InstanceCatalogChangeOperation.Upsert,
                    snapshot.Id,
                    snapshot);
            });
        }
    }

    internal void Remove(Guid instanceId)
    {
        lock (_publicationLock)
        {
            var current = _publisher.Current.Value;
            if (!current.Instances.ContainsKey(instanceId))
                return;

            _commitFeed.Publish(() =>
            {
                var published = _publisher.Update(catalog =>
                    new InstanceCatalogSnapshot(catalog.Instances.Remove(instanceId)));
                return new InstanceCatalogCommit(
                    published.Version,
                    InstanceCatalogChangeOperation.Remove,
                    instanceId,
                    null);
            });
        }
    }

    private static InstanceCatalogSnapshot CreateCatalog(IEnumerable<KeyValuePair<Guid, IInstance>> instances)
    {
        return new InstanceCatalogSnapshot(instances.Select(pair =>
            new KeyValuePair<Guid, InstanceSnapshot>(pair.Key, CreateSnapshot(pair.Value))));
    }

    private static InstanceSnapshot CreateSnapshot(IInstance instance)
    {
        var config = instance.Config;
        return new InstanceSnapshot(
            config.Uuid,
            config.Name,
            config.InstanceType,
            config.Version,
            instance.Status);
    }
}
