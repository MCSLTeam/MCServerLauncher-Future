using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Exercises the daemon's Sequential MessagePipe and DomainEventPort wrapper configuration.
/// Hosts and subscriptions are fixed during setup; only publish paths are measured.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class DomainEventPortBenchmarks
{
    private static readonly InstanceLogDomainEvent Event = new(
        Guid.Parse("4cd7f23d-4f15-40d5-835f-4aef5e7c3ad8"),
        "benchmark event");

    private EventPortHost _normalHost = null!;
    private EventPortHost _exceptionHost = null!;
    private EventPortHost _cancellationHost = null!;
    private CancellationTokenSource? _activeCancellationSource;
    private int _normalObservations;
    private int _exceptionObservations;
    private int _cancellationObservations;

    [Params(1, 8, 32)]
    public int Subscribers { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _normalHost = EventPortHost.Create();
        _exceptionHost = EventPortHost.Create();
        _cancellationHost = EventPortHost.Create();

        SubscribeNormal(_normalHost.Port, static benchmark => Interlocked.Increment(ref benchmark._normalObservations));
        SubscribeExceptionPath();
        SubscribeCancellationPath();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _normalHost.Dispose();
        _exceptionHost.Dispose();
        _cancellationHost.Dispose();
    }

    [Benchmark]
    public async ValueTask<int> PublishNormalFanOutAsync()
    {
        await _normalHost.Port.PublishAsync(Event);
        return Volatile.Read(ref _normalObservations);
    }

    /// <summary>
    /// Includes the deliberate per-operation exception allocation needed to measure the wrapper's swallowed-handler path.
    /// </summary>
    [Benchmark]
    public async ValueTask<int> PublishHandlerExceptionAsync()
    {
        await _exceptionHost.Port.PublishAsync(Event);
        return Volatile.Read(ref _exceptionObservations);
    }

    /// <summary>
    /// Measures end-to-end cancellation propagation, including cancellation-source creation, cancellation, and disposal.
    /// </summary>
    [Benchmark]
    public async ValueTask<int> PublishCancelledDuringFanOutAsync()
    {
        var observationStart = Volatile.Read(ref _cancellationObservations);
        using var source = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _activeCancellationSource, source, null) is not null)
            throw new InvalidOperationException("Cancellation benchmark invocations must not overlap.");

        try
        {
            await _cancellationHost.Port.PublishAsync(Event, source.Token);
        }
        catch (OperationCanceledException exception)
            when (exception.CancellationToken == source.Token && exception.CancellationToken.IsCancellationRequested)
        {
            var observed = Volatile.Read(ref _cancellationObservations) - observationStart;
            if (observed != Subscribers)
            {
                throw new InvalidOperationException(
                    $"Cancelled domain event fan-out invoked {observed} subscribers; expected {Subscribers}.");
            }

            return observed;
        }
        finally
        {
            if (!ReferenceEquals(Interlocked.Exchange(ref _activeCancellationSource, null), source))
                throw new InvalidOperationException("Cancellation benchmark lost its active cancellation source.");
        }

        throw new InvalidOperationException(
            "A domain event handler cancellation must propagate the matching publisher cancellation token.");
    }

    private void SubscribeExceptionPath()
    {
        var owner = _exceptionHost.Port.CreateOwner("benchmark-exception");
        _exceptionHost.Port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
            ValueTask.FromException(new BenchmarkHandlerException()));

        for (var index = 1; index < Subscribers; index++)
        {
            var normalOwner = _exceptionHost.Port.CreateOwner($"benchmark-exception-normal-{index}");
            _exceptionHost.Port.Subscribe<InstanceLogDomainEvent>(normalOwner, (_, _) =>
            {
                Interlocked.Increment(ref _exceptionObservations);
                return ValueTask.CompletedTask;
            });
        }
    }

    private void SubscribeCancellationPath()
    {
        for (var index = 0; index < Subscribers - 1; index++)
        {
            var owner = _cancellationHost.Port.CreateOwner($"benchmark-cancellation-normal-{index}");
            _cancellationHost.Port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
            {
                Interlocked.Increment(ref _cancellationObservations);
                return ValueTask.CompletedTask;
            });
        }

        var cancellingOwner = _cancellationHost.Port.CreateOwner("benchmark-cancellation-final");
        _cancellationHost.Port.Subscribe<InstanceLogDomainEvent>(cancellingOwner, (_, cancellationToken) =>
        {
            Interlocked.Increment(ref _cancellationObservations);
            var source = Volatile.Read(ref _activeCancellationSource) ??
                         throw new InvalidOperationException("Cancellation benchmark has no active cancellation source.");
            source.Cancel();
            throw new OperationCanceledException(cancellationToken);
        });
    }

    private void SubscribeNormal(DomainEventPort port, Action<DomainEventPortBenchmarks> observe)
    {
        for (var index = 0; index < Subscribers; index++)
        {
            var owner = port.CreateOwner($"benchmark-normal-{index}");
            port.Subscribe<InstanceLogDomainEvent>(owner, (_, _) =>
            {
                observe(this);
                return ValueTask.CompletedTask;
            });
        }
    }

    private sealed class BenchmarkHandlerException : Exception;

    private sealed class EventPortHost(ServiceProvider services) : IDisposable
    {
        internal DomainEventPort Port { get; } = services.GetRequiredService<DomainEventPort>();

        internal static EventPortHost Create()
        {
            var services = new ServiceCollection();
            services.AddMessagePipe(options =>
            {
                options.EnableAutoRegistration = false;
                options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
                options.InstanceLifetime = InstanceLifetime.Singleton;
                options.EnableCaptureStackTrace = false;
            });
            services.AddSingleton<ILogger<DomainEventPort>>(NullLogger<DomainEventPort>.Instance);
            services.AddSingleton(DomainEventDispatchPolicy.Default);
            services.AddSingleton<DomainEventPort>();
            return new EventPortHost(services.BuildServiceProvider());
        }

        public void Dispose() => services.Dispose();
    }
}
