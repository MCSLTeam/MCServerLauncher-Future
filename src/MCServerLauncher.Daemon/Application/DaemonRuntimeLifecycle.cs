using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal interface IDaemonRuntimeLifecycle
{
    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class LocalDaemonRuntimeLifecycle(
    FileSessionCoordinator fileSessionCoordinator,
    IInstanceManager instanceManager,
    ILogger<LocalDaemonRuntimeLifecycle> logger) : IDaemonRuntimeLifecycle
{
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Closing file sessions");
        await fileSessionCoordinator.StopAsync();

        logger.LogDebug("Stopping instances");
        await instanceManager.StopAllInstances(cancellationToken);
    }
}
