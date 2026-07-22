using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-returned-error",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartReturnedError.StartReturnedErrorPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "7e39b20218c299fa1569d82ff0f11575f451305ca2d19614193e420c1148721a")]

namespace MCServerLauncher.PluginFixtures.StartReturnedError;

public sealed class StartReturnedErrorPlugin : IGeneratedDaemonPluginAdapter
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
