using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
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
        var observedCatalogChanges = 0;
        var catalogObserver = domainEvents.Port.CreateOwner("catalog-lifecycle-test");
        domainEvents.Port.Subscribe<InstanceCatalogChangedDomainEvent>(catalogObserver, (_, _) =>
        {
            Interlocked.Increment(ref observedCatalogChanges);
            return ValueTask.CompletedTask;
        });
        var instance = new FaultingProcessInstance(CreateConfig());
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        using var bridge = new InstanceDomainEventBridge(manager, domainEvents.Port);
        await using var trigger = new EventTriggerService(
            new StubDaemonApplication(),
            domainEvents.Port,
            NullLogger<EventTriggerService>.Instance);
        var catalogBridge = new InstanceCatalogDomainEventBridge(manager.CatalogCommitFeed, domainEvents.Port);
        catalogBridge.Start();
        var lifecycle = new LocalDaemonRuntimeLifecycle(
            new FileSessionCoordinator(),
            manager,
            manager.MutationAdmission,
            new DaemonReportPublisher(
                new StubDaemonApplication(),
                domainEvents.Port,
                NullLogger<DaemonReportPublisher>.Instance),
            trigger,
            bridge,
            manager.CatalogCommitFeed,
            catalogBridge,
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
        var lastAdmission = manager.MutationAdmission.EnterExternal();

        var stopTask = lifecycle.StopAsync(cancellation.Token);
        Assert.False(stopTask.IsCompleted);
        var finalConfig = CreateConfig("last-admitted-catalog-commit");
        finalConfig.Uuid = instance.Config.Uuid;
        var replacement = new FaultingProcessInstance(finalConfig);
        manager.ReplaceInstanceWithinAdmission(finalConfig.Uuid, replacement);
        lastAdmission.Dispose();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => stopTask);

        Assert.Contains(exception.Flatten().InnerExceptions, inner => inner is InvalidOperationException);
        Assert.Equal(2, Volatile.Read(ref observedCatalogChanges));
        Assert.Equal(2, manager.InstanceSnapshotSource.Current.Version);
        await replacement.RaiseLogAsync("after shutdown");
        Assert.Equal(0, Volatile.Read(ref observedLogs));
        domainEvents.Port.DisposeOwner(observer);
        domainEvents.Port.DisposeOwner(catalogObserver);
    }

    private static InstanceConfig CreateConfig(string name = "lifecycle-test")
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = name,
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
}
