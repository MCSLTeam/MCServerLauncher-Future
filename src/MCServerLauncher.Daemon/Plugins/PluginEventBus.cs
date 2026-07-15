using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MessagePipe;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal interface IPluginEventBus
{
    PluginEventSlot<TData, TMeta> Create<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginErrorFactory errorFactory,
        ILogger logger);
}

internal sealed class PluginEventBus(EventFactory eventFactory) : IPluginEventBus
{
    private readonly EventFactory _eventFactory = eventFactory ?? throw new ArgumentNullException(nameof(eventFactory));

    public PluginEventSlot<TData, TMeta> Create<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        PluginErrorFactory errorFactory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(errorFactory);
        ArgumentNullException.ThrowIfNull(logger);

        var (publisher, subscriber) = _eventFactory.CreateAsyncEvent<PluginEventMessage<TData, TMeta>>();
        return new PluginEventSlot<TData, TMeta>(descriptor, errorFactory, logger, publisher, subscriber);
    }
}

internal sealed record PluginEventMessage<TData, TMeta>(
    DaemonEventField<TMeta> Meta,
    DaemonEventField<TData> Data);

internal sealed class PluginEventOwnerLedger
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;

    internal void Track(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        lock (_gate)
        {
            if (_disposed)
            {
                subscription.Dispose();
                throw new ObjectDisposedException(nameof(PluginEventOwnerLedger));
            }

            _subscriptions.Add(subscription);
        }
    }

    internal void Dispose(ILogger logger, string pluginId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        IDisposable[] subscriptions;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Plugin {PluginId} event owner cleanup failed.",
                    pluginId);
            }
        }
    }
}

internal sealed class PluginEventSlot<TData, TMeta>
    : IDisposable
{
    private readonly object _gate = new();
    private readonly PluginErrorFactory _errorFactory;
    private readonly ILogger _logger;
    private readonly IDisposableAsyncPublisher<PluginEventMessage<TData, TMeta>> _publisher;
    private readonly IAsyncSubscriber<PluginEventMessage<TData, TMeta>> _subscriber;
    private FrozenEventBinding? _binding;
    private V2RemoteEventBridge? _remoteEvents;
    private IDisposable? _subscription;
    private bool _active;
    private bool _disposed;

    internal PluginEventSlot(
        EventDescriptor<TData, TMeta> descriptor,
        PluginErrorFactory errorFactory,
        ILogger logger,
        IDisposableAsyncPublisher<PluginEventMessage<TData, TMeta>> publisher,
        IAsyncSubscriber<PluginEventMessage<TData, TMeta>> subscriber)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _errorFactory = errorFactory ?? throw new ArgumentNullException(nameof(errorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
    }

    internal EventDescriptor<TData, TMeta> Descriptor { get; }

    internal ValueTask<Result<Unit, DaemonError>> PublishAsync(
        DaemonEventField<TMeta> meta,
        DaemonEventField<TData> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_active)
            {
                return new ValueTask<Result<Unit, DaemonError>>(
                    Result.Err<Unit, DaemonError>(_errorFactory.Create(
                        "plugin_not_active",
                        $"Plugin event '{Descriptor.Name.Value}' is not active.")));
            }
        }

        return PublishCoreAsync(meta, data, cancellationToken);
    }

    internal void Attach(
        FrozenEventBinding binding,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger ownerLedger)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(remoteEvents);
        ArgumentNullException.ThrowIfNull(ownerLedger);

        lock (_gate)
        {
            if (_active)
                throw new InvalidOperationException($"Plugin event '{Descriptor.Name.Value}' was attached more than once.");

            _binding = binding;
            _remoteEvents = remoteEvents;
            var subscription = _subscriber.Subscribe(
                new RemoteBridgeSubscriber(this, binding, remoteEvents));
            _subscription = subscription;
            try
            {
                ownerLedger.Track(this);
            }
            catch
            {
                subscription.Dispose();
                _publisher.Dispose();
                _subscription = null;
                _binding = null;
                _remoteEvents = null;
                throw;
            }

            _active = true;
        }
    }

    internal void Clear()
    {
        Dispose();
    }

    public void Dispose()
    {
        IDisposable? subscription;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _active = false;
            _binding = null;
            _remoteEvents = null;
            subscription = _subscription;
            _subscription = null;
        }

        subscription?.Dispose();
        _publisher.Dispose();
    }

    private async ValueTask<Result<Unit, DaemonError>> PublishCoreAsync(
        DaemonEventField<TMeta> meta,
        DaemonEventField<TData> data,
        CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.PublishAsync(
                new PluginEventMessage<TData, TMeta>(meta, data),
                cancellationToken).ConfigureAwait(false);
            return PluginResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Plugin event {EventName} publish failed.", Descriptor.Name.Value);
            return PluginResult.Fail<Unit>(_errorFactory.Create(
                "plugin_event_publish_failed",
                $"Plugin event '{Descriptor.Name.Value}' publish failed."));
        }
    }

    private sealed class RemoteBridgeSubscriber(
        PluginEventSlot<TData, TMeta> slot,
        FrozenEventBinding binding,
        V2RemoteEventBridge remoteEvents) : IAsyncMessageHandler<PluginEventMessage<TData, TMeta>>
    {
        public async ValueTask HandleAsync(
            PluginEventMessage<TData, TMeta> message,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await remoteEvents.PublishPluginAsync(
                    binding,
                    message.Meta,
                    message.Data,
                    slot.Descriptor.DataTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                if (result.IsErr(out var error))
                {
                    slot._logger.LogError(
                        "Plugin event {EventName} remote subscriber rejected a message: {Code} {Message}",
                        slot.Descriptor.Name.Value,
                        error!.Code,
                        error.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                slot._logger.LogError(
                    exception,
                    "Plugin event {EventName} remote subscriber failed.",
                    slot.Descriptor.Name.Value);
            }
        }
    }
}
