using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.Throwing;

public sealed class ThrowingPlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "fixture.throwing",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.PluginFixtures.Throwing.ThrowingPlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "f4136e29fc02496bd82c42281310e2f68dc716259af5c50b1d1c37ff32e9373c");

    public Result<Unit, DaemonError> Configure(IPluginContext context) =>
        throw new InvalidOperationException("The throwing fixture fails during configuration.");

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
