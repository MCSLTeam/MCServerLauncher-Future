using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginEventSubscriber(
    PluginManifest manifest,
    IDomainEventPort domainEvents,
    DomainEventOwner owner,
    PluginErrorFactory errors) : IPluginEventSubscriber
{
    public Result<Unit, DaemonError> Subscribe<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginEventHandler<TData, TMeta> handler,
        DaemonEventField<TMeta> metaFilter = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (!manifest.HasFeature(PluginFeature.EventSubscribe))
        {
            return PluginResult.Fail(errors.Create(
                "plugin_feature_required",
                $"Plugin '{manifest.Identity.Id}' must declare feature 'event.subscribe' before subscribing to events."));
        }

        try
        {
            if (ReferenceEquals(descriptor, BuiltInProtocolDefinitions.InstanceCatalogChanged))
                return SubscribeCatalogChanged(descriptor, handler, metaFilter);
            if (ReferenceEquals(descriptor, BuiltInProtocolDefinitions.DaemonReport))
                return SubscribeDaemonReport(descriptor, handler, metaFilter);
            if (ReferenceEquals(descriptor, BuiltInProtocolDefinitions.InstanceLog))
                return SubscribeInstanceLog(descriptor, handler, metaFilter);
            if (ReferenceEquals(descriptor, BuiltInProtocolDefinitions.Notification))
                return SubscribeNotification(descriptor, handler, metaFilter);

            return PluginResult.Fail(errors.Create(
                "plugin_event_subscription_invalid",
                $"Plugin event subscription '{descriptor.Name.Value}' is not a built-in application event descriptor."));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return PluginResult.Fail(errors.Create("plugin_event_subscription_invalid", exception.Message));
        }
    }

    private Result<Unit, DaemonError> SubscribeCatalogChanged<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginEventHandler<TData, TMeta> handler,
        DaemonEventField<TMeta> metaFilter)
    {
        EnsureShape<TData, TMeta, InstanceCatalogChangedEventData, EmptyRequest>(descriptor);
        EnsureOmittedMetaFilter(descriptor, metaFilter);
        domainEvents.Subscribe<InstanceCatalogChangedDomainEvent>(owner, (domainEvent, cancellationToken) =>
            handler(
                DaemonEventField<TMeta>.Missing,
                DaemonEventField<TData>.FromValue((TData)(object)domainEvent.Data),
                cancellationToken));
        return PluginResult.Ok();
    }

    private Result<Unit, DaemonError> SubscribeDaemonReport<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginEventHandler<TData, TMeta> handler,
        DaemonEventField<TMeta> metaFilter)
    {
        EnsureShape<TData, TMeta, DaemonReportEventData, EmptyRequest>(descriptor);
        EnsureOmittedMetaFilter(descriptor, metaFilter);
        domainEvents.Subscribe<DaemonReportDomainEvent>(owner, (domainEvent, cancellationToken) =>
            handler(
                DaemonEventField<TMeta>.Missing,
                DaemonEventField<TData>.FromValue((TData)(object)new DaemonReportEventData(domainEvent.SystemInfo, domainEvent.StartTimestamp)),
                cancellationToken));
        return PluginResult.Ok();
    }

    private Result<Unit, DaemonError> SubscribeInstanceLog<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginEventHandler<TData, TMeta> handler,
        DaemonEventField<TMeta> metaFilter)
    {
        EnsureShape<TData, TMeta, InstanceLogEventData, InstanceLogEventMeta>(descriptor);
        EnsureAllowedMetaFilter(descriptor, metaFilter);
        domainEvents.Subscribe<InstanceLogDomainEvent>(owner, (domainEvent, cancellationToken) =>
        {
            var meta = DaemonEventField<TMeta>.FromValue((TMeta)(object)new InstanceLogEventMeta(domainEvent.InstanceId));
            if (!MatchesMetaFilter(metaFilter, meta))
                return ValueTask.CompletedTask;

            return handler(
                meta,
                DaemonEventField<TData>.FromValue((TData)(object)new InstanceLogEventData(domainEvent.Log)),
                cancellationToken);
        });
        return PluginResult.Ok();
    }

    private Result<Unit, DaemonError> SubscribeNotification<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginEventHandler<TData, TMeta> handler,
        DaemonEventField<TMeta> metaFilter)
    {
        EnsureShape<TData, TMeta, NotificationEventData, NotificationEventMeta>(descriptor);
        EnsureAllowedMetaFilter(descriptor, metaFilter);
        domainEvents.Subscribe<ClientNotificationDomainEvent>(owner, (domainEvent, cancellationToken) =>
        {
            var meta = DaemonEventField<TMeta>.FromValue((TMeta)(object)new NotificationEventMeta(domainEvent.SourceInstanceId, domainEvent.RuleId));
            if (!MatchesMetaFilter(metaFilter, meta))
                return ValueTask.CompletedTask;

            return handler(
                meta,
                DaemonEventField<TData>.FromValue((TData)(object)new NotificationEventData(domainEvent.Title, domainEvent.Message, domainEvent.Severity)),
                cancellationToken);
        });
        return PluginResult.Ok();
    }

    private static void EnsureShape<TData, TMeta, TExpectedData, TExpectedMeta>(EventDescriptor<TData, TMeta> descriptor)
    {
        if (typeof(TData) != typeof(TExpectedData) || typeof(TMeta) != typeof(TExpectedMeta))
        {
            throw new ArgumentException(
                $"Event descriptor '{descriptor.Name.Value}' does not match the requested data/meta type arguments.",
                nameof(descriptor));
        }
    }

    private static void EnsureOmittedMetaFilter<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventField<TMeta> metaFilter)
    {
        if (metaFilter.Kind != DaemonEventFieldKind.Missing)
        {
            throw new ArgumentException(
                $"Event descriptor '{descriptor.Name.Value}' omits metadata and only accepts a missing metadata filter.",
                nameof(metaFilter));
        }
    }

    private static void EnsureAllowedMetaFilter<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventField<TMeta> metaFilter)
    {
        if (metaFilter.Kind == DaemonEventFieldKind.ExplicitNull &&
            descriptor.MetaPresence != OpenRpcEventFieldPresence.Optional)
        {
            throw new ArgumentException(
                $"Event descriptor '{descriptor.Name.Value}' does not accept a null metadata filter.",
                nameof(metaFilter));
        }
    }

    private static bool MatchesMetaFilter<TMeta>(DaemonEventField<TMeta> filter, DaemonEventField<TMeta> actual) =>
        filter.Kind switch
        {
            DaemonEventFieldKind.Missing => true,
            DaemonEventFieldKind.ExplicitNull => actual.Kind == DaemonEventFieldKind.ExplicitNull,
            DaemonEventFieldKind.Value => actual.Kind == DaemonEventFieldKind.Value &&
                                          EqualityComparer<TMeta>.Default.Equals(filter.Value, actual.Value),
            _ => false
        };
}

internal sealed class PluginDomainEventOwnerHandle(
    IDomainEventPort domainEvents,
    DomainEventOwner owner) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        domainEvents.DisposeOwner(owner);
    }
}
