using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-throwing",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartThrowing.StartThrowingPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "5c5a8443d184a5ac1e7fb64c521081df09ded8886bb29b9cfbd02cabbbde6477")]

namespace MCServerLauncher.PluginFixtures.StartThrowing;

public sealed class StartThrowingPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The start-throwing fixture fails during startup.");

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
