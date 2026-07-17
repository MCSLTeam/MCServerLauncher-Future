using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;

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
            JsonRpcOptionalPayload.From(domainEvent.Data, BuiltInProtocolJsonContext.Default.InstanceCatalogChangedEventData));

    private ValueTask OnDaemonReport(DaemonReportDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _daemonReport,
            V2CanonicalEventMeta.Omitted(_daemonReport),
            JsonRpcOptionalPayload.From(
                new DaemonReportEventData(domainEvent.SystemInfo, domainEvent.StartTimestamp),
                BuiltInProtocolJsonContext.Default.DaemonReportEventData));

    private ValueTask OnInstanceLog(InstanceLogDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _instanceLog,
            V2CanonicalEventMeta.FromTypedObject(
                _instanceLog,
                new InstanceLogEventMeta(domainEvent.InstanceId)),
            JsonRpcOptionalPayload.From(
                new InstanceLogEventData(domainEvent.Log),
                BuiltInProtocolJsonContext.Default.InstanceLogEventData));

    private ValueTask OnNotification(ClientNotificationDomainEvent domainEvent, CancellationToken _) =>
        Publish(
            _notification,
            V2CanonicalEventMeta.FromTypedObject(
                _notification,
                new NotificationEventMeta(domainEvent.SourceInstanceId, domainEvent.RuleId)),
            JsonRpcOptionalPayload.From(
                new NotificationEventData(domainEvent.Title, domainEvent.Message, domainEvent.Severity),
                BuiltInProtocolJsonContext.Default.NotificationEventData));

    private ValueTask Publish(
        FrozenEventBinding binding,
        V2CanonicalEventMeta actualMeta,
        JsonRpcOptionalPayload dataPayload)
    {
        lock (_publishGate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _sequence = NextSequence(_sequence);
            var timestamp = GetTimestamp(_timeProvider);

            V2OutboundMessage? message = null;
            foreach (var entry in _connections.Snapshot(binding))
            {
                if (!entry.Ledger.Matches(binding, actualMeta))
                    continue;

                message ??= CreateMessage(binding, actualMeta, dataPayload, _sequence, timestamp);
                entry.Owner.TryEnqueue(message);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static V2OutboundMessage CreateMessage(
        FrozenEventBinding binding,
        V2CanonicalEventMeta actualMeta,
        JsonRpcOptionalPayload dataPayload,
        long sequence,
        long timestamp)
    {
        var notification = new JsonRpcRemoteEventNotification(
            binding.Descriptor.Name.Value,
            new JsonRpcRemoteEventParameters(
                sequence,
                timestamp,
                ToWireMeta(actualMeta),
                dataPayload));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            notification,
            BuiltInProtocolJsonContext.Default.JsonRpcRemoteEventNotification);
        var payload = ImmutableCollectionsMarshal.AsImmutableArray(bytes);
        return V2OutboundMessage.Single(V2OutboundFrame.Text(payload));
    }

    internal ValueTask<Result<Unit, DaemonError>> PublishPluginAsync<TData, TMeta>(
        FrozenEventBinding binding,
        DaemonEventField<TMeta> meta,
        DaemonEventField<TData> data,
        JsonTypeInfo<TData> dataTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(dataTypeInfo);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (binding.Descriptor.DataTypeInfo.Type != typeof(TData))
            {
                throw new ArgumentException(
                    "The plugin event data metadata does not match its descriptor.",
                    nameof(dataTypeInfo));
            }

            var dataPayload = ToWireData(binding.Descriptor, data, dataTypeInfo);
            var actualMeta = ToCanonicalMeta(binding, meta);
            lock (_publishGate)
            {
                if (_disposed)
                {
                    return new ValueTask<Result<Unit, DaemonError>>(
                        Result.Err<Unit, DaemonError>(
                            new InternalDaemonError("event_bridge_closed", "The daemon event bridge is closed.")));
                }

                _ = Publish(binding, actualMeta, dataPayload);
                return new ValueTask<Result<Unit, DaemonError>>(
                    Result.Ok<Unit, DaemonError>(Unit.Default));
            }
        }
        catch (ArgumentException exception)
        {
            return new ValueTask<Result<Unit, DaemonError>>(
                Result.Err<Unit, DaemonError>(
                    new ValidationDaemonError("plugin_event_invalid", exception.Message)));
        }
    }

    private static JsonRpcOptionalPayload ToWireData<TData>(
        EventDescriptor descriptor,
        DaemonEventField<TData> data,
        JsonTypeInfo<TData> dataTypeInfo)
    {
        if (descriptor.DataPresence == OpenRpcEventFieldPresence.Required && data.Kind != DaemonEventFieldKind.Value)
            throw new ArgumentException("The plugin event requires a data value.", nameof(data));
        if (descriptor.DataPresence == OpenRpcEventFieldPresence.Omitted && data.Kind != DaemonEventFieldKind.Missing)
            throw new ArgumentException("The plugin event omits its data field.", nameof(data));

        return data.Kind switch
        {
            DaemonEventFieldKind.Missing => JsonRpcOptionalPayload.Missing,
            DaemonEventFieldKind.ExplicitNull => JsonRpcOptionalPayload.ExplicitNull,
            DaemonEventFieldKind.Value => JsonRpcOptionalPayload.From(data.Value, dataTypeInfo),
            _ => throw new ArgumentOutOfRangeException(nameof(data))
        };
    }

    private static V2CanonicalEventMeta ToCanonicalMeta<TMeta>(
        FrozenEventBinding binding,
        DaemonEventField<TMeta> meta)
    {
        var descriptor = binding.Descriptor;
        return meta.Kind switch
        {
            DaemonEventFieldKind.Missing when descriptor.MetaPresence is OpenRpcEventFieldPresence.Omitted or OpenRpcEventFieldPresence.Optional =>
                V2CanonicalEventMeta.Omitted(binding),
            DaemonEventFieldKind.ExplicitNull when descriptor.MetaPresence == OpenRpcEventFieldPresence.Optional =>
                V2CanonicalEventMeta.ExplicitNull(binding),
            DaemonEventFieldKind.Value when descriptor.MetaTypeInfo?.Type == typeof(TMeta) =>
                V2CanonicalEventMeta.FromTypedObject(binding, meta.Value!),
            DaemonEventFieldKind.Value =>
                throw new ArgumentException("The plugin event metadata type does not match its descriptor.", nameof(meta)),
            _ => throw new ArgumentException("The plugin event requires metadata.", nameof(meta))
        };
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
