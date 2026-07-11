using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal interface IDomainEvent;

internal sealed record InstanceLogDomainEvent(Guid InstanceId, string Log) : IDomainEvent;

internal sealed record InstanceStatusChangedDomainEvent(Guid InstanceId, InstanceStatus Status) : IDomainEvent;

internal sealed record DaemonReportDomainEvent(SystemInfo SystemInfo, long StartTimestamp) : IDomainEvent;

internal sealed record ClientNotificationDomainEvent(
    string Title,
    string Message,
    string Severity,
    Guid SourceInstanceId,
    Guid RuleId,
    long Timestamp) : IDomainEvent;

internal interface IDomainEventPort
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent;

    void Publish<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent;
}

internal sealed class DomainEventPort(ILogger<DomainEventPort> logger) : IDomainEventPort
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, ImmutableArray<Delegate>> _handlers = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var handlers))
                handlers = ImmutableArray<Delegate>.Empty;
            _handlers[eventType] = handlers.Add(handler);
        }

        return new Subscription<TEvent>(this, handler);
    }

    public void Publish<TEvent>(TEvent domainEvent)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        ImmutableArray<Delegate> handlers;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out handlers))
                return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                ((Action<TEvent>)handler)(domainEvent);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Domain event subscriber '{Subscriber}' failed while handling '{EventType}'",
                    handler.Method.DeclaringType?.FullName ?? handler.Method.Name,
                    typeof(TEvent).FullName ?? typeof(TEvent).Name);
            }
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IDomainEvent
    {
        lock (_gate)
        {
            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;

            var updatedHandlers = handlers.Remove(handler);
            if (updatedHandlers.IsEmpty)
                _handlers.Remove(eventType);
            else
                _handlers[eventType] = updatedHandlers;
        }
    }

    private sealed class Subscription<TEvent>(DomainEventPort owner, Action<TEvent> handler) : IDisposable
        where TEvent : IDomainEvent
    {
        private DomainEventPort? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unsubscribe(handler);
        }
    }
}
