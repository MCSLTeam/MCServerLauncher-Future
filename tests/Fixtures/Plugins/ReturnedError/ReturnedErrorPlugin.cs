using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.ReturnedError;

public sealed class ReturnedErrorPlugin : IDaemonPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) =>
        context.Errors.Fail("fixture_returned_error", "The returned-error fixture rejects configuration.");

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
