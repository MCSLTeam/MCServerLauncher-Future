using System.Diagnostics;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.ProtocolTests;

public class InstanceManagerKillTests
{
    [Theory]
    [InlineData(InstanceStatus.Stopped)]
    [InlineData(InstanceStatus.Crashed)]
    public async Task KillInstance_AfterTryStopInstance_KillsLiveProcessRegardlessOfStatus(InstanceStatus status)
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateLongRunningProcessStartInfo(), InstanceType.MCJava);
        var started = false;

        try
        {
            started = await process.StartAsync(delayToCheck: 100);
            Assert.True(started);

            var instance = new TestInstance(config, status, () => process);
            manager.Instances[config.Uuid] = instance;
            manager.RunningInstances[config.Uuid] = instance;

            Assert.True(await manager.TryStopInstance(config.Uuid));
            // Intermediate Stopping keeps the instance mapped until a terminal status arrives.
            Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));
            Assert.False(process.HasExit);

            await manager.KillInstanceAsync(config.Uuid);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.HasExit);
        }
        finally
        {
            if (started && !process.HasExit)
                process.KillProcess();
        }
    }

    [Fact]
    public async Task KillInstance_MissingInstance_IsNoOp()
    {
        var manager = new InstanceManager();

        var exception = await Record.ExceptionAsync(() => manager.KillInstanceAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task KillInstance_NullProcess_IsNoOpAndReadsProcessOnce()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var processReads = 0;
        manager.Instances[config.Uuid] = new TestInstance(
            config,
            InstanceStatus.Stopped,
            () =>
            {
                processReads++;
                return null;
            });

        var exception = await Record.ExceptionAsync(() => manager.KillInstanceAsync(config.Uuid));

        Assert.Null(exception);
        Assert.Equal(1, processReads);
    }

    [Fact]
    public async Task KillInstance_UsesSingleCapturedProcessReference()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateLongRunningProcessStartInfo(), InstanceType.Universal);
        var processReads = 0;
        var started = false;

        try
        {
            started = await process.StartAsync(delayToCheck: 100);
            Assert.True(started);
            manager.Instances[config.Uuid] = new TestInstance(
                config,
                InstanceStatus.Stopped,
                () =>
                {
                    processReads++;
                    return processReads == 1
                        ? process
                        : throw new Xunit.Sdk.XunitException("Process must be captured only once.");
                });

            await manager.KillInstanceAsync(config.Uuid);

            Assert.Equal(1, processReads);
            Assert.True(process.HasExit);
        }
        finally
        {
            if (started && !process.HasExit)
                process.KillProcess();
        }
    }

    [Fact]
    public async Task KillInstance_ProcessFailure_PropagatesAndPreservesBinding()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.Instances[config.Uuid] = new TestInstance(
            config,
            InstanceStatus.Stopped,
            () => throw new InvalidOperationException("process accessor failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.KillInstanceAsync(config.Uuid));
        Assert.Equal("process accessor failed", exception.Message);
        Assert.True(manager.Instances.ContainsKey(config.Uuid));
    }

    [Fact]
    public void IInstanceManager_DoesNotExposeTryKillInstance()
    {
        Assert.Null(typeof(IInstanceManager).GetMethod("TryKillInstance"));
    }

    private static InstanceConfig CreateConfig()
    {
        return new InstanceConfig
        {
            Name = "test-instance",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private static ProcessStartInfo CreateLongRunningProcessStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = "-n 31 127.0.0.1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/sleep",
            Arguments = "30",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
    }

    private sealed class TestInstance : IInstance
    {
        private readonly Func<InstanceProcess?> _processFactory;

        public TestInstance(InstanceConfig config, InstanceStatus status, Func<InstanceProcess?> processFactory)
        {
            Config = config;
            Status = status;
            _processFactory = processFactory;
        }

        public InstanceConfig Config { get; }
        public InstanceProcess? Process => _processFactory();
        public InstanceStatus Status { get; }
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

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new InstanceReport(Status, Config, new Dictionary<string, string>(), [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task ForceKillAndClearAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Process?.KillProcess();
            return Task.CompletedTask;
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
