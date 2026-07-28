using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInMonitoringRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IMonitoringApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        // Metrics are system-wide observations like mcsl.system.info.get: permission-gated but
        // not owner-bound.
        Register<EmptyRequest, MonitoringCurrentResult>(builder, "mcsl.monitoring.current.get", async (_, _, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetCurrentAsync(token).ConfigureAwait(false)));
        Register<MonitoringQuery, MonitoringQueryResult>(builder, "mcsl.monitoring.query", async (_, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.QueryAsync(request, token).ConfigureAwait(false)));
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
