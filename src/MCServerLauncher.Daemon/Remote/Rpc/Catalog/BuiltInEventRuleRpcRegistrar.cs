using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInEventRuleRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IEventRuleApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<EventRuleQuery, EventRuleSet>(builder, "mcsl.instance.event-rules.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetEventRulesAsync(request, token).ConfigureAwait(false)));
        Register<EventRuleUpdateRequest, UnitResult>(builder, "mcsl.instance.event-rules.update", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.UpdateEventRulesAsync(request, token).ConfigureAwait(false)));
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
