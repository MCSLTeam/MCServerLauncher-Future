using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInExtensionDeploymentRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IExtensionDeploymentApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<ExtensionDaemonBundleInstallRequest, ExtensionDaemonBundleDeploymentResult>(
            builder,
            "mcsl.extension.daemon.bundle.install",
            async (request, token) => BuiltInApplicationRpcExecution.FromResult(
                await application.InstallDaemonBundleAsync(request, token).ConfigureAwait(false)));
        Register<ExtensionDaemonBundleRemoveRequest, ExtensionDaemonBundleDeploymentResult>(
            builder,
            "mcsl.extension.daemon.bundle.remove",
            async (request, token) => BuiltInApplicationRpcExecution.FromResult(
                await application.RemoveDaemonBundleAsync(request, token).ConfigureAwait(false)));
    }

    private static void Register<TRequest, TResult>(
        ProtocolCatalogBuilder builder,
        string method,
        Func<TRequest, CancellationToken, Task<ProtocolRpcExecution<TResult>>> handler)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, method));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, request, token) => handler(request, token)));
    }
}
