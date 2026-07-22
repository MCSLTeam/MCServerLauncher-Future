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

        Register<OperationListQuery, OperationListResult>(builder, "mcsl.operation.list", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: true, out var subject, out var error))
                return ProtocolRpcExecution<OperationListResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ListOperationsAsync(bound, token).ConfigureAwait(false));
        });
        Register<OperationReference, OperationSnapshot>(builder, "mcsl.operation.get", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: true, out var subject, out var error))
                return ProtocolRpcExecution<OperationSnapshot>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.GetOperationAsync(bound, token).ConfigureAwait(false));
        });
        Register<OperationCancelRequest, OperationCancelResult>(builder, "mcsl.operation.cancel", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: true, out var subject, out var error))
                return ProtocolRpcExecution<OperationCancelResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.CancelOperationAsync(bound, token).ConfigureAwait(false));
        });
    }

    private static void Register<TRequest, TResult>(
        ProtocolCatalogBuilder builder,
        string method,
        ProtocolRpcHandler<TRequest, TResult> handler)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, method));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(
                ProtocolExecutionOwner.BuiltIn,
                handler));
    }
}
