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
        // Shutdown / dispose may run before lifecycle Start (e.g. ServeAsync aborted before
        // host.StartAsync). Treat never-started as already drained so cleanup stays idempotent.
        Task? runTask;
        lock (_gate)
        {
            runTask = _runTask;
        }

        if (runTask is null)
            return;

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
