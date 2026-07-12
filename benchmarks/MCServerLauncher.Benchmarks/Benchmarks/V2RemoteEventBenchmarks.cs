using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class V2RemoteEventBenchmarks
{
    private ServiceProvider _services = null!;
    private DomainEventPort _domainEvents = null!;
    private V2RemoteEventBridge _bridge = null!;
    private V2ConnectionOwner[] _owners = null!;
    private InstanceLogDomainEvent _domainEvent = null!;
    private JsonRpcRemoteEventNotification _comparisonNotification = null!;
    private SendCoordinator _sendCoordinator = null!;

    [Params(1, 8, 32)]
    public int MatchingConnections { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
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
        _services = services.BuildServiceProvider();
        _domainEvents = _services.GetRequiredService<DomainEventPort>();

        var catalog = CreateCatalog();
        var registry = new V2EventConnectionRegistry(catalog);
        _sendCoordinator = new SendCoordinator();
        _owners = new V2ConnectionOwner[MatchingConnections];
        for (var index = 0; index < MatchingConnections; index++)
        {
            var owner = new V2ConnectionOwner(new CountingSender(_sendCoordinator), ["*"]);
            if (registry.TryAttach($"benchmark-{index}", owner, out var entry) != V2EventConnectionAttachResult.Attached)
                throw new InvalidOperationException("Benchmark connection attachment failed.");
            if (entry!.Ledger.Subscribe(new EventSubscriptionRequest("mcsl.event.instance.log")).IsErr(out _))
                throw new InvalidOperationException("Benchmark event subscription failed.");
            _owners[index] = owner;
            _ = owner.Start();
        }

        _bridge = new V2RemoteEventBridge(_domainEvents, catalog, registry, TimeProvider.System);
        var instanceId = Guid.Parse("c8a9d4ee-27f3-4ba0-97be-1eb05c5952d4");
        _domainEvent = new InstanceLogDomainEvent(instanceId, "benchmark log line");
        _comparisonNotification = new JsonRpcRemoteEventNotification(
            "mcsl.event.instance.log",
            new JsonRpcRemoteEventParameters(
                1,
                1_783_677_000_000,
                JsonRpcOptionalPayload.From(
                    new InstanceLogEventMeta(instanceId),
                    BuiltInProtocolJsonContext.Default.InstanceLogEventMeta),
                JsonRpcOptionalPayload.From(
                    new InstanceLogEventData(_domainEvent.Log),
                    BuiltInProtocolJsonContext.Default.InstanceLogEventData)));
    }

    [Benchmark]
    public async Task PublishThroughBridgeAsync()
    {
        await _domainEvents.PublishAsync(_domainEvent);
        await _sendCoordinator.WaitAsync(MatchingConnections);
    }

    [Benchmark(Baseline = true)]
    public int SerializeEnvelopePerSubscriber()
    {
        var length = 0;
        for (var index = 0; index < MatchingConnections; index++)
        {
            length += JsonSerializer.SerializeToUtf8Bytes(
                _comparisonNotification,
                BuiltInProtocolJsonContext.Default.JsonRpcRemoteEventNotification).Length;
        }

        return length;
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _bridge.Dispose();
        foreach (var owner in _owners)
            await owner.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static FrozenProtocolCatalog CreateCatalog()
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("benchmark", "1.0.0"));
        foreach (var descriptor in BuiltInProtocolDefinitions.Events)
        {
            if (descriptor.DataTypeInfo.Type == typeof(InstanceCatalogChangedEventData))
                builder.RegisterBuiltInEvent(descriptor, new EventBinding<InstanceCatalogChangedEventData>(ProtocolExecutionOwner.BuiltIn));
            else if (descriptor.DataTypeInfo.Type == typeof(DaemonReportEventData))
                builder.RegisterBuiltInEvent(descriptor, new EventBinding<DaemonReportEventData>(ProtocolExecutionOwner.BuiltIn));
            else if (descriptor.DataTypeInfo.Type == typeof(InstanceLogEventData))
                builder.RegisterBuiltInEvent(descriptor, new EventBinding<InstanceLogEventData, InstanceLogEventMeta>(ProtocolExecutionOwner.BuiltIn));
            else if (descriptor.DataTypeInfo.Type == typeof(NotificationEventData))
                builder.RegisterBuiltInEvent(descriptor, new EventBinding<NotificationEventData, NotificationEventMeta>(ProtocolExecutionOwner.BuiltIn));
        }

        return builder.Freeze();
    }

    private sealed class CountingSender(SendCoordinator coordinator) : IV2OutboundSender
    {
        private long _bytes;

        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken)
        {
            Interlocked.Add(ref _bytes, frame.Payload.Length);
            coordinator.Signal();
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class SendCoordinator
    {
        private readonly SemaphoreSlim _sent = new(0);

        internal void Signal() => _sent.Release();

        internal async Task WaitAsync(int count)
        {
            for (var index = 0; index < count; index++)
                await _sent.WaitAsync();
        }
    }
}
