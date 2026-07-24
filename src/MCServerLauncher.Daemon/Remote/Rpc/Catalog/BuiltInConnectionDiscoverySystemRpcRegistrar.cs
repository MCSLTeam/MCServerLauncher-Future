using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

internal static class BuiltInConnectionDiscoverySystemRpcRegistrar
{
    private const string PermissionsMethod = "mcsl.auth.permissions.get";
    private const string PingMethod = "mcsl.daemon.ping";
    private const string SubscribeMethod = "mcsl.event.subscribe";
    private const string UnsubscribeMethod = "mcsl.event.unsubscribe";
    private const string DiscoverMethod = "rpc.discover";
    private const string CatalogMethod = "mcsl.instance.catalog.get";
    private const string JavaListMethod = "mcsl.java.list";
    private const string SystemInfoMethod = "mcsl.system.info.get";

    public static void Register(
        ProtocolCatalogBuilder builder,
        ISystemApplication systemApplication,
        IInstanceSnapshotSource snapshotSource,
        TimeProvider timeProvider,
        IFrozenProtocolCatalogAccessor catalogAccessor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(systemApplication);
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(catalogAccessor);

        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, PermissionsResult>(PermissionsMethod),
            new RpcBinding<EmptyRequest, PermissionsResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, _, _) => Task.FromResult(GetPermissions(context))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, PingResult>(PingMethod),
            new RpcBinding<EmptyRequest, PingResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(ProtocolRpcExecution<PingResult>.Ok(
                    new PingResult(timeProvider.GetUtcNow().ToUnixTimeMilliseconds())))));
        builder.RegisterBuiltInRpc(
            Descriptor<EventSubscriptionRequest, UnitResult>(SubscribeMethod),
            new RpcBinding<EventSubscriptionRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, request, _) => Task.FromResult(UpdateSubscription(context, request, subscribe: true))));
        builder.RegisterBuiltInRpc(
            Descriptor<EventSubscriptionRequest, UnitResult>(UnsubscribeMethod),
            new RpcBinding<EventSubscriptionRequest, UnitResult>(
                ProtocolExecutionOwner.BuiltIn,
                static (context, request, _) => Task.FromResult(UpdateSubscription(context, request, subscribe: false))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, OpenRpcDocument>(DiscoverMethod),
            new RpcBinding<EmptyRequest, OpenRpcDocument>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(Discover(catalogAccessor))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, InstanceCatalogResult>(CatalogMethod),
            new RpcBinding<EmptyRequest, InstanceCatalogResult>(
                ProtocolExecutionOwner.BuiltIn,
                (_, _, _) => Task.FromResult(GetInstanceCatalog(snapshotSource))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, JavaRuntimeList>(JavaListMethod),
            new RpcBinding<EmptyRequest, JavaRuntimeList>(
                ProtocolExecutionOwner.BuiltIn,
                async (_, _, cancellationToken) => ToExecution(
                    await systemApplication.ListJavaRuntimesAsync(cancellationToken).ConfigureAwait(false))));
        builder.RegisterBuiltInRpc(
            Descriptor<EmptyRequest, SystemInfo>(SystemInfoMethod),
            new RpcBinding<EmptyRequest, SystemInfo>(
                ProtocolExecutionOwner.BuiltIn,
                async (_, _, cancellationToken) => ToExecution(
                    await systemApplication.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false))));
    }

    private static ProtocolRpcExecution<PermissionsResult> GetPermissions(ProtocolInvocationContext context)
    {
        if (context.PermissionView is null)
        {
            return ProtocolRpcExecution<PermissionsResult>.Err(MissingConnectionCapability("permission view"));
        }

        return ProtocolRpcExecution<PermissionsResult>.Ok(new PermissionsResult(context.PermissionView.Permissions));
    }

    private static ProtocolRpcExecution<UnitResult> UpdateSubscription(
        ProtocolInvocationContext context,
        EventSubscriptionRequest request,
        bool subscribe)
    {
        if (context.SubscriptionOperations is null)
        {
            return ProtocolRpcExecution<UnitResult>.Err(MissingConnectionCapability("subscription operations"));
        }

        var result = subscribe
            ? context.SubscriptionOperations.Subscribe(request)
            : context.SubscriptionOperations.Unsubscribe(request);
        return result.IsOk(out _)
            ? ProtocolRpcExecution<UnitResult>.Ok(new UnitResult())
            : ProtocolRpcExecution<UnitResult>.Err(result.UnwrapErr());
    }

    private static ProtocolRpcExecution<OpenRpcDocument> Discover(IFrozenProtocolCatalogAccessor catalogAccessor)
    {
        if (!catalogAccessor.TryGet(out var catalog))
        {
            return ProtocolRpcExecution<OpenRpcDocument>.Err(new InternalDaemonError(
                "protocol.catalog.unavailable",
                "The frozen runtime protocol catalog is not available."));
        }

        return ProtocolRpcExecution<OpenRpcDocument>.Ok(catalog!.Document);
    }

    private static ProtocolRpcExecution<InstanceCatalogResult> GetInstanceCatalog(
        IInstanceSnapshotSource snapshotSource)
    {
        var published = snapshotSource.Current;
        var items = published.Value.Instances.Values
            .Select(static snapshot => new InstanceCatalogItem(
                snapshot.Id,
                snapshot.Name,
                snapshot.InstanceType,
                snapshot.Version,
                snapshot.Status,
                snapshot.ReadyTimedOut))
            .ToImmutableArray();
        return ProtocolRpcExecution<InstanceCatalogResult>.Ok(
            new InstanceCatalogResult(published.Version, items));
    }

    private static ProtocolRpcExecution<TResult> ToExecution<TResult>(Result<TResult, DaemonError> result)
        where TResult : notnull =>
        result.IsOk(out var value)
            ? ProtocolRpcExecution<TResult>.Ok(value)
            : ProtocolRpcExecution<TResult>.Err(result.UnwrapErr());

    private static InternalDaemonError MissingConnectionCapability(string capability) =>
        new(
            "protocol.connection.capability.unavailable",
            $"The RPC invocation does not provide the required connection {capability}.");

    private static RpcDescriptor<TRequest, TResult> Descriptor<TRequest, TResult>(string method) =>
        (RpcDescriptor<TRequest, TResult>)BuiltInProtocolDefinitions.Rpcs.Single(
            descriptor => StringComparer.Ordinal.Equals(descriptor.Method.Value, method));
}
