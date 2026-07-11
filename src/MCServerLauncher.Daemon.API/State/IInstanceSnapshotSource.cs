namespace MCServerLauncher.Daemon.API.State;

/// <summary>
/// Provides lock-free access to the current immutable instance catalog.
/// </summary>
public interface IInstanceSnapshotSource
{
    PublishedState<InstanceCatalogSnapshot> Current { get; }

    bool TryGet(Guid instanceId, out InstanceSnapshot snapshot);
}
