using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.ProtocolTests;

public sealed class DaemonRuntimeLifecycleTests
{
    [Fact]
    public async Task StopAsync_InstanceDrainFailure_StillDisposesEventBridge()
    {
        using var domainEvents = DomainEventPortTestHost.Create();
        var manager = new InstanceManager();
        var instance = new FaultingProcessInstance(CreateConfig());
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        using var bridge = new InstanceDomainEventBridge(manager, domainEvents.Port);
        await using var trigger = new EventTriggerService(
            new StubDaemonApplication(),
            domainEvents.Port,
            NullLogger<EventTriggerService>.Instance);
        await using var legacyAdapter = new LegacyDomainEventAdapter(
            domainEvents.Port,
            new EventService(),
            new WsContextContainer(),
            NullLogger<LegacyDomainEventAdapter>.Instance);
        var queueControl = new LegacyEventQueueControl();
        queueControl.Attach(new CompletingQueueParticipant());
        var lifecycle = new LocalDaemonRuntimeLifecycle(
            new FileSessionCoordinator(),
            manager,
            new DaemonReportPublisher(
                new StubDaemonApplication(),
                domainEvents.Port,
                NullLogger<DaemonReportPublisher>.Instance),
            trigger,
            legacyAdapter,
            bridge,
            queueControl,
            NullLogger<LocalDaemonRuntimeLifecycle>.Instance);
        var observedLogs = 0;
        var observer = domainEvents.Port.CreateOwner(nameof(DaemonRuntimeLifecycleTests));
        domainEvents.Port.Subscribe<InstanceLogDomainEvent>(observer, (_, _) =>
        {
            Interlocked.Increment(ref observedLogs);
            return ValueTask.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => lifecycle.StopAsync(cancellation.Token));

        Assert.Contains(exception.Flatten().InnerExceptions, inner => inner is InvalidOperationException);
        await instance.RaiseLogAsync("after shutdown");
        Assert.Equal(0, Volatile.Read(ref observedLogs));
        domainEvents.Port.DisposeOwner(observer);
    }

    private static InstanceConfig CreateConfig()
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = "lifecycle-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private sealed class StubDaemonApplication : MCServerLauncher.Daemon.API.Application.IDaemonApplication
    {
        public MCServerLauncher.Daemon.API.Application.IInstanceApplication Instances => null!;
        public MCServerLauncher.Daemon.API.Application.IFileApplication Files => null!;
        public MCServerLauncher.Daemon.API.Application.ISystemApplication System => null!;
        public MCServerLauncher.Daemon.API.Application.IEventRuleApplication EventRules => null!;
    }

    private sealed class FaultingProcessInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => throw new InvalidOperationException("process drain failed");
        public InstanceStatus Status => InstanceStatus.Running;
        public int ServerProcessId => -1;
        public event Func<Guid, string, CancellationToken, Task>? OnLog;
        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new InstanceReport(Status, Config, [], [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }

        public Task RaiseLogAsync(string log)
        {
            return OnLog?.Invoke(Config.Uuid, log, CancellationToken.None) ?? Task.CompletedTask;
        }
    }

    private sealed class CompletingQueueParticipant : ILegacyEventQueueParticipant
    {
        public void StopAccepting()
        {
        }

        public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
