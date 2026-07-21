using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInProvisioningRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IProvisioningApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<ProvisioningResolveRequest, ProvisioningPlanSnapshot>(builder, "mcsl.provisioning.resolve", async (context, request, token) =>
        {
            if (!TryResolveSubject(context, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningPlanSnapshot>.Err(error!);
            var bound = request with { CreatorPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ResolveAsync(bound, token).ConfigureAwait(false));
        });
        Register<ProvisioningPlanReference, ProvisioningPlanSnapshot>(builder, "mcsl.provisioning.get", async (context, request, token) =>
        {
            if (!TryResolveSubject(context, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningPlanSnapshot>.Err(error!);
            var bound = request with { OwnerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.GetPlanAsync(bound, token).ConfigureAwait(false));
        });
        Register<ProvisioningExecuteRequest, ProvisioningExecuteResult>(builder, "mcsl.provisioning.execute", async (context, request, token) =>
        {
            if (!TryResolveSubject(context, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningExecuteResult>.Err(error!);
            var bound = request with { ExecutorPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ExecuteAsync(bound, token).ConfigureAwait(false));
        });
    }

    private static bool TryResolveSubject(
        ProtocolInvocationContext context,
        out string subject,
        out DaemonError? error)
    {
        subject = string.Empty;
        error = null;
        var view = context.PermissionView;
        if (view is null || string.IsNullOrWhiteSpace(view.Subject))
        {
            error = new PermissionDaemonError(
                "auth.subject_required",
                "Connection subject is required for provisioning ownership binding.");
            return false;
        }

        subject = view.Subject;
        return true;
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
