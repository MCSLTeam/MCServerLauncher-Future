using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.Throwing;

public sealed class ThrowingPlugin : IDaemonPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) =>
        throw new InvalidOperationException("The throwing fixture fails during configuration.");

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
