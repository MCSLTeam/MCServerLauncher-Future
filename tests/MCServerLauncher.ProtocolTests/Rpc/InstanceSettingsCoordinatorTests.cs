using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using System.Diagnostics;
using RuntimeInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public class InstanceSettingsCoordinatorTests
{
    [Fact]
    public async Task GetInstanceSettings_StoppedInstance_ReturnsEditableSettings()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var instance = new FakeInstance(config, InstanceStatus.Stopped);
        manager.Instances[config.Uuid] = instance;

        var result = await manager.GetInstanceSettings(config.Uuid);

        Assert.True(result.IsOk(out var settings));
        Assert.True(settings.CanEdit);
        Assert.Equal(config.Name, settings.Config.Name);
        Assert.Equal(config.Target, settings.Config.Target);
        Assert.Equal(config.InstanceType, settings.Config.InstanceType);
    }

    [Fact]
    public async Task UpdateInstanceSettings_RunningInstance_IsRejected()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var instance = new FakeInstance(config, InstanceStatus.Running);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;

        var result = await manager.UpdateInstanceSettings(new UpdateInstanceSettingsRequest(
            config.Uuid,
            "updated-name",
            InstanceType.MCJava,
            config.JavaPath,
            config.Arguments.ToImmutableArray(),
            config.Version,
            null,
            false));

        Assert.True(result.IsErr(out var error));
        Assert.Contains("must be stopped", error!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryStartInstance_RegistersInstanceBeforeStartCompletes()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var instance = new FakeInstance(config, InstanceStatus.Running, startGate);
        manager.Instances[config.Uuid] = instance;

        var startTask = manager.TryStartInstance(config.Uuid);
        await instance.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));

        startGate.SetResult(true);
        var started = await startTask;

        Assert.Same(instance, started);
        Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));
    }

    [Fact]
    public async Task TryStartInstance_ReportShowsRunningAfterProcessStartSucceeds()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var instance = new FakeInstance(config, InstanceStatus.Stopped);
        manager.Instances[config.Uuid] = instance;

        var started = await manager.TryStartInstance(config.Uuid);
        var report = await manager.GetInstanceReport(config.Uuid);

        Assert.Same(instance, started);
        Assert.NotNull(report);
        Assert.Equal(InstanceStatus.Running, report.Status);
    }

    [Fact]
    public async Task InstanceProcess_MinecraftProcessStaysStoppedBeforeDoneLog()
    {
        var startInfo = CreateShortLivedProcessStartInfo();
        using var process = new InstanceProcess(startInfo, isMcServer: true);

        var started = await process.StartAsync(delayToCheck: 100);

        Assert.True(started);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
        process.KillProcess();
    }

    [Fact]
    public void InstanceStatus_ContainsOnlyStableLifecycleStates()
    {
        var names = Enum.GetNames<InstanceStatus>();

        Assert.DoesNotContain("Starting", names);
        Assert.DoesNotContain("Stopping", names);
        Assert.Contains("Running", names);
        Assert.Contains("Stopped", names);
        Assert.Contains("Crashed", names);
    }

    private static InstanceConfig CreateConfig(InstanceType type)
    {
        return new InstanceConfig
        {
            Name = "test-instance",
            Target = "server.jar",
            InstanceType = type,
            TargetType = TargetType.Jar,
            Version = type.RequiresNumericMinecraftVersion() ? "1.20.1" : string.Empty,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private static ProcessStartInfo CreateShortLivedProcessStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping -n 6 127.0.0.1 > nul",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"sleep 5\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
    }

    private sealed class FakeInstance : IInstance
    {
        private readonly TaskCompletionSource<bool>? _startGate;

        public FakeInstance(
            InstanceConfig config,
            InstanceStatus status,
            TaskCompletionSource<bool>? startGate = null)
        {
            Config = config;
            Status = status;
            _startGate = startGate;
        }

        public InstanceConfig Config { get; }
        public InstanceProcess? Process => null;
        public InstanceStatus Status { get; private set; }
        public int ServerProcessId => -1;
        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public Task<RuntimeInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new RuntimeInstanceReport(Status, Config, new Dictionary<string, string>(), [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            StartEntered.TrySetResult(true);
            if (_startGate is not null) return _startGate.Task;
            Status = InstanceStatus.Running;
            return Task.FromResult(true);
        }

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory()
        {
            return Array.Empty<string>();
        }

        public void Dispose()
        {
        }
    }
}
