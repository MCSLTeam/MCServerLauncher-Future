using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.Daemon.Remote.Rpc.Events;

internal sealed class V2RemoteEventBridge : IDisposable
{
    private readonly object _publishGate = new();
    private readonly IDomainEventPort _domainEvents;
    private readonly V2EventConnectionRegistry _connections;
    private readonly TimeProvider _timeProvider;
    private readonly DomainEventOwner _eventOwner;
    private readonly FrozenEventBinding _catalogChanged;
    private readonly FrozenEventBinding _daemonReport;
    private readonly FrozenEventBinding _instanceLog;
    private readonly FrozenEventBinding _notification;
    private long _sequence;
    private bool _disposed;

    internal V2RemoteEventBridge(
        IDomainEventPort domainEvents,
        FrozenProtocolCatalog catalog,
        V2EventConnectionRegistry connections,
        TimeProvider timeProvider)
    {
        _domainEvents = domainEvents ?? throw new ArgumentNullException(nameof(domainEvents));
        ArgumentNullException.ThrowIfNull(catalog);
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _catalogChanged = GetBinding<InstanceCatalogChangedDomainEvent>(catalog);
        _daemonReport = GetBinding<DaemonReportDomainEvent>(catalog);
        _instanceLog = GetBinding<InstanceLogDomainEvent>(catalog);
        _notification = GetBinding<ClientNotificationDomainEvent>(catalog);

        _eventOwner = _domainEvents.CreateOwner(nameof(V2RemoteEventBridge));
        try
        {
            _domainEvents.Subscribe<InstanceCatalogChangedDomainEvent>(_eventOwner, OnCatalogChanged);
            _domainEvents.Subscribe<DaemonReportDomainEvent>(_eventOwner, OnDaemonReport);
            _domainEvents.Subscribe<InstanceLogDomainEvent>(_eventOwner, OnInstanceLog);
            _domainEvents.Subscribe<ClientNotificationDomainEvent>(_eventOwner, OnNotification);
        }
        catch
        {
            _domainEvents.DisposeOwner(_eventOwner);
            throw;
        }
    }

    public void Dispose()
    {
        lock (_publishGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _domainEvents.DisposeOwner(_eventOwner);
        }
    }

    internal static long NextSequence(long current) => checked(current + 1);

    internal static long GetTimestamp(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        return timestamp >= 0
            ? timestamp
            : throw new InvalidOperationException("The remote event clock returned a pre-epoch timestamp.");
    }

    private ValueTask OnCatalogChanged(InstanceCatalogChangedDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _catalogChanged,
            V2CanonicalEventMeta.Omitted(_catalogChanged),
            domainEvent.Data,
            BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData);

    private ValueTask OnDaemonReport(DaemonReportDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _daemonReport,
            V2CanonicalEventMeta.Omitted(_daemonReport),
            new DaemonReportEventData(domainEvent.SystemInfo, domainEvent.StartTimestamp),
            BuiltInProtocolJsonContext.Default.DaemonReportEventData);

    private ValueTask OnInstanceLog(InstanceLogDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _instanceLog,
            V2CanonicalEventMeta.FromTypedObject(
                _instanceLog,
                new InstanceLogEventMeta(domainEvent.InstanceId)),
            new InstanceLogEventData(domainEvent.Log),
            BuiltInProtocolJsonContext.Default.InstanceLogEventData);

    private ValueTask OnNotification(ClientNotificationDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _notification,
            V2CanonicalEventMeta.FromTypedObject(
                _notification,
                new NotificationEventMeta(domainEvent.SourceInstanceId, domainEvent.RuleId)),
            new NotificationEventData(domainEvent.Title, domainEvent.Message, domainEvent.Severity),
            BuiltInProtocolJsonContext.Default.NotificationEventData);

    private ValueTask Publish<TData>(
        FrozenEventBinding binding,
        V2CanonicalEventMeta actualMeta,
        TData data,
        JsonTypeInfo<TData> dataTypeInfo)
    {
        lock (_publishGate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _sequence = NextSequence(_sequence);
            var timestamp = GetTimestamp(_timeProvider);

            var matchingOwners = _connections.Snapshot()
                .Where(entry => entry.Ledger.Matches(binding, actualMeta))
                .Select(static entry => entry.Owner)
                .ToImmutableArray();
            if (matchingOwners.IsEmpty)
                return ValueTask.CompletedTask;

            var notification = new JsonRpcRemoteEventNotification(
                binding.Descriptor.Name.Value,
                new JsonRpcRemoteEventParameters(
                    _sequence,
                    timestamp,
                    ToWireMeta(actualMeta),
                    JsonRpcOptionalPayload.From(data, dataTypeInfo)));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                notification,
                BuiltInProtocolJsonContext.Default.JsonRpcRemoteEventNotification);
            var payload = ImmutableCollectionsMarshal.AsImmutableArray(bytes);
            var message = V2OutboundMessage.Single(V2OutboundFrame.Text(payload));

            foreach (var owner in matchingOwners)
                owner.TryEnqueue(message);
        }

        return ValueTask.CompletedTask;
    }

    private static JsonRpcOptionalPayload ToWireMeta(V2CanonicalEventMeta meta) => meta.Kind switch
    {
        V2EventMetaValueKind.Omitted => JsonRpcOptionalPayload.Missing,
        V2EventMetaValueKind.ExplicitNull => JsonRpcOptionalPayload.ExplicitNull,
        V2EventMetaValueKind.Object => JsonRpcOptionalPayload.FromOwnedBuffer(
            ImmutableCollectionsMarshal.AsArray(meta.CanonicalUtf8)!,
            0,
            meta.CanonicalUtf8.Length),
        _ => throw new ArgumentOutOfRangeException(nameof(meta), meta.Kind, null)
    };

    private static FrozenEventBinding GetBinding<TProjection>(FrozenProtocolCatalog catalog)
    {
        var source = BuiltInProtocolEventSourceInventory.All
            .Single(item => item.ProjectionType == typeof(TProjection));
        if (!catalog.TryGetEvent(source.Descriptor.Name, out var binding) ||
            !ReferenceEquals(binding.Descriptor, source.Descriptor))
        {
            throw new InvalidOperationException(
                $"The final protocol catalog does not contain the exact built-in event '{source.Descriptor.Name.Value}'.");
        }

        return binding;
    }
}
