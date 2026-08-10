using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginInstanceLifecycleHookBridge : IDisposable
{
    private readonly PluginExtensionPointRuntimeRegistry _registry;
    private readonly IDomainEventPort _domainEvents;
    private readonly ILogger<PluginInstanceLifecycleHookBridge> _logger;
    private readonly DomainEventOwner _owner;
    private int _disposed;

    public PluginInstanceLifecycleHookBridge(
        PluginExtensionPointRuntimeRegistry registry,
        IDomainEventPort domainEvents,
        ILogger<PluginInstanceLifecycleHookBridge> logger)
    {
        _registry = registry;
        _domainEvents = domainEvents;
        _logger = logger;
        _owner = domainEvents.CreateOwner(nameof(PluginInstanceLifecycleHookBridge));
        domainEvents.Subscribe<InstanceStatusChangedDomainEvent>(_owner, OnInstanceStatusChangedAsync);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _domainEvents.DisposeOwner(_owner);
    }

    private async ValueTask OnInstanceStatusChangedAsync(
        InstanceStatusChangedDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var result = await _registry.PublishInstanceLifecycleAsync(
            new PluginInstanceLifecycleEvent(
                domainEvent.InstanceId.ToString("D"),
                domainEvent.Status.ToString()),
            cancellationToken).ConfigureAwait(false);
        if (result.IsErr(out var error))
        {
            _logger.LogWarning(
                "Plugin lifecycle hook failed for instance {InstanceId} status {Status}: {Code} {Message}",
                domainEvent.InstanceId,
                domainEvent.Status,
                error!.Code,
                error.Message);
        }
    }
}
