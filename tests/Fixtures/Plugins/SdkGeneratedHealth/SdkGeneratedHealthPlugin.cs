using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.SdkGeneratedHealth;

[DaemonPluginModule]
public partial class SdkGeneratedHealthPlugin
{
    public void ConfigureServices(IServiceCollection services, SdkGeneratedHealthPluginFeatures features)
    {
        _ = services;
        _ = features.Context;
        _ = features.Rpc;
        _ = features.Events;
        _ = features.Instances;
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());
}
