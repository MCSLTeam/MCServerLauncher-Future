using System.Threading;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.ApplicationCore.Monitoring;
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
    private readonly InstanceLifecycleEventBuffer? _lifecycleEvents;

    public AuthoritativeInstanceSnapshotSource(
        IEnumerable<KeyValuePair<Guid, IInstance>> instances,
        InstanceCatalogCommitFeed commitFeed,
        InstanceLifecycleEventBuffer? lifecycleEvents = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(commitFeed);
        _publisher = new StatePublisher<InstanceCatalogSnapshot>(CreateCatalog(instances));
        _commitFeed = commitFeed;
        _lifecycleEvents = lifecycleEvents;
    }

    public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) =>
        Current.Value.TryGet(instanceId, out snapshot);

    internal void Upsert(IInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Upsert(instance, new InstanceReportFact(instance.Status, instance.ReadyTimedOut));
    }

    internal void Upsert(IInstance instance, InstanceReportFact fact)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var snapshot = CreateSnapshot(instance, fact);
        lock (_publicationLock)
        {
            var current = _publisher.Current.Value;
            if (current.TryGet(snapshot.Id, out var existing) && existing == snapshot)
                return;

            // Under the publication lock, where both states are already in hand. This is the only
            // point that observes every transition: a sampler comparing its own ticks sees the net
            // change and loses everything in between.
            _lifecycleEvents?.Record(existing, snapshot);

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
        return CreateSnapshot(instance, new InstanceReportFact(instance.Status, instance.ReadyTimedOut));
    }

    private static InstanceSnapshot CreateSnapshot(IInstance instance, InstanceReportFact fact)
    {
        var config = instance.Config;
        return new InstanceSnapshot(
            config.Uuid,
            config.Name,
            config.InstanceType,
            config.Version,
            fact.Status,
            fact.ReadyTimedOut);
    }
}
