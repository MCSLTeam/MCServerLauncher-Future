using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-returned-error",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartReturnedError.StartReturnedErrorPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "e61bec6f93b774b29283b2e75bb57266d03c6c57649bdce76c1b271a533b6348")]

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
