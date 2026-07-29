using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Serialization;
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
    public async Task Query_DownsamplingKeepsEveryEventItCannotKeepASampleFor()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-event-downsample-").FullName;
        var start = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        var time = new ManualTimeProvider(start);
        var instance = new TestInstance(RunningId, "flapping-demo", InstanceStatus.Running);
        using var sampler = CreateSampler(root, time, instance);

        await sampler.SampleOnceAsync(CancellationToken.None); // baseline tick emits nothing
        var expected = 0;
        for (var index = 0; index < 40; index++)
        {
            instance.SetStatus(index % 2 == 0 ? InstanceStatus.Stopped : InstanceStatus.Running);
            time.Advance(TimeSpan.FromSeconds(15));
            var sample = await sampler.SampleOnceAsync(CancellationToken.None);
            expected += sample.Events!.Value.Length;
        }

        // Far fewer points than samples, so every bucket holds several ticks: the condition under
        // which keeping one sample per bucket used to discard the events the others carried.
        var downsampled = sampler.Query(
            new MonitoringQuery(start.AddMinutes(-1), time.GetUtcNow().AddMinutes(1), MaximumPoints: 5));

        Assert.True(downsampled.Samples.Length <= 5);
        Assert.Equal(40, expected);
        Assert.Equal(
            expected,
            downsampled.Samples.Sum(static sample => sample.Events?.Length ?? 0));
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

    [Fact]
    public async Task SampleOnce_RecordsDiskFromTheAlreadyCachedSystemInfo()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-disk-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        using var sampler = CreateSampler(root, time);

        var sample = await sampler.SampleOnceAsync(CancellationToken.None);

        Assert.Equal(1024UL, sample.DiskTotalBytes);
        Assert.Equal(512UL, sample.DiskFreeBytes);

        // The persisted record carries it too, not just the in-memory sample.
        var persisted = Assert.Single(sampler
            .Query(new MonitoringQuery(sample.Timestamp.AddMinutes(-1), sample.Timestamp.AddMinutes(1)))
            .Samples);
        Assert.Equal(1024UL, persisted.DiskTotalBytes);
        Assert.Equal(512UL, persisted.DiskFreeBytes);
    }

    [Fact]
    public async Task SampleOnce_RecordsStatusTransitionsBetweenTicksAsSignificantEvents()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-events-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var running = new TestInstance(RunningId, "running-demo", InstanceStatus.Running);
        var stopped = new TestInstance(StoppedId, "stopped-demo", InstanceStatus.Stopped);
        using var sampler = CreateSampler(root, time, running, stopped);

        // The first tick only establishes a baseline: a restart must not manufacture transitions.
        var baseline = await sampler.SampleOnceAsync(CancellationToken.None);
        Assert.Empty(baseline.Events!.Value);

        running.SetStatus(InstanceStatus.Crashed);
        stopped.SetStatus(InstanceStatus.Starting);
        time.Advance(TimeSpan.FromSeconds(15));
        var transitioned = await sampler.SampleOnceAsync(CancellationToken.None);

        Assert.Equal(2, transitioned.Events!.Value.Length);
        var crash = Assert.Single(transitioned.Events!.Value, entry => entry.InstanceId == RunningId);
        Assert.Equal(MonitoringEventKind.StatusChanged, crash.Kind);
        Assert.Equal(InstanceStatus.Running, crash.PreviousStatus);
        Assert.Equal(InstanceStatus.Crashed, crash.Status);
        var starting = Assert.Single(transitioned.Events!.Value, entry => entry.InstanceId == StoppedId);
        Assert.Equal(InstanceStatus.Stopped, starting.PreviousStatus);
        Assert.Equal(InstanceStatus.Starting, starting.Status);

        // A steady tick reports nothing: events are transitions, not repeated state.
        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Empty((await sampler.SampleOnceAsync(CancellationToken.None)).Events!.Value);
    }

    [Fact]
    public async Task SampleOnce_NeverRequestsAnInstanceReport()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-passive-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var running = new TestInstance(RunningId, "running-demo", InstanceStatus.Running);
        var stopped = new TestInstance(StoppedId, "stopped-demo", InstanceStatus.Stopped);
        using var sampler = CreateSampler(root, time, running, stopped);

        for (var tick = 0; tick < 3; tick++)
        {
            await sampler.SampleOnceAsync(CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(15));
        }

        // GetReportAsync issues a real server ping per instance per tick. Sampling reads the
        // 2-second cached process counters instead, and must keep doing so.
        Assert.Equal(0, Volatile.Read(ref running.ReportCalls));
        Assert.Equal(0, Volatile.Read(ref stopped.ReportCalls));
    }

    [Fact]
    public async Task Responsiveness_MeasuresSilenceOnlyWhileTheInstanceIsRunning()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-silence-").FullName;
        using var process = new InstanceProcess(CreateReadyThenWaitingStartInfo(), InstanceType.Universal);
        await StartAndAwaitOutputAsync(process);

        Assert.NotNull(process.LastOutputAt);

        var instance = new TestInstance(RunningId, "running-demo", InstanceStatus.Running) { Process = process };
        using var sampler = CreateSampler(root, TimeProvider.System, instance);

        var running = Assert.Single((await sampler.SampleOnceAsync(CancellationToken.None)).Instances);
        Assert.NotNull(running.SilentSeconds);
        Assert.InRange(running.SilentSeconds!.Value, 0, 120);

        // Silence carries no meaning once the instance is no longer Running: recording a growing
        // number there would let a responsiveness trigger fire on a process that simply stopped.
        instance.SetStatus(InstanceStatus.Stopped);
        var notRunning = Assert.Single((await sampler.SampleOnceAsync(CancellationToken.None)).Instances);
        Assert.Null(notRunning.SilentSeconds);
    }

    [Fact]
    public async Task Responsiveness_IsUnmeasuredWhenTheInstanceHasProducedNoOutput()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-silent-start-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        // No process at all: nothing observed the instance's output, so the honest answer is null.
        using var sampler = CreateSampler(root, time, new TestInstance(RunningId, "running-demo", InstanceStatus.Running));

        var entry = Assert.Single((await sampler.SampleOnceAsync(CancellationToken.None)).Instances);
        Assert.Equal(InstanceStatus.Running, entry.Status);
        Assert.Null(entry.SilentSeconds);
    }

    [Fact]
    public async Task ReadyTimeout_IsRecordedOnceAsASignificantEvent()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-ready-timeout-").FullName;
        // The Minecraft observer never treats process start as ready, so the instance stays in
        // Starting until its ready deadline elapses.
        using var process = new InstanceProcess(
            CreateReadyThenWaitingStartInfo(),
            MinecraftInstanceLifecycleObserver.Instance,
            ConsoleMode.Pipe,
            monitorFrequency: 2000,
            TimeProvider.System,
            readyTimeout: TimeSpan.FromMilliseconds(100));
        await StartAndAwaitOutputAsync(process);
        await WaitUntilAsync(() => process.ReadyTimedOut);

        var instance = new TestInstance(RunningId, "starting-demo", InstanceStatus.Starting) { Process = process };
        using var sampler = CreateSampler(root, TimeProvider.System, instance);

        var first = Assert.Single((await sampler.SampleOnceAsync(CancellationToken.None)).Events!.Value);
        Assert.Equal(RunningId, first.InstanceId);
        Assert.Equal(MonitoringEventKind.ReadyTimeout, first.Kind);
        Assert.Equal(InstanceStatus.Starting, first.Status);
        Assert.Null(first.PreviousStatus);

        // The flag latches on the process; repeating it every tick would drown the history in one
        // stuck start.
        Assert.Empty((await sampler.SampleOnceAsync(CancellationToken.None)).Events!.Value);
    }

    [Fact]
    public async Task Gap_CarriesNullForEveryMetricItNeverCollected()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-gap-nulls-").FullName;
        var start = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        var time = new ManualTimeProvider(start);
        using (var first = CreateSampler(root, time))
        {
            await first.SampleOnceAsync(CancellationToken.None);
        }

        time.Advance(TimeSpan.FromMinutes(10));
        using var afterHole = CreateSampler(root, time);

        var samples = afterHole.Query(new MonitoringQuery(start.AddMinutes(-1), start.AddMinutes(30))).Samples;
        var gap = Assert.Single(samples, sample => sample.Gap);
        // A hole is time nobody observed. Zero would be a measurement, and a sustained disk trigger
        // reading 0 free bytes off a gap would fire on evidence that was never collected.
        Assert.Null(gap.DiskTotalBytes);
        Assert.Null(gap.DiskFreeBytes);
        Assert.Null(gap.Events);
    }

    [Fact]
    public async Task Retention_TheWiderRecordLeavesTheAgeFloorInChargeAtTheDefaultConfig()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-retention-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var config = new DaemonMonitoringConfig();
        using var sampler = CreateSampler(root, time);

        await sampler.SampleOnceAsync(CancellationToken.None);
        var recordBytes = Directory.EnumerateFiles(root, "metrics-*.jsonl").Sum(path => new FileInfo(path).Length);

        // Every metric added widens the record, and a fixed byte cap therefore retains fewer of
        // them. At the default cadence a full retention window holds this many records, so the cap
        // only starts deciding retention once one record exceeds cap/records. Keeping a normal
        // record far below that line is what keeps the age floor — not the byte cap — in charge.
        var recordsPerWindow = TimeSpan.FromDays(config.RetentionDays).Ticks / sampler.SampleInterval.Ticks;
        var projected = recordBytes * recordsPerWindow;
        Assert.True(
            projected < config.MaximumBytes,
            $"A {recordBytes}-byte record over {recordsPerWindow} records projects to {projected} bytes, " +
            $"past the {config.MaximumBytes}-byte cap: the byte cap would start truncating the retention window.");
    }

    [Fact]
    public void Json_RoundTripsEveryMetricAddedToTheSample()
    {
        var sample = new MonitoringSample(
            DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"),
            Gap: false,
            5.5,
            16384,
            32768,
            [new MonitoringInstanceSample(RunningId, "running-demo", InstanceStatus.Running, 12.5, 1024, 42.5)],
            DiskTotalBytes: 1024,
            DiskFreeBytes: 512,
            Events:
            [
                new MonitoringInstanceEvent(RunningId, MonitoringEventKind.StatusChanged, InstanceStatus.Crashed, InstanceStatus.Running),
                new MonitoringInstanceEvent(StoppedId, MonitoringEventKind.ReadyTimeout, InstanceStatus.Starting, null),
            ]);

        var json = JsonSerializer.Serialize(sample, ApplicationContractJsonContext.Default.MonitoringSample);
        var restored = JsonSerializer.Deserialize(json, ApplicationContractJsonContext.Default.MonitoringSample);

        Assert.NotNull(restored);
        Assert.Equal(1024UL, restored.DiskTotalBytes);
        Assert.Equal(512UL, restored.DiskFreeBytes);
        Assert.Equal(42.5, Assert.Single(restored.Instances).SilentSeconds);
        Assert.NotNull(restored.Events);
        Assert.Equal(2, restored.Events!.Value.Length);
        Assert.Equal(
            new MonitoringInstanceEvent(RunningId, MonitoringEventKind.StatusChanged, InstanceStatus.Crashed, InstanceStatus.Running),
            restored.Events!.Value[0]);
        Assert.Equal(
            new MonitoringInstanceEvent(StoppedId, MonitoringEventKind.ReadyTimeout, InstanceStatus.Starting, null),
            restored.Events!.Value[1]);

        // The wire names are part of the contract, and the events are lifecycle only.
        Assert.Contains("\"disk_total_bytes\":1024", json);
        Assert.Contains("\"disk_free_bytes\":512", json);
        Assert.Contains("\"silent_seconds\":42.5", json);
        Assert.Contains("\"kind\":\"status_changed\"", json);
        Assert.Contains("\"kind\":\"ready_timeout\"", json);
        Assert.Contains("\"previous_status\":null", json);
    }

    [Fact]
    public void Json_ARecordWrittenBeforeTheWideningReadsAsUnmeasuredNotAsZero()
    {
        // Byte-for-byte the shape the sampler wrote before disk, responsiveness and events existed.
        const string legacy =
            """
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[{"instance_id":"11111111-1111-1111-1111-111111111111","name":"running-demo","status":"running","cpu_percent":12.5,"memory_bytes":1024}]}
            """;

        var restored = JsonSerializer.Deserialize(legacy, ApplicationContractJsonContext.Default.MonitoringSample);

        Assert.NotNull(restored);
        // The whole point: "never collected" must not be indistinguishable from "measured zero".
        Assert.Null(restored.DiskTotalBytes);
        Assert.Null(restored.DiskFreeBytes);
        Assert.Null(restored.Events);
        Assert.Null(Assert.Single(restored.Instances).SilentSeconds);
        Assert.NotEqual(0UL, restored.DiskFreeBytes.GetValueOrDefault(ulong.MaxValue));

        // The fields that always existed still read normally.
        Assert.Equal(5.5, restored.SystemCpuPercent);
        Assert.Equal(16384UL, restored.MemoryUsedKilobytes);
        Assert.Equal(InstanceStatus.Running, Assert.Single(restored.Instances).Status);
    }

    [Fact]
    public void Query_ReadsAHistoryWrittenBeforeTheWideningWithoutInventingZeros()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-monitoring-legacy-history-").FullName;
        var start = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        // A segment exactly as the previous daemon build left it on disk.
        File.WriteAllText(
            Path.Combine(root, "metrics-000000.jsonl"),
            """
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[{"instance_id":"11111111-1111-1111-1111-111111111111","name":"running-demo","status":"running","cpu_percent":12.5,"memory_bytes":1024}]}
            """ + "\n");

        // Within two intervals of the newest record, so opening the log records no startup gap.
        var time = new ManualTimeProvider(start.AddSeconds(10));
        using var sampler = CreateSampler(root, time);

        var restored = Assert.Single(sampler.Query(new MonitoringQuery(start.AddMinutes(-1), start.AddMinutes(1))).Samples);
        Assert.False(restored.Gap);
        Assert.Equal(5.5, restored.SystemCpuPercent);
        // The record survived the crash-recovery and read paths intact, and everything it never
        // measured still reads as unmeasured rather than as a reading of zero.
        Assert.Null(restored.DiskTotalBytes);
        Assert.Null(restored.DiskFreeBytes);
        Assert.Null(restored.Events);
        Assert.Null(Assert.Single(restored.Instances).SilentSeconds);
    }

    [Fact]
    public async Task InstanceProcess_LastOutputAtStampsTheNewestOutputLine()
    {
        using var process = new InstanceProcess(CreateReadyThenWaitingStartInfo(), InstanceType.Universal);
        Assert.Null(process.LastOutputAt);

        var before = DateTimeOffset.UtcNow;
        await StartAndAwaitOutputAsync(process);

        var stamped = process.LastOutputAt;
        Assert.NotNull(stamped);
        Assert.InRange(stamped!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    private static async Task StartAndAwaitOutputAsync(InstanceProcess process)
    {
        var sawOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += (message, _) =>
        {
            if (message == "ready")
                sawOutput.TrySetResult();
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 10));
        await sawOutput.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(condition());
    }

    private static ProcessStartInfo CreateReadyThenWaitingStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo ready&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'ready\\n'; read line");
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static MonitoringSampler CreateSampler(string root, TimeProvider time) =>
        CreateSampler(
            root,
            time,
            new TestInstance(RunningId, "running-demo", InstanceStatus.Running),
            new TestInstance(StoppedId, "stopped-demo", InstanceStatus.Stopped));

    private static MonitoringSampler CreateSampler(string root, TimeProvider time, params TestInstance[] instances)
    {
        var manager = new FakeInstanceManager();
        foreach (var instance in instances)
            manager.Instances[instance.Config.Uuid] = instance;
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
        private InstanceStatus _status = status;

        /// <summary>
        /// How many times anything asked this instance for a report. The monitoring sampler must
        /// leave it at zero: a report pings the game server, and sampling is passive by contract.
        /// </summary>
        internal int ReportCalls;

        public InstanceConfig Config { get; } = new() { Uuid = id, Name = name, Target = "server.jar" };

        public InstanceProcess? Process { get; init; }

        public InstanceStatus Status => _status;

        public int ServerProcessId => -1;

        internal void SetStatus(InstanceStatus next) => _status = next;

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

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReportCalls);
            throw new InvalidOperationException(
                "The monitoring sampler must never request an instance report: it issues a real server ping.");
        }

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

        public Guid? AttachConsole(Guid instanceId, Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler, bool replayHistory = true) => throw new NotSupportedException();

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
