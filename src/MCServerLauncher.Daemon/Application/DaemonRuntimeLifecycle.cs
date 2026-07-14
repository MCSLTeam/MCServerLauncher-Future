using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal interface IDaemonRuntimeLifecycle
{
    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class LocalDaemonRuntimeLifecycle(
    FileSessionCoordinator fileSessionCoordinator,
    InstanceManager instanceManager,
    InstanceMutationAdmissionGate mutationAdmission,
    DaemonReportPublisher reportPublisher,
    EventTriggerService eventTriggerService,
    InstanceDomainEventBridge instanceDomainEventBridge,
    InstanceCatalogCommitFeed instanceCatalogCommitFeed,
    InstanceCatalogDomainEventBridge instanceCatalogDomainEventBridge,
    ILogger<LocalDaemonRuntimeLifecycle> logger) : IDaemonRuntimeLifecycle
{
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Once shutdown begins, cleanup must outlive a caller that stops waiting for it.
        _ = cancellationToken;
        List<Exception> failures = [];

        logger.LogDebug("Stopping new authoritative instance mutation admission");
        await StopStepAsync(
            "stop and drain external instance mutations",
            mutationAdmission.StopExternalAdmissionAndDrainAsync,
            failures);

        logger.LogDebug("Stopping daemon event producers and rule consumers");
        reportPublisher.RequestStop();
        await StopStepAsync(
            "drain daemon report publisher",
            () => reportPublisher.StopAsync(CancellationToken.None),
            failures);
        await StopStepAsync(
            "cancel and drain event trigger service",
            () => eventTriggerService.StopAsync(CancellationToken.None),
            failures);

        logger.LogDebug("Stopping instances and draining process event pumps");
        await StopStepAsync(
            "stop instances and drain process event pumps",
            () => instanceManager.StopAllInstances(CancellationToken.None),
            failures);
        await StopStepAsync(
            "stop and drain instance status producers",
            mutationAdmission.StopProducerAdmissionAndDrainAsync,
            failures);
        await StopStepAsync(
            "detach instance process event producers",
            () =>
            {
                instanceManager.DetachInstanceEventProducers();
                return Task.CompletedTask;
            },
            failures);
        await StopStepAsync(
            "dispose instance domain-event bridge",
            () =>
            {
                instanceDomainEventBridge.Dispose();
                return Task.CompletedTask;
            },
            failures);
        await StopStepAsync(
            "stop instance catalog commit production",
            () =>
            {
                instanceCatalogCommitFeed.CompleteProduction();
                return Task.CompletedTask;
            },
            failures);
        await StopStepAsync(
            "drain instance catalog domain-event bridge",
            instanceCatalogDomainEventBridge.DrainAsync,
            failures);
        logger.LogDebug("Closing file sessions");
        await StopStepAsync("close file sessions", fileSessionCoordinator.StopAsync, failures);

        if (failures.Count != 0)
            throw new AggregateException("One or more daemon shutdown cleanup steps failed.", failures);
    }

    private async Task StopStepAsync(
        string operation,
        Func<Task> stopAsync,
        List<Exception> failures)
    {
        try
        {
            await stopAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to {Operation} during daemon shutdown", operation);
            failures.Add(exception);
        }
    }
}
