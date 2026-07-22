using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.PackageReferenceConsumer;

[DaemonPluginModule]
public partial class PackageReferenceConsumerPlugin
{
    public void ConfigureServices(
        IServiceCollection services,
        PackageReferenceConsumerPluginFeatures features)
    {
        _ = services;
        var registration = features.Rpc.Register(
            "ping",
            BuiltInProtocolJsonContext.Default.EmptyRequest,
            BuiltInProtocolJsonContext.Default.UnitResult,
            new RpcDocumentation(
                "fixture.package-reference-consumer",
                "Package reference ping",
                "Proves the published package consumer was generated and loaded through Plugin.Sdk.",
                "fixture.empty-request",
                "fixture.unit-result"),
            static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
        if (registration.IsErr(out var error))
            throw new InvalidOperationException($"Could not register the package-reference fixture RPC: {error!.Code}");
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
