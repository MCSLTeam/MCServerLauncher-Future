using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Management;

namespace MCServerLauncher.Daemon.Bootstrap;

internal sealed class InstanceDomainEventBridge : IDisposable
{
    private readonly InstanceManager _manager;
    private readonly IDomainEventPort _domainEvents;
    private int _disposed;

    public InstanceDomainEventBridge(InstanceManager manager, IDomainEventPort domainEvents)
    {
        _manager = manager;
        _domainEvents = domainEvents;
        _manager.InstanceLogReceived += OnInstanceLogAsync;
        _manager.InstanceStatusChanged += OnInstanceStatusChangedAsync;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _manager.InstanceLogReceived -= OnInstanceLogAsync;
        _manager.InstanceStatusChanged -= OnInstanceStatusChangedAsync;
    }

    private async Task OnInstanceLogAsync(
        Guid instanceId,
        string log,
        CancellationToken cancellationToken)
    {
        await _domainEvents.PublishAsync(
            new InstanceLogDomainEvent(instanceId, log),
            cancellationToken);
    }

    private async Task OnInstanceStatusChangedAsync(
        Guid instanceId,
        InstanceStatus status,
        CancellationToken cancellationToken)
    {
        await _domainEvents.PublishAsync(
            new InstanceStatusChangedDomainEvent(instanceId, status),
            cancellationToken);
    }
}
