using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.throwing",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.Throwing.ThrowingPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "2ddc6b4e5abe9894151398c6abc067f94c325abd7af4c43f416ab996131b7fa2")]

namespace MCServerLauncher.PluginFixtures.Throwing;

public sealed class ThrowingPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) =>
        throw new InvalidOperationException("The throwing fixture fails during configuration.");

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
