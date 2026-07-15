using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.PackageReferenceConsumer;

public sealed class PackageReferenceConsumerPlugin : IDaemonPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        context.Logger.LogInformation("fixture.package_reference_consumer.configure");
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
