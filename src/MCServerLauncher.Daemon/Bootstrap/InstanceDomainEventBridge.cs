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
        _manager.InstanceLogReceived += OnInstanceLog;
        _manager.InstanceStatusChanged += OnInstanceStatusChanged;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _manager.InstanceLogReceived -= OnInstanceLog;
        _manager.InstanceStatusChanged -= OnInstanceStatusChanged;
    }

    private void OnInstanceLog(Guid instanceId, string log)
    {
        _domainEvents.Publish(new InstanceLogDomainEvent(instanceId, log));
    }

    private void OnInstanceStatusChanged(Guid instanceId, InstanceStatus status)
    {
        _domainEvents.Publish(new InstanceStatusChangedDomainEvent(instanceId, status));
    }
}
