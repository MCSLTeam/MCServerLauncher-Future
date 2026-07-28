using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInAuditRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IAuditApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        // The main token queries the full history ("*"); every other subject sees only records of
        // its own mutations, matching the operation-list visibility convention.
        Register<AuditQuery, AuditQueryResult>(builder, "mcsl.audit.query", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: true, out var subject, out var error))
                return ProtocolRpcExecution<AuditQueryResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.QueryAsync(bound, token).ConfigureAwait(false));
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
