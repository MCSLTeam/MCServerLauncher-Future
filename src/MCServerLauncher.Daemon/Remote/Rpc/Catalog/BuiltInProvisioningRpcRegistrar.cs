using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInProvisioningRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IProvisioningApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<ProvisioningResolveRequest, ProvisioningPlanSnapshot>(builder, "mcsl.provisioning.resolve", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ResolveAsync(request, token).ConfigureAwait(false)));
        Register<ProvisioningPlanReference, ProvisioningPlanSnapshot>(builder, "mcsl.provisioning.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetPlanAsync(request, token).ConfigureAwait(false)));
        Register<ProvisioningExecuteRequest, ProvisioningExecuteResult>(builder, "mcsl.provisioning.execute", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ExecuteAsync(request, token).ConfigureAwait(false)));
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
