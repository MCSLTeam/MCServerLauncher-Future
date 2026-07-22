using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal sealed class InstanceCatalogDomainEventBridge(
    InstanceCatalogCommitFeed feed,
    IDomainEventPort domainEvents)
{
    private readonly object _gate = new();
    private Task? _runTask;

    internal void Start()
    {
        TaskCompletionSource? startSignal = null;
        lock (_gate)
        {
            if (_runTask is not null)
                return;

            startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _runTask = RunAfterStartAsync(startSignal.Task);
        }

        startSignal.SetResult();
    }

    internal async Task DrainAsync()
    {
        Task runTask;
        lock (_gate)
        {
            runTask = _runTask ?? throw new InvalidOperationException(
                "The instance catalog domain-event bridge must be started before it can be drained.");
        }

        await runTask;
    }

    private async Task RunAsync()
    {
        await foreach (var commit in feed.ReadAllAsync())
        {
            var snapshot = commit.Snapshot is null
                ? null
                : new InstanceCatalogItem(
                    commit.Snapshot.Id,
                    commit.Snapshot.Name,
                    commit.Snapshot.InstanceType,
                    commit.Snapshot.Version,
                    commit.Snapshot.Status,
                    commit.Snapshot.ReadyTimedOut);
            await domainEvents.PublishAsync(
                new InstanceCatalogChangedDomainEvent(
                    new InstanceCatalogChangedEventData(
                        commit.Version,
                        commit.Operation,
                        commit.InstanceId,
                        snapshot)),
                CancellationToken.None);
        }
    }

    private async Task RunAfterStartAsync(Task startSignal)
    {
        await startSignal;
        await RunAsync();
    }
}
