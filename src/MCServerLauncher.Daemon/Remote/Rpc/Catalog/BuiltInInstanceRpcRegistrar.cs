using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInInstanceRpcRegistrar
{
    public static void Register(ProtocolCatalogBuilder builder, IInstanceApplication application)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(application);

        Register<InstanceCommandRequest, UnitResult>(builder, "mcsl.instance.command.send", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.SendCommandAsync(request, token).ConfigureAwait(false)));
        RegisterConsole<ConsoleOpenRequest, ConsoleSession>(builder, "mcsl.instance.console.open", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await operations.OpenConsoleAsync(request, token).ConfigureAwait(false)));
        RegisterConsole<ConsoleResizeRequest, UnitResult>(builder, "mcsl.instance.console.resize", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await operations.ResizeConsoleAsync(request, token).ConfigureAwait(false)));
        RegisterConsole<ConsoleSessionReference, UnitResult>(builder, "mcsl.instance.console.close", async (operations, request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await operations.CloseConsoleAsync(request, token).ConfigureAwait(false)));
        Register<CreateInstanceRequest, CreateInstanceResult>(builder, "mcsl.instance.create", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.CreateInstanceAsync(request, token).ConfigureAwait(false)));
        Register<InstanceReference, UnitResult>(builder, "mcsl.instance.halt", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.HaltInstanceAsync(request, token).ConfigureAwait(false)));
        Register<InstanceLogQuery, InstanceLogResult>(builder, "mcsl.instance.log.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetInstanceLogAsync(request, token).ConfigureAwait(false)));
        Register<InstanceReference, UnitResult>(builder, "mcsl.instance.remove", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.RemoveInstanceAsync(request, token).ConfigureAwait(false)));
        Register<InstanceReference, InstanceReport>(builder, "mcsl.instance.report.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetInstanceReportAsync(request, token).ConfigureAwait(false)));
        Register<EmptyRequest, InstanceReportList>(builder, "mcsl.instance.report.list", async (_, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.ListInstanceReportsAsync(token).ConfigureAwait(false)));
        Register<InstanceReference, InstanceSettingsResult>(builder, "mcsl.instance.settings.get", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.GetInstanceSettingsAsync(request, token).ConfigureAwait(false)));
        Register<UpdateInstanceSettingsRequest, UpdateInstanceSettingsResult>(builder, "mcsl.instance.settings.update", async (request, token) =>
            BuiltInApplicationRpcExecution.FromResult(await application.UpdateInstanceSettingsAsync(request, token).ConfigureAwait(false)));
        Register<InstanceReference, UnitResult>(builder, "mcsl.instance.start", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.StartInstanceAsync(request, token).ConfigureAwait(false)));
        Register<InstanceReference, UnitResult>(builder, "mcsl.instance.stop", async (request, token) =>
            BuiltInApplicationRpcExecution.FromUnit(await application.StopInstanceAsync(request, token).ConfigureAwait(false)));
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

    private static void RegisterConsole<TRequest, TResult>(
        ProtocolCatalogBuilder builder,
        string method,
        Func<IProtocolConsoleSessionOperations, TRequest, CancellationToken, Task<ProtocolRpcExecution<TResult>>> handler)
        where TResult : notnull
    {
        var descriptor = (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            candidate => StringComparer.Ordinal.Equals(candidate.Method.Value, method));
        builder.RegisterBuiltInRpc(
            descriptor,
            new RpcBinding<TRequest, TResult>(
                ProtocolExecutionOwner.BuiltIn,
                (context, request, token) => context.ConsoleSessionOperations is { } operations
                    ? handler(operations, request, token)
                    : Task.FromResult(ProtocolRpcExecution<TResult>.Err(
                        new InternalDaemonError(
                            "console.session.context_missing",
                            "The connection console-session context is unavailable.")))));
    }
}
