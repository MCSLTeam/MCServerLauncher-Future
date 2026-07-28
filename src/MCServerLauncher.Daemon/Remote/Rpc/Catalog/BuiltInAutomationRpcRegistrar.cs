using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInAutomationRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IAutomationApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        // Policies are daemon-global admin objects: permission-gated, never owner-scoped. The
        // intent confirm/execute pair binds the connection subject with
        // useGlobalOwnerForMainToken: false like backup restore — a confirmation identity must be
        // a real subject, never the admin wildcard.
        Register<EmptyRequest, AutomationGetResult>(builder, "mcsl.automation.get", async (_, _, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetAsync(token).ConfigureAwait(false)));
        Register<AutomationValidateRequest, AutomationValidateResult>(builder, "mcsl.automation.validate", async (_, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ValidateAsync(request, token).ConfigureAwait(false)));
        Register<EmptyRequest, AutomationTestResult>(builder, "mcsl.automation.test", async (_, _, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.TestAsync(token).ConfigureAwait(false)));
        Register<AutomationApplyRequest, AutomationApplyResult>(builder, "mcsl.automation.apply", async (_, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ApplyAsync(request, token).ConfigureAwait(false)));
        Register<AutomationEnableRequest, AutomationApplyResult>(builder, "mcsl.automation.enable", async (_, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.EnableAsync(request, token).ConfigureAwait(false)));
        Register<AutomationIntentConfirmRequest, ProvisioningPlanSnapshot>(builder, "mcsl.automation.intent.confirm", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<ProvisioningPlanSnapshot>.Err(error!);
            var bound = request with { ConfirmerPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ConfirmIntentAsync(bound, token).ConfigureAwait(false));
        });
        Register<AutomationIntentExecuteRequest, AutomationIntentExecuteResult>(builder, "mcsl.automation.intent.execute", async (context, request, token) =>
        {
            if (!RpcCallerSubjectResolver.TryResolve(context, useGlobalOwnerForMainToken: false, out var subject, out var error))
                return ProtocolRpcExecution<AutomationIntentExecuteResult>.Err(error!);
            var bound = request with { ExecutorPrincipal = subject };
            return BuiltInApplicationRpcExecution.FromResult(await application.ExecuteIntentAsync(bound, token).ConfigureAwait(false));
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
