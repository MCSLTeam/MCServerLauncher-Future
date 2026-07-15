using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.StartReturnedError;

public sealed class StartReturnedErrorPlugin : IDaemonPlugin
{
    private IPluginContext? _context;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_context!.Errors.Fail(
            "fixture_start_returned_error",
            "The start-returned-error fixture rejects startup."));

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
