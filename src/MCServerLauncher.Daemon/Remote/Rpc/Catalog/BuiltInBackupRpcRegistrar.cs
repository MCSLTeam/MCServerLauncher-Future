using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInBackupRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IBackupApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        // Every method binds the connection subject with useGlobalOwnerForMainToken: false, matching
        // provisioning. Honouring the main-token wildcard here would let it confirm and execute
        // another principal's destructive restore plan.
        Register<BackupListQuery, BackupListResult>(builder, "mcsl.backup.list", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<BackupListResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ListAsync(bound, token).ConfigureAwait(false));
        });
        Register<BackupCreateRequest, BackupCreateResult>(builder, "mcsl.backup.create", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<BackupCreateResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.CreateAsync(bound, token).ConfigureAwait(false));
        });
        Register<BackupPruneRequest, BackupPruneResult>(builder, "mcsl.backup.prune", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<BackupPruneResult>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.PruneAsync(bound, token).ConfigureAwait(false));
        });
        Register<BackupRestorePlanRequest, ProvisioningPlanSnapshot>(builder, "mcsl.backup.restore.plan", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningPlanSnapshot>.Err(error!);
            var bound = request with { CreatorPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.PlanRestoreAsync(bound, token).ConfigureAwait(false));
        });
        Register<BackupRestoreConfirmRequest, ProvisioningPlanSnapshot>(builder, "mcsl.backup.restore.confirm", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningPlanSnapshot>.Err(error!);
            var bound = request with { ConfirmerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ConfirmRestoreAsync(bound, token).ConfigureAwait(false));
        });
        Register<BackupRestoreExecuteRequest, BackupRestoreExecuteResult>(builder, "mcsl.backup.restore.execute", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<BackupRestoreExecuteResult>.Err(error!);
            var bound = request with { ExecutorPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ExecuteRestoreAsync(bound, token).ConfigureAwait(false));
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
