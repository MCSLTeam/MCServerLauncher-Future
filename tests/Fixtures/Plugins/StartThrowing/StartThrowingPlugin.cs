using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.StartThrowing;

public sealed class StartThrowingPlugin : IDaemonPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The start-throwing fixture fails during startup.");

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
