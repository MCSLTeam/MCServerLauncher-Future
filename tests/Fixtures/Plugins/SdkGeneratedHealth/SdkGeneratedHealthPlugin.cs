using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugin.Sdk;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.SdkGeneratedHealth;

[DaemonPluginModule]
public partial class SdkGeneratedHealthPlugin
{
    public Result<Unit, DaemonError> Configure(IPluginContext context, SdkGeneratedHealthPluginFeatures features)
    {
        _ = features.Context;
        _ = features.Rpc;
        _ = features.Events;
        _ = features.Instances;
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());
}
