using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.start-throwing",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartThrowing.StartThrowingPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "5ef74201b140943c67cb36368c707124dc33bc1b57ad580601a6b606a3b88f4e")]

namespace MCServerLauncher.PluginFixtures.StartThrowing;

public sealed class StartThrowingPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The start-throwing fixture fails during startup.");

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
