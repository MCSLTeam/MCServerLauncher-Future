using System.Diagnostics;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MessagePipe;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal interface IDomainEvent;

internal sealed record InstanceLogDomainEvent(Guid InstanceId, string Log) : IDomainEvent;

internal sealed record InstanceStatusChangedDomainEvent(Guid InstanceId, InstanceStatus Status) : IDomainEvent;

internal sealed record InstanceCatalogChangedDomainEvent(InstanceCatalogChangedEventData Data) : IDomainEvent;

internal sealed record DaemonReportDomainEvent(SystemInfo SystemInfo, long StartTimestamp) : IDomainEvent;

internal sealed record ClientNotificationDomainEvent(
    string Title,
    string Message,
    string Severity,
    Guid SourceInstanceId,
    Guid RuleId,
    long Timestamp) : IDomainEvent;

internal sealed class DomainEventOwner
{
    private int _disposed;

    internal DomainEventOwner(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    internal string Name { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal bool TryDispose()
    {
        return Interlocked.Exchange(ref _disposed, 1) == 0;
    }
}

internal interface IDomainEventPort
{
    DomainEventOwner CreateOwner(string name);

    void Subscribe<TEvent>(
        DomainEventOwner owner,
        Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : IDomainEvent;

    ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    void DisposeOwner(DomainEventOwner owner);
}

internal interface IDomainEventClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
}

internal sealed class SystemDomainEventClock : IDomainEventClock
{
    internal static readonly SystemDomainEventClock Instance = new();

    private SystemDomainEventClock()
    {
    }

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        return Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
    }
}

internal sealed record DomainEventDispatchPolicy(
    TimeSpan SlowHandlerThreshold,
    IDomainEventClock Clock)
{
    internal static readonly DomainEventDispatchPolicy Default = new(
        TimeSpan.FromSeconds(1),
        SystemDomainEventClock.Instance);
}

internal sealed class DomainEventPort : IDomainEventPort, IDisposable
{
    private readonly IAsyncPublisher<InstanceLogDomainEvent> _logPublisher;
    private readonly IAsyncSubscriber<InstanceLogDomainEvent> _logSubscriber;
    private readonly IAsyncPublisher<InstanceStatusChangedDomainEvent> _statusPublisher;
    private readonly IAsyncSubscriber<InstanceStatusChangedDomainEvent> _statusSubscriber;
    private readonly IAsyncPublisher<InstanceCatalogChangedDomainEvent> _catalogPublisher;
    private readonly IAsyncSubscriber<InstanceCatalogChangedDomainEvent> _catalogSubscriber;
    private readonly IAsyncPublisher<DaemonReportDomainEvent> _reportPublisher;
    private readonly IAsyncSubscriber<DaemonReportDomainEvent> _reportSubscriber;
    private readonly IAsyncPublisher<ClientNotificationDomainEvent> _notificationPublisher;
    private readonly IAsyncSubscriber<ClientNotificationDomainEvent> _notificationSubscriber;
    private readonly ILogger<DomainEventPort> _logger;
    private readonly DomainEventDispatchPolicy _policy;
    private readonly object _gate = new();
    private readonly Dictionary<DomainEventOwner, List<IDisposable>> _subscriptions = [];
    private bool _disposed;

    public DomainEventPort(
        IAsyncPublisher<InstanceLogDomainEvent> logPublisher,
        IAsyncSubscriber<InstanceLogDomainEvent> logSubscriber,
        IAsyncPublisher<InstanceStatusChangedDomainEvent> statusPublisher,
        IAsyncSubscriber<InstanceStatusChangedDomainEvent> statusSubscriber,
        IAsyncPublisher<InstanceCatalogChangedDomainEvent> catalogPublisher,
        IAsyncSubscriber<InstanceCatalogChangedDomainEvent> catalogSubscriber,
        IAsyncPublisher<DaemonReportDomainEvent> reportPublisher,
        IAsyncSubscriber<DaemonReportDomainEvent> reportSubscriber,
        IAsyncPublisher<ClientNotificationDomainEvent> notificationPublisher,
        IAsyncSubscriber<ClientNotificationDomainEvent> notificationSubscriber,
        ILogger<DomainEventPort> logger,
        DomainEventDispatchPolicy policy)
    {
        _logPublisher = logPublisher;
        _logSubscriber = logSubscriber;
        _statusPublisher = statusPublisher;
        _statusSubscriber = statusSubscriber;
        _catalogPublisher = catalogPublisher;
        _catalogSubscriber = catalogSubscriber;
        _reportPublisher = reportPublisher;
        _reportSubscriber = reportSubscriber;
        _notificationPublisher = notificationPublisher;
        _notificationSubscriber = notificationSubscriber;
        _logger = logger;
        _policy = policy;
    }

    internal int ActiveSubscriptionCount
    {
        get
        {
            lock (_gate)
                return _subscriptions.Values.Sum(static subscriptions => subscriptions.Count);
        }
    }

    public DomainEventOwner CreateOwner(string name)
    {
        var owner = new DomainEventOwner(name);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions.Add(owner, []);
        }

        return owner;
    }

    public void Subscribe<TEvent>(
        DomainEventOwner owner,
        Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObjectDisposedException.ThrowIf(owner.IsDisposed, owner);
            if (!_subscriptions.TryGetValue(owner, out var subscriptions))
                throw new InvalidOperationException("The domain-event owner was not created by this port.");

            var guardedHandler = new GuardedAsyncMessageHandler<TEvent>(
                owner.Name,
                handler,
                _logger,
                _policy);
            subscriptions.Add(SubscribeCore(guardedHandler));
        }
    }

    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        if (domainEvent is InstanceLogDomainEvent logEvent)
            return _logPublisher.PublishAsync(logEvent, cancellationToken);
        if (domainEvent is InstanceStatusChangedDomainEvent statusEvent)
            return _statusPublisher.PublishAsync(statusEvent, cancellationToken);
        if (domainEvent is InstanceCatalogChangedDomainEvent catalogEvent)
            return _catalogPublisher.PublishAsync(catalogEvent, cancellationToken);
        if (domainEvent is DaemonReportDomainEvent reportEvent)
            return _reportPublisher.PublishAsync(reportEvent, cancellationToken);
        if (domainEvent is ClientNotificationDomainEvent notificationEvent)
            return _notificationPublisher.PublishAsync(notificationEvent, cancellationToken);

        throw new NotSupportedException($"Domain event type '{typeof(TEvent).FullName}' is not registered.");
    }

    public void DisposeOwner(DomainEventOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.TryDispose())
            return;

        List<IDisposable>? subscriptions;
        lock (_gate)
        {
            if (!_subscriptions.Remove(owner, out subscriptions))
                return;
        }

        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }

    public void Dispose()
    {
        List<IDisposable> subscriptions;
        DomainEventOwner[] owners;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            owners = [.. _subscriptions.Keys];
            subscriptions = [.. _subscriptions.Values.SelectMany(static value => value)];
            _subscriptions.Clear();
        }

        foreach (var owner in owners)
            owner.TryDispose();
        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }

    private IDisposable SubscribeCore<TEvent>(IAsyncMessageHandler<TEvent> handler)
        where TEvent : IDomainEvent
    {
        if (typeof(TEvent) == typeof(InstanceLogDomainEvent))
            return _logSubscriber.Subscribe((IAsyncMessageHandler<InstanceLogDomainEvent>)handler);
        if (typeof(TEvent) == typeof(InstanceStatusChangedDomainEvent))
            return _statusSubscriber.Subscribe((IAsyncMessageHandler<InstanceStatusChangedDomainEvent>)handler);
        if (typeof(TEvent) == typeof(InstanceCatalogChangedDomainEvent))
            return _catalogSubscriber.Subscribe((IAsyncMessageHandler<InstanceCatalogChangedDomainEvent>)handler);
        if (typeof(TEvent) == typeof(DaemonReportDomainEvent))
            return _reportSubscriber.Subscribe((IAsyncMessageHandler<DaemonReportDomainEvent>)handler);
        if (typeof(TEvent) == typeof(ClientNotificationDomainEvent))
            return _notificationSubscriber.Subscribe((IAsyncMessageHandler<ClientNotificationDomainEvent>)handler);

        throw new NotSupportedException($"Domain event type '{typeof(TEvent).FullName}' is not registered.");
    }

    private sealed class GuardedAsyncMessageHandler<TEvent>(
        string owner,
        Func<TEvent, CancellationToken, ValueTask> handler,
        ILogger logger,
        DomainEventDispatchPolicy policy) : IAsyncMessageHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public async ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken)
        {
            var started = policy.Clock.GetTimestamp();
            try
            {
                await handler(message, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Domain event owner '{Owner}' failed while handling '{DomainEventType}'",
                    owner,
                    typeof(TEvent).FullName ?? typeof(TEvent).Name);
            }
            finally
            {
                var elapsed = policy.Clock.GetElapsedTime(started, policy.Clock.GetTimestamp());
                if (elapsed >= policy.SlowHandlerThreshold)
                {
                    logger.LogWarning(
                        "Slow domain event owner '{Owner}' handled '{DomainEventType}' in {ElapsedMilliseconds} ms",
                        owner,
                        typeof(TEvent).FullName ?? typeof(TEvent).Name,
                        elapsed.TotalMilliseconds);
                }
            }
        }
    }
}
