using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInOperationRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IOperationApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<OperationListQuery, OperationListResult>(builder, "mcsl.operation.list", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ListOperationsAsync(request, token).ConfigureAwait(false)));
        Register<OperationReference, OperationSnapshot>(builder, "mcsl.operation.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetOperationAsync(request, token).ConfigureAwait(false)));
        Register<OperationCancelRequest, OperationCancelResult>(builder, "mcsl.operation.cancel", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.CancelOperationAsync(request, token).ConfigureAwait(false)));
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
