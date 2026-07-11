using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using Microsoft.Extensions.Logging.Abstractions;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceDomainEventBridgeTests
{
    [Fact]
    public void InstanceManagerBridge_PublishesLogAndStatusUntilDisposed()
    {
        var manager = new InstanceManager();
        var instance = new EventingInstance(CreateConfig());
        manager.ReplaceInstance(instance.Config.Uuid, instance);
        var port = new DomainEventPort(NullLogger<DomainEventPort>.Instance);
        var logs = new List<InstanceLogDomainEvent>();
        var statuses = new List<InstanceStatusChangedDomainEvent>();
        using var logSubscription = port.Subscribe<InstanceLogDomainEvent>(logs.Add);
        using var statusSubscription = port.Subscribe<InstanceStatusChangedDomainEvent>(statuses.Add);
        var bridge = new InstanceDomainEventBridge(manager, port);

        instance.RaiseLog("started");
        instance.RaiseStatus(InstanceStatus.Running);

        bridge.Dispose();
        bridge.Dispose();
        instance.RaiseLog("ignored");
        instance.RaiseStatus(InstanceStatus.Stopped);

        var log = Assert.Single(logs);
        Assert.Equal(instance.Config.Uuid, log.InstanceId);
        Assert.Equal("started", log.Log);
        var status = Assert.Single(statuses);
        Assert.Equal(instance.Config.Uuid, status.InstanceId);
        Assert.Equal(InstanceStatus.Running, status.Status);
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
        public event Action<Guid, string>? OnLog;
        public event Action<Guid, InstanceStatus>? OnStatusChanged;

        public void RaiseLog(string log)
        {
            OnLog?.Invoke(Config.Uuid, log);
        }

        public void RaiseStatus(InstanceStatus status)
        {
            Status = status;
            OnStatusChanged?.Invoke(Config.Uuid, status);
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

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }
}
