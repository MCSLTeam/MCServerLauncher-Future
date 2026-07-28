using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Monitoring;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;
using ContractInstanceConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceConfiguration;
using InstanceFactoryConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceFactoryConfiguration;
using InstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult;
using UpdateInstanceSettingsRequest = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest;
using UpdateInstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult;

namespace MCServerLauncher.ProtocolTests;

public sealed class MonitoringSamplerTests
{
    private static readonly Guid RunningId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StoppedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SampleOnce_RecordsSystemAndInstanceMetrics()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-sample-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        using var sampler = CreateSampler(root, time);

        var sample = await sampler.SampleOnceAsync(CancellationToken.None);

        Assert.Equal(time.GetUtcNow(), sample.Timestamp);
        Assert.False(sample.Gap);
        Assert.Equal(5.5, sample.SystemCpuPercent);
        Assert.Equal(16384UL, sample.MemoryUsedKilobytes);
        Assert.Equal(32768UL, sample.MemoryTotalKilobytes);
        Assert.Equal(2, sample.Instances.Length);
        Assert.Equal(RunningId, sample.Instances[0].InstanceId);
        Assert.Equal("running-demo", sample.Instances[0].Name);
        Assert.Equal(InstanceStatus.Running, sample.Instances[0].Status);
        Assert.Equal(InstanceStatus.Stopped, sample.Instances[1].Status);
        Assert.Same(sample, sampler.Latest);

        var queried = sampler.Query(new MonitoringQuery(
            sample.Timestamp.AddMinutes(-1),
            sample.Timestamp.AddMinutes(1)));
        Assert.Equal(sample.Timestamp, Assert.Single(queried.Samples).Timestamp);
        Assert.Equal(0, queried.DroppedRecords);
    }

    [Fact]
    public async Task Restart_MarksTheHoleWithASingleGapRecord()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-gap-").FullName;
        var start = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        var time = new ManualTimeProvider(start);
        using (var first = CreateSampler(root, time))
        {
            await first.SampleOnceAsync(CancellationToken.None);
        }

        // Restart within two intervals: no gap is recorded.
        time.Advance(TimeSpan.FromSeconds(20));
        using (var quickRestart = CreateSampler(root, time))
        {
            var noGap = quickRestart.Query(new MonitoringQuery(start.AddMinutes(-1), start.AddMinutes(30)));
            Assert.All(noGap.Samples, sample => Assert.False(sample.Gap));
        }

        // Restart after a real hole: exactly one structured gap record marks it.
        time.Advance(TimeSpan.FromMinutes(10));
        using var afterHole = CreateSampler(root, time);
        var samples = afterHole.Query(new MonitoringQuery(start.AddMinutes(-1), start.AddMinutes(30))).Samples;
        Assert.Equal(2, samples.Length);
        Assert.False(samples[0].Gap);
        Assert.True(samples[1].Gap);

        // A gap marker is idempotent: another restart on top of the gap adds nothing.
        time.Advance(TimeSpan.FromMinutes(10));
        using var secondRestart = CreateSampler(root, time);
        Assert.Equal(2, secondRestart.Query(new MonitoringQuery(start.AddMinutes(-1), start.AddMinutes(60))).Samples.Length);
    }

    [Fact]
    public async Task Query_DownsamplesDeterministicallyAndNeverHidesGaps()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-downsample-").FullName;
        var start = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        var time = new ManualTimeProvider(start);
        using (var writer = CreateSampler(root, time))
        {
            for (var index = 0; index < 120; index++)
            {
                await writer.SampleOnceAsync(CancellationToken.None);
                time.Advance(TimeSpan.FromSeconds(15));
            }
        }

        time.Advance(TimeSpan.FromMinutes(30));
        using var sampler = CreateSampler(root, time); // appends a gap record after the hole

        var query = new MonitoringQuery(start, time.GetUtcNow(), MaximumPoints: 10);
        var first = sampler.Query(query);
        var second = sampler.Query(query);

        Assert.True(first.Samples.Length <= 10);
        Assert.Equal(first.Samples.Select(static sample => sample.Timestamp), second.Samples.Select(static sample => sample.Timestamp));
        Assert.Contains(first.Samples, static sample => sample.Gap);

        // Under the cap the raw series is returned untouched.
        var raw = sampler.Query(new MonitoringQuery(start, start.AddMinutes(2)));
        Assert.Equal(9, raw.Samples.Length);

        // An inverted window yields nothing rather than throwing.
        Assert.Empty(sampler.Query(new MonitoringQuery(start.AddDays(1), start)).Samples);
    }

    [Fact]
    public async Task LocalApplication_ExposesCurrentAndQuery()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-app-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        using var sampler = CreateSampler(root, time);
        var application = new LocalMonitoringApplication(sampler);

        var beforeFirstTick = (await application.GetCurrentAsync(CancellationToken.None)).Unwrap();
        Assert.Null(beforeFirstTick.Sample);

        var sample = await sampler.SampleOnceAsync(CancellationToken.None);
        var current = (await application.GetCurrentAsync(CancellationToken.None)).Unwrap();
        Assert.Equal(sample.Timestamp, current.Sample!.Timestamp);

        var queried = (await application.QueryAsync(
            new MonitoringQuery(sample.Timestamp.AddMinutes(-1), sample.Timestamp.AddMinutes(1)),
            CancellationToken.None)).Unwrap();
        Assert.Single(queried.Samples);
    }

    private static MonitoringSampler CreateSampler(string root, TimeProvider time)
    {
        var manager = new FakeInstanceManager();
        manager.Instances[RunningId] = new TestInstance(RunningId, "running-demo", InstanceStatus.Running);
        manager.Instances[StoppedId] = new TestInstance(StoppedId, "stopped-demo", InstanceStatus.Stopped);
        return new MonitoringSampler(
            new DaemonMonitoringConfig(),
            manager,
            new FixedSystemInfoCell(new SystemInfo(
                new OperatingSystemInfo("Windows", "x64"),
                new ProcessorInfo("vendor", "cpu", 16, 5.5, 8, 16),
                new MemoryInfo(32768, 16384),
                new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1024, 512, "C:\\"),
                [new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1024, 512, "C:\\")],
                "2.0.0")),
            time,
            root);
    }

    private sealed class FixedSystemInfoCell(SystemInfo info) : IAsyncTimedLazyCell<SystemInfo>
    {
        public ValueTask<SystemInfo> Value => ValueTask.FromResult(info);

        public DateTime LastUpdated => DateTime.UtcNow;

        public TimeSpan CacheDuration => TimeSpan.FromSeconds(2);

        public bool IsExpired() => false;

        public Task Update() => Task.CompletedTask;
    }

    private sealed class TestInstance(Guid id, string name, InstanceStatus status) : IInstance
    {
        public InstanceConfig Config { get; } = new() { Uuid = id, Name = name, Target = "server.jar" };

        public InstanceProcess? Process => null;

        public InstanceStatus Status => status;

        public int ServerProcessId => -1;

        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }

        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> StopAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private sealed class FakeInstanceManager : IInstanceManager
    {
        public ConcurrentDictionary<Guid, IInstance> Instances { get; } = new();

        public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; } = new();

        public Task<Result<ContractInstanceConfiguration, DaemonError>> TryAddInstance(InstanceFactoryConfiguration setting, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> TryRemoveInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IInstance?> TryStartInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> TryStopInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public bool SendToInstance(Guid instanceId, string message) => throw new NotSupportedException();

        public bool TryWriteConsole(Guid instanceId, ReadOnlyMemory<byte> data) => throw new NotSupportedException();

        public bool TryResizeConsole(Guid instanceId, ushort columns, ushort rows) => throw new NotSupportedException();

        public Guid? AttachConsole(Guid instanceId, Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler) => throw new NotSupportedException();

        public void DetachConsole(Guid instanceId, Guid subscriberId) => throw new NotSupportedException();

        public Task KillInstanceAsync(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<InstanceReport?> GetInstanceReport(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Dictionary<Guid, InstanceReport>> GetAllReports(CancellationToken ct = default) => throw new NotSupportedException();

        public bool TryGetInstanceLog(Guid instanceId, out IReadOnlyList<string> logs) => throw new NotSupportedException();

        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettings(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettings(UpdateInstanceSettingsRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public IDisposable AcquireInstanceMutation(Guid instanceId) => throw new NotSupportedException();

        public ValueTask<IDisposable> AcquireInstanceMutationAsync(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task StopAllInstances(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
