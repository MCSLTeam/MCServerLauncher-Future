using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Remote.Event;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal sealed record BuiltInProtocolEventSource(
    EventDescriptor Descriptor,
    Type AuthoritativeSourceType,
    Type ProjectionType);

/// <summary>
/// The four built-in protocol event projections. Instance status is intentionally absent because it is internal to event rules.
/// </summary>
internal static class BuiltInProtocolEventSourceInventory
{
    internal static ImmutableArray<BuiltInProtocolEventSource> All { get; } =
    [
        new(
            GetDescriptor<InstanceCatalogChangedEventData>(),
            typeof(AuthoritativeInstanceSnapshotSource),
            typeof(InstanceCatalogChangedDomainEvent)),
        new(
            GetDescriptor<DaemonReportEventData>(),
            typeof(DaemonReportPublisher),
            typeof(DaemonReportDomainEvent)),
        new(
            GetDescriptor<InstanceLogEventData>(),
            typeof(InstanceDomainEventBridge),
            typeof(InstanceLogDomainEvent)),
        new(
            GetDescriptor<NotificationEventData>(),
            typeof(EventTriggerService),
            typeof(ClientNotificationDomainEvent))
    ];

    private static EventDescriptor GetDescriptor<TData>() =>
        BuiltInProtocolDefinitions.Events.Single(descriptor => descriptor.DataTypeInfo.Type == typeof(TData));
}
