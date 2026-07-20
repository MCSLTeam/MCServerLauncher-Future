using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceDomainEventBridgeTests
{
    [Fact]
    public async Task InstanceManagerBridge_PublishesLogAndStatusUntilDisposed()
    {
        var manager = new InstanceManager();
        var instance = new EventingInstance(CreateConfig());
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        using var portHost = DomainEventPortTestHost.Create();
        var port = portHost.Port;
        var owner = port.CreateOwner("bridge-test");
        var logs = new List<InstanceLogDomainEvent>();
        var statuses = new List<InstanceStatusChangedDomainEvent>();
        port.Subscribe<InstanceLogDomainEvent>(owner, (domainEvent, _) =>
        {
            logs.Add(domainEvent);
            return ValueTask.CompletedTask;
        });
        port.Subscribe<InstanceStatusChangedDomainEvent>(owner, (domainEvent, _) =>
        {
            statuses.Add(domainEvent);
            return ValueTask.CompletedTask;
        });
        var bridge = new InstanceDomainEventBridge(manager, port);

        await instance.RaiseLogAsync("started");
        await instance.RaiseStatusAsync(InstanceStatus.Running);

        bridge.Dispose();
        bridge.Dispose();
        await instance.RaiseLogAsync("ignored");
        await instance.RaiseStatusAsync(InstanceStatus.Stopped);

        var log = Assert.Single(logs);
        Assert.Equal(instance.Config.Uuid, log.InstanceId);
        Assert.Equal("started", log.Log);
        var status = Assert.Single(statuses);
        Assert.Equal(instance.Config.Uuid, status.InstanceId);
        Assert.Equal(InstanceStatus.Running, status.Status);
        port.DisposeOwner(owner);
    }

    [Fact]
    public async Task StatusBridge_ObservesAuthoritativeSnapshotCommitBeforePublication()
    {
        var manager = new InstanceManager();
        var instance = new EventingInstance(CreateConfig());
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        using var portHost = DomainEventPortTestHost.Create();
        var port = portHost.Port;
        var owner = port.CreateOwner("snapshot-order-test");
        InstanceStatus? publishedSnapshotStatus = null;
        port.Subscribe<InstanceStatusChangedDomainEvent>(owner, (domainEvent, _) =>
        {
            Assert.True(manager.InstanceSnapshotSource.Current.Value.Instances.TryGetValue(
                domainEvent.InstanceId,
                out var snapshot));
            publishedSnapshotStatus = snapshot.Status;
            return ValueTask.CompletedTask;
        });
        using var bridge = new InstanceDomainEventBridge(manager, port);

        await instance.RaiseStatusAsync(InstanceStatus.Running);

        Assert.Equal(InstanceStatus.Running, publishedSnapshotStatus);
        port.DisposeOwner(owner);
    }

    private static InstanceConfig CreateConfig()
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = "event-bridge-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            Version = "1.20.1",
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private sealed class EventingInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => null;
        public InstanceStatus Status { get; private set; } = InstanceStatus.Stopped;
        public int ServerProcessId => -1;
        public event Func<Guid, string, CancellationToken, Task>? OnLog;
        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

        public Task RaiseLogAsync(string log)
        {
            return OnLog?.Invoke(Config.Uuid, log, CancellationToken.None) ?? Task.CompletedTask;
        }

        public Task RaiseStatusAsync(InstanceStatus status)
        {
            Status = status;
            return OnStatusChanged?.Invoke(Config.Uuid, status, CancellationToken.None) ?? Task.CompletedTask;
        }

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LegacyInstanceReport(Status, Config, [], [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public void Stop()
        {
        }

        public void ForceKillAndClear() { }

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }
}
