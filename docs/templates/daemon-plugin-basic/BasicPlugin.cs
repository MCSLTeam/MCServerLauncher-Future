using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace Community.Example.BasicPlugin;

[DaemonPluginModule]
public partial class BasicPlugin
{
    public void ConfigureServices(IServiceCollection services, BasicPluginFeatures features)
    {
        var registered = features.Rpc.Register(
            "ping",
            BuiltInProtocolJsonContext.Default.EmptyRequest,
            BuiltInProtocolJsonContext.Default.UnitResult,
            new RpcDocumentation(
                "community.example.basic",
                "Ping",
                "Returns Unit when the plugin is active.",
                "example.empty-request",
                "example.unit-result"),
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));

        if (registered.IsErr(out var error))
            throw new InvalidOperationException(error!.Message);
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
