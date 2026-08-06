using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.metadata-malformed",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.MetadataMalformed.MetadataMalformedPlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\n\nrpc.register",
    "c2353bbd44ee217a06c6c01627d07cdaab2f166f0f8f4fccd308b8d5c9053043")]

namespace MCServerLauncher.PluginFixtures.MetadataMalformed;

public sealed class MetadataMalformedPlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
