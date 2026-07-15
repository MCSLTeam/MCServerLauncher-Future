using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Plugins;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

/// <summary>
/// Composes the daemon's built-in protocol graph and publishes it atomically after its unique freeze.
/// The draft callback is startup-only and exists solely for the Phase 5 plugin composition boundary.
/// </summary>
internal sealed class BuiltInProtocolCatalogComposition
{
    public BuiltInProtocolCatalogComposition(
        IInstanceApplication instanceApplication,
        IFileApplication fileApplication,
        ISystemApplication systemApplication,
        IEventRuleApplication eventRuleApplication,
        IInstanceSnapshotSource snapshotSource,
        TimeProvider timeProvider,
        FrozenProtocolCatalogAccessor catalogAccessor,
        PluginHost pluginHost)
        : this(
            instanceApplication,
            fileApplication,
            systemApplication,
            eventRuleApplication,
            snapshotSource,
            timeProvider,
            catalogAccessor,
            GetPluginDraftCallback(pluginHost))
    {
    }

    internal BuiltInProtocolCatalogComposition(
        IInstanceApplication instanceApplication,
        IFileApplication fileApplication,
        ISystemApplication systemApplication,
        IEventRuleApplication eventRuleApplication,
        IInstanceSnapshotSource snapshotSource,
        TimeProvider timeProvider,
        FrozenProtocolCatalogAccessor catalogAccessor,
        Action<ProtocolCatalogBuilder>? configureStartupDraft)
    {
        ArgumentNullException.ThrowIfNull(instanceApplication);
        ArgumentNullException.ThrowIfNull(fileApplication);
        ArgumentNullException.ThrowIfNull(systemApplication);
        ArgumentNullException.ThrowIfNull(eventRuleApplication);
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(catalogAccessor);

        var builder = new ProtocolCatalogBuilder(
            new OpenRpcInfo("MCServerLauncher daemon", Application.AppVersion.ToString()));

        BuiltInConnectionDiscoverySystemRpcRegistrar.Register(
            builder,
            systemApplication,
            snapshotSource,
            timeProvider,
            catalogAccessor);
        BuiltInFileRpcRegistrar.Register(builder, fileApplication);
        BuiltInInstanceRpcRegistrar.Register(builder, instanceApplication);
        BuiltInEventRuleRpcRegistrar.Register(builder, eventRuleApplication);
        RegisterBuiltInEvents(builder);

        configureStartupDraft?.Invoke(builder);

        var catalog = builder.Freeze();
        catalogAccessor.Publish(catalog);
        Catalog = catalog;
    }

    public FrozenProtocolCatalog Catalog { get; }

    private static Action<ProtocolCatalogBuilder> GetPluginDraftCallback(PluginHost pluginHost)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);
        return pluginHost.AddCatalogContributions;
    }

    private static void RegisterBuiltInEvents(ProtocolCatalogBuilder builder)
    {
        RegisterEvent<InstanceCatalogChangedEventData>(builder);
        RegisterEvent<DaemonReportEventData>(builder);
        RegisterEvent<InstanceLogEventData, InstanceLogEventMeta>(builder);
        RegisterEvent<NotificationEventData, NotificationEventMeta>(builder);
    }

    private static void RegisterEvent<TData>(ProtocolCatalogBuilder builder)
    {
        var descriptor = BuiltInProtocolEventSourceInventory.All
            .Single(source => source.Descriptor.DataTypeInfo.Type == typeof(TData))
            .Descriptor;
        builder.RegisterBuiltInEvent(
            descriptor,
            new EventBinding<TData>(ProtocolExecutionOwner.BuiltIn));
    }

    private static void RegisterEvent<TData, TMeta>(ProtocolCatalogBuilder builder)
    {
        var descriptor = BuiltInProtocolEventSourceInventory.All
            .Single(source => source.Descriptor.DataTypeInfo.Type == typeof(TData))
            .Descriptor;
        builder.RegisterBuiltInEvent(
            descriptor,
            new EventBinding<TData, TMeta>(ProtocolExecutionOwner.BuiltIn));
    }
}
