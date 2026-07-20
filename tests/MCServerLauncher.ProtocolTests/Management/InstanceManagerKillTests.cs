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
        using var process = new InstanceProcess(CreateLongRunningProcessStartInfo(), isMcServer: true);
        var started = false;

        try
        {
            started = await process.StartAsync(delayToCheck: 100);
            Assert.True(started);

            var instance = new TestInstance(config, status, () => process);
            manager.Instances[config.Uuid] = instance;
            manager.RunningInstances[config.Uuid] = instance;

            Assert.True(await manager.TryStopInstance(config.Uuid));
            Assert.False(manager.RunningInstances.ContainsKey(config.Uuid));
            Assert.False(process.HasExit);

            manager.KillInstance(config.Uuid);

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
    public void KillInstance_MissingInstance_IsNoOp()
    {
        var manager = new InstanceManager();

        var exception = Record.Exception(() => manager.KillInstance(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public void KillInstance_NullProcess_IsNoOpAndReadsProcessOnce()
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

        var exception = Record.Exception(() => manager.KillInstance(config.Uuid));

        Assert.Null(exception);
        Assert.Equal(1, processReads);
    }

    [Fact]
    public async Task KillInstance_UsesSingleCapturedProcessReference()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateLongRunningProcessStartInfo(), isMcServer: false);
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

            manager.KillInstance(config.Uuid);

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
    public void KillInstance_ProcessFailure_IsSwallowed()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.Instances[config.Uuid] = new TestInstance(
            config,
            InstanceStatus.Stopped,
            () => throw new InvalidOperationException("process accessor failed"));

        var exception = Record.Exception(() => manager.KillInstance(config.Uuid));
        Assert.Null(exception);
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

        public void Stop()
        {
        }

        public void ForceKillAndClear() { Process?.KillProcess(); }

        public IReadOnlyList<string> GetLogHistory()
        {
            return Array.Empty<string>();
        }

        public void Dispose()
        {
        }
    }
}
