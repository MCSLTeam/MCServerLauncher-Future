using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-malformed",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.MetadataMalformed.MetadataMalformedPlugin",
    "[2.0.0, 3.0.0)",
    "event.publish\n\nrpc.register",
    "0000000000000000000000000000000000000000000000000000000000000000")]

namespace MCServerLauncher.PluginFixtures.MetadataMalformed;

public sealed class MetadataMalformedPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
