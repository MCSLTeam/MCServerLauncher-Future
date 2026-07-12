using System.Collections.Concurrent;
using System.Diagnostics;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceProcessEventPumpTests
{
    [Fact]
    public async Task WaitForExitAsync_DrainsStdoutAndStderrBeforeCompleting()
    {
        using var process = new InstanceProcess(CreateOutputThenExitStartInfo(), isMcServer: false);
        var logs = new ConcurrentQueue<string>();
        process.OnLog += (message, _) =>
        {
            logs.Enqueue(message);
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 10));
        process.WriteLine("exit");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Contains("stdout-line", logs);
        Assert.Contains("[STDERR] stderr-line", logs);
    }

    [Fact]
    public async Task NaturalExit_PublishesStoppedOnlyAfterBothOutputPumpsDrain()
    {
        using var process = new InstanceProcess(CreateOutputThenExitStartInfo(), isMcServer: false);
        var logs = new ConcurrentQueue<string>();
        var stoppedSawDrainedOutput = false;
        process.OnLog += (message, _) =>
        {
            logs.Enqueue(message);
            return Task.CompletedTask;
        };
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Stopped)
            {
                stoppedSawDrainedOutput = logs.Contains("stdout-line") &&
                                        logs.Contains("[STDERR] stderr-line");
            }

            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 10));
        process.WriteLine("exit");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        Assert.True(stoppedSawDrainedOutput);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task KillProcess_ConfirmsOsExitSynchronouslyWhileAsyncPumpRemainsDrainable()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), isMcServer: false);
        var logEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += async (message, _) =>
        {
            if (message == "ready")
            {
                logEntered.TrySetResult();
                await releaseLog.Task;
            }
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        await logEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        process.KillProcess();

        Assert.True(process.HasExit);
        Assert.False(process.Completion.IsCompleted);
        releaseLog.SetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task StopAllInstances_WaitsForProcessFinalizerAndOutputDrain()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateStdinStopStartInfo(), isMcServer: false);
        var stoppedLogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStoppedLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += async (message, _) =>
        {
            if (message == "stopped")
            {
                stoppedLogEntered.TrySetResult();
                await releaseStoppedLog.Task;
            }
        };
        var instance = new ProcessBackedInstance(config, process);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;

        Assert.True(await process.StartAsync(delayToCheck: 20));
        var stopTask = manager.StopAllInstances();
        await stoppedLogEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(stopTask.IsCompleted);

        releaseStoppedLog.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(process.HasExit);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task StopAllInstances_DrainsProcessFinalizerAfterCallerCancellation()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateStdinStopStartInfo(), isMcServer: false);
        var stoppedLogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStoppedLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += async (message, _) =>
        {
            if (message == "stopped")
            {
                stoppedLogEntered.TrySetResult();
                await releaseStoppedLog.Task;
            }
        };
        var instance = new ProcessBackedInstance(config, process);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;

        Assert.True(await process.StartAsync(delayToCheck: 20));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stopTask = manager.StopAllInstances(cancellation.Token);

        await stoppedLogEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(stopTask.IsCompleted);

        releaseStoppedLog.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(process.HasExit);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task MinecraftProcess_PublishesRunningOnlyAfterDoneOutput()
    {
        using var process = new InstanceProcess(CreateMinecraftDoneStartInfo(), isMcServer: true);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        var runningPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            if (status == InstanceStatus.Running)
                runningPublished.TrySetResult();
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.DoesNotContain(InstanceStatus.Running, statuses);

        process.WriteLine("continue");
        await runningPublished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, statuses.Count(status => status == InstanceStatus.Running));

        process.KillProcess();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task StartAsync_CanceledDuringStartup_TerminatesAndDrainsStartedProcess()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), isMcServer: false);
        var processStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += (message, _) =>
        {
            if (message == "ready")
                processStarted.TrySetResult();
            return Task.CompletedTask;
        };
        using var cancellation = new CancellationTokenSource();

        var startTask = process.StartAsync(Timeout.Infinite, cancellation.Token);
        await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(process.HasExit);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task QuickExit_NeverPublishesRunningAndFinishesStopped()
    {
        using var process = new InstanceProcess(CreateQuickExitStartInfo(), isMcServer: false);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            return Task.CompletedTask;
        };

        Assert.False(await process.StartAsync(delayToCheck: 100));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.DoesNotContain(InstanceStatus.Running, statuses);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task StartAsync_PreStartCancellation_DoesNotStartProcess()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), isMcServer: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process.StartAsync(delayToCheck: 20, cancellation.Token));

        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.True(process.Completion.IsCompleted);
    }

    [Fact]
    public async Task InstanceBase_FailedOsStartResetsProcessAndCanRetry()
    {
        var config = CreateConfig() with { InstanceType = InstanceType.Universal, Target = "broken.exe", TargetType = TargetType.Executable };
        var workingDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, config.Target), "not an executable");
        var instance = new ResettableInstance(config);

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => instance.StartAsync(delayToCheck: 20));
            Assert.Null(instance.Process);

            var retryTarget = OperatingSystem.IsWindows() ? "retry.cmd" : "retry.sh";
            var retryPath = Path.Combine(workingDirectory, retryTarget);
            await File.WriteAllTextAsync(
                retryPath,
                OperatingSystem.IsWindows() ? "@echo ready\r\nset /p line=" : "#!/bin/sh\nprintf 'ready\\n'\nread line\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(retryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            instance.ReplaceConfig(config with { Target = retryTarget, TargetType = TargetType.Script });

            Assert.True(await instance.StartAsync(delayToCheck: 20));
            instance.Stop();
            await instance.Process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            instance.Dispose();
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstanceBase_ExitedProcessIsClearedBeforeInvalidConfigAndCanRetry()
    {
        var config = CreateConfig() with { InstanceType = InstanceType.Universal, TargetType = TargetType.Script };
        var workingDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        var initialTarget = OperatingSystem.IsWindows() ? "initial.cmd" : "initial.sh";
        var initialPath = Path.Combine(workingDirectory, initialTarget);
        await File.WriteAllTextAsync(
            initialPath,
            OperatingSystem.IsWindows() ? "@echo ready\r\n@set /p line=\r\n" : "#!/bin/sh\nprintf 'ready\\n'\nread line\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(initialPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var instance = new ResettableInstance(config with { Target = initialTarget });

        try
        {
            Assert.True(await instance.StartAsync(delayToCheck: 20));
            instance.Stop();
            await instance.Process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

            instance.ReplaceConfig(config with { Target = string.Empty });
            Assert.False(await instance.StartAsync(delayToCheck: 20));
            Assert.Null(instance.Process);

            var retryTarget = OperatingSystem.IsWindows() ? "retry-after-invalid.cmd" : "retry-after-invalid.sh";
            var retryPath = Path.Combine(workingDirectory, retryTarget);
            await File.WriteAllTextAsync(
                retryPath,
                OperatingSystem.IsWindows() ? "@echo ready\r\n@set /p line=\r\n" : "#!/bin/sh\nprintf 'ready\\n'\nread line\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(retryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            instance.ReplaceConfig(config with { Target = retryTarget });

            Assert.True(await instance.StartAsync(delayToCheck: 20));
            instance.Stop();
            await instance.Process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            instance.Dispose();
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StopAllInstances_SignalFailureStillDrainsOtherProcesses()
    {
        var manager = new InstanceManager();
        var failedConfig = CreateConfig();
        var liveConfig = CreateConfig();
        using var failedProcess = new InstanceProcess(CreateStdinStopStartInfo(), isMcServer: false);
        failedProcess.Dispose();
        using var liveProcess = new InstanceProcess(CreateStdinStopStartInfo(), isMcServer: false);
        var stoppedLogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStoppedLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        liveProcess.OnLog += async (message, _) =>
        {
            if (message == "stopped")
            {
                stoppedLogEntered.TrySetResult();
                await releaseStoppedLog.Task;
            }
        };
        manager.Instances[failedConfig.Uuid] = new ProcessBackedInstance(failedConfig, failedProcess);
        manager.RunningInstances[failedConfig.Uuid] = new ProcessBackedInstance(failedConfig, failedProcess);
        manager.Instances[liveConfig.Uuid] = new ProcessBackedInstance(liveConfig, liveProcess);
        manager.RunningInstances[liveConfig.Uuid] = new ProcessBackedInstance(liveConfig, liveProcess);

        Assert.True(await liveProcess.StartAsync(delayToCheck: 20));
        var stopTask = manager.StopAllInstances();
        await stoppedLogEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(stopTask.IsCompleted);

        releaseStoppedLog.TrySetResult();
        await Assert.ThrowsAsync<AggregateException>(() => stopTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(liveProcess.HasExit);
        Assert.Equal(InstanceStatus.Stopped, liveProcess.Status);
    }

    [Fact]
    public void AsyncEventChain_DoesNotUseForbiddenCallbackOrBlockingPatterns()
    {
        var repoRoot = ResolveRepoRoot();
        var sources = new[]
        {
            "src/MCServerLauncher.Daemon/Management/Communicate/InstanceProcess.cs",
            "src/MCServerLauncher.Daemon/Management/InstanceBase.cs",
            "src/MCServerLauncher.Daemon/Management/InstanceManager.cs",
            "src/MCServerLauncher.Daemon/Bootstrap/InstanceDomainEventBridge.cs",
            "src/MCServerLauncher.Daemon/Application/Events/DomainEvents.cs"
        }.Select(path => File.ReadAllText(Path.Combine(repoRoot, path)));

        var combined = string.Join('\n', sources);
        Assert.DoesNotContain("OutputDataReceived +=", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorDataReceived +=", combined, StringComparison.Ordinal);
        Assert.DoesNotContain(".Exited +=", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("async void", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run(", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = _domainEvents.PublishAsync", combined, StringComparison.Ordinal);
    }

    private static ProcessStartInfo CreateOutputThenExitStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo stdout-line&echo stderr-line>&2&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'stdout-line\\n'; printf 'stderr-line\\n' >&2; read line");
    }

    private static ProcessStartInfo CreateReadyThenLongRunningStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo ready&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'ready\\n'; read line");
    }

    private static ProcessStartInfo CreateStdinStopStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "set /p line= & echo stopped")
            : CreateStartInfo("/bin/sh", "-c", "read line; printf 'stopped\\n'");
    }

    private static ProcessStartInfo CreateMinecraftDoneStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo booted&set /p line=&echo Done ^(0.001s^)! For help, type 'help'&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'booted\\n'; read line; printf 'Done (0.001s)! For help, type \"help\"\\n'; read line");
    }

    private static ProcessStartInfo CreateQuickExitStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "exit 0")
            : CreateStartInfo("/bin/sh", "-c", "exit 0");
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

    private static InstanceConfig CreateConfig()
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = "process-pump-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private static string ResolveRepoRoot()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "MCServerLauncher.sln")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class ProcessBackedInstance : IInstance
    {
        private readonly InstanceProcess _process;

        internal ProcessBackedInstance(InstanceConfig config, InstanceProcess process)
        {
            Config = config;
            _process = process;
        }

        public InstanceConfig Config { get; }
        public InstanceProcess? Process => _process;
        public InstanceStatus Status => _process.Status;
        public int ServerProcessId => _process.ServerProcessId;
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
            return Task.FromResult(new InstanceReport(Status, Config, [], [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return _process.StartAsync(delayToCheck, ct);
        }

        public void Stop() => _process.KillProcess();
        public IReadOnlyList<string> GetLogHistory() => _process.GetLogHistory();
        public void Dispose()
        {
        }
    }

    private sealed class ResettableInstance(InstanceConfig config) : InstanceBase(config)
    {
        public void ReplaceConfig(InstanceConfig config)
        {
            ProtectedConfig = config;
        }
    }
}
