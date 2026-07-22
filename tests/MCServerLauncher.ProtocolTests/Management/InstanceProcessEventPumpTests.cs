using System.Collections.Concurrent;
using System.Diagnostics;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using InstanceReference = MCServerLauncher.Common.Contracts.Instances.InstanceReference;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceProcessEventPumpTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitForExitAsync_DrainsStdoutAndStderrBeforeCompleting()
    {
        using var process = new InstanceProcess(CreateOutputThenExitStartInfo(), InstanceType.Universal);

        Assert.True(await process.StartAsync(delayToCheck: 10));
        process.WriteLine("exit");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);

        var logs = process.GetLogHistory();
        Assert.Contains("stdout-line", logs);
        Assert.Contains("[STDERR] stderr-line", logs);
    }

    [Fact]
    public async Task NaturalExit_PublishesStoppedOnlyAfterBothOutputPumpsDrain()
    {
        using var process = new InstanceProcess(CreateOutputThenExitStartInfo(), InstanceType.Universal);
        var stoppedSawDrainedOutput = false;
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Stopped)
            {
                var logs = process.GetLogHistory();
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
    public async Task KillProcess_BlockedLogConsumerCannotDelayOsPumpOrLifecycleDrain()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), InstanceType.Universal);
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
        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.Equal(-1, process.ServerProcessId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        Assert.False(releaseLog.Task.IsCompleted);
        Assert.True(process.Completion.IsCompletedSuccessfully);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
        releaseLog.SetResult();
    }

    [Fact]
    public async Task StopAllInstances_CommitsStoppingAndIgnoresBlockedLogConsumer()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateReadyStdinStopStartInfo(), InstanceType.Universal);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedLogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStoppedLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnLog += async (message, _) =>
        {
            if (message == "ready")
                ready.TrySetResult();
            if (message == "stopped")
            {
                stoppedLogEntered.TrySetResult();
                await releaseStoppedLog.Task;
            }
        };
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            return Task.CompletedTask;
        };
        var instance = new ProcessBackedInstance(config, process);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 20));
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stopTask = manager.StopAllInstances();
            await stoppedLogEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await stopTask.WaitAsync(TestTimeout);

            Assert.False(releaseStoppedLog.Task.IsCompleted);
            Assert.True(process.HasExit);
            Assert.Equal(InstanceStatus.Stopped, process.Status);
            var observed = statuses.ToArray();
            Assert.Equal(1, observed.Count(status => status == InstanceStatus.Stopping));
            Assert.Equal(1, observed.Count(status => status == InstanceStatus.Stopped));
            Assert.True(
                Array.IndexOf(observed, InstanceStatus.Stopping) <
                Array.IndexOf(observed, InstanceStatus.Stopped));
        }
        finally
        {
            releaseStoppedLog.TrySetResult();
            if (!process.HasExit)
            {
                process.KillProcess();
                await process.WaitForExitAsync().WaitAsync(TestTimeout);
            }
        }
    }

    [Fact]
    public async Task StopAllInstances_DrainsAfterCallerCancellationWithoutWaitingForLogConsumer()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        using var process = new InstanceProcess(CreateStdinStopStartInfo(), InstanceType.Universal);
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
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(releaseStoppedLog.Task.IsCompleted);
        Assert.True(process.HasExit);
        Assert.Equal(InstanceStatus.Stopped, process.Status);
        releaseStoppedLog.SetResult();
    }

    [Fact]
    public async Task MinecraftProcess_PublishesRunningOnlyAfterDoneOutput()
    {
        using var process = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
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
        Assert.Equal(InstanceStatus.Starting, process.Status);
        Assert.Contains(InstanceStatus.Starting, statuses);
        Assert.DoesNotContain(InstanceStatus.Running, statuses);

        process.WriteLine("continue");
        await runningPublished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, statuses.Count(status => status == InstanceStatus.Running));

        process.KillProcess();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task MinecraftProcess_BlockedLogConsumerCannotDelayReadyRecognitionOrTimeoutIt()
    {
        var time = new ManualTimeProvider();
        using var process = new InstanceProcess(
            CreateMinecraftDoneStartInfo(),
            MinecraftInstanceLifecycleObserver.Instance,
            timeProvider: time,
            readyTimeout: TimeSpan.FromMinutes(1));
        var logEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += async (message, _) =>
        {
            if (!message.StartsWith("Done (", StringComparison.Ordinal))
                return;

            logEntered.TrySetResult();
            await releaseLog.Task;
        };
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Running)
                running.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 20));
            process.WriteLine("continue");
            await logEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(running.Task.IsCompletedSuccessfully);
            Assert.Equal(InstanceStatus.Running, process.Status);
            time.Advance(TimeSpan.FromMinutes(1));
            Assert.False(process.ReadyTimedOut);
        }
        finally
        {
            releaseLog.TrySetResult();
            if (!process.HasExit)
                process.KillProcess();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task MinecraftProcess_ThrowingLogConsumerCannotFaultPumpOrHideReady()
    {
        using var process = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnLog += (message, _) =>
            message.StartsWith("Done (", StringComparison.Ordinal)
                ? throw new InvalidOperationException("consumer failed")
                : Task.CompletedTask;
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Running)
                running.TrySetResult();
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        process.WriteLine("continue");
        await running.Task.WaitAsync(TimeSpan.FromSeconds(3));

        process.KillProcess();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task MinecraftPty_BlockedAndThrowingConsoleConsumersCannotHideCrashOrDelayCompletion()
    {
        using var process = new InstanceProcess(
            CreatePtyMinecraftCrashStartInfo(),
            InstanceType.MCJava,
            ConsoleMode.Pty);
        var blockedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var throwingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var crashed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.AttachConsoleSubscriber(async (output, _, _) =>
        {
            if (output.IsEmpty)
                return;

            blockedEntered.TrySetResult();
            await releaseBlocked.Task;
        });
        process.AttachConsoleSubscriber((output, _, _) =>
        {
            if (output.IsEmpty)
                return Task.CompletedTask;

            throwingEntered.TrySetResult();
            throw new InvalidOperationException("console consumer failed");
        });
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Crashed)
                crashed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 20));
            Assert.True(process.IsPty, "The supported test platform must exercise the PTY output path.");
            await blockedEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await throwingEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            process.WriteLine("crash");
            await crashed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(releaseBlocked.Task.IsCompleted);
            Assert.Equal(InstanceStatus.Crashed, process.Status);
            Assert.True(process.Completion.IsCompletedSuccessfully);
        }
        finally
        {
            releaseBlocked.TrySetResult();
            if (!process.HasExit)
                process.KillProcess();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task GenericProcess_ProcessReadyCancelsReadyTimeout()
    {
        var time = new ManualTimeProvider();
        using var process = new InstanceProcess(
            CreateReadyThenLongRunningStartInfo(),
            GenericInstanceLifecycleObserver.Instance,
            timeProvider: time,
            readyTimeout: TimeSpan.FromMinutes(1));
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Running)
                running.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 20));
            await running.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(InstanceStatus.Running, process.Status);

            time.Advance(TimeSpan.FromMinutes(1));
            Assert.False(process.ReadyTimedOut);
            Assert.Equal(InstanceStatus.Running, process.Status);
        }
        finally
        {
            if (!process.HasExit)
                process.KillProcess();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task MinecraftProcess_ReadyTimeoutStaysStartingAndClearsOnReady()
    {
        var time = new ManualTimeProvider();
        using var process = new InstanceProcess(
            CreateMinecraftDoneStartInfo(),
            MinecraftInstanceLifecycleObserver.Instance,
            timeProvider: time,
            readyTimeout: TimeSpan.FromMinutes(1));
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut = new TaskCompletionSource<InstanceReportFact>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            if (status == InstanceStatus.Running)
                running.TrySetResult();
            return Task.CompletedTask;
        };
        process.OnReportFactChanged += (fact, _) =>
        {
            timedOut.TrySetResult(fact);
            return Task.CompletedTask;
        };

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 20));
            Assert.Equal(InstanceStatus.Starting, process.Status);
            Assert.False(process.ReadyTimedOut);

            time.Advance(TimeSpan.FromMinutes(1));
            var timeoutFact = await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(InstanceStatus.Starting, process.Status);
            Assert.True(process.ReadyTimedOut);
            Assert.False(process.HasExit);
            Assert.Equal(new InstanceReportFact(InstanceStatus.Starting, ReadyTimedOut: true), timeoutFact);
            Assert.Equal([InstanceStatus.Starting], statuses.ToArray());

            process.WriteLine("continue");
            await running.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(InstanceStatus.Running, process.Status);
            Assert.False(process.ReadyTimedOut);
        }
        finally
        {
            if (!process.HasExit)
                process.KillProcess();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task MinecraftProcess_ObserverConfirmedCrashRemainsCrashedAfterExit()
    {
        using var process = new InstanceProcess(CreateMinecraftCrashStartInfo(), InstanceType.MCJava);
        var crashed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += (status, _) =>
        {
            if (status == InstanceStatus.Crashed)
                crashed.TrySetResult();
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        process.WriteLine("crash");
        await crashed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(InstanceStatus.Crashed, process.Status);
        Assert.True(process.HasExit);
    }

    [Fact]
    public async Task MinecraftProcess_DuplicateReadyAndCrashSignalsPublishOnlyLegalTransitions()
    {
        using var process = new InstanceProcess(CreateMinecraftDuplicateSignalsStartInfo(), InstanceType.MCJava);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        process.WriteLine("continue");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(
            [InstanceStatus.Starting, InstanceStatus.Running, InstanceStatus.Crashed],
            statuses.ToArray());
        Assert.Equal(InstanceStatus.Crashed, process.Status);
    }

    [Fact]
    public async Task MinecraftProcess_StopCommittedBeforeCrashFinishesStopped()
    {
        using var process = new InstanceProcess(CreateMinecraftCrashStartInfo(), InstanceType.MCJava);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        Assert.True(await process.RequestStoppingAsync());
        Assert.Equal(InstanceStatus.Stopping, process.Status);
        Assert.False(await process.RequestStoppingAsync());

        process.WriteLine("crash");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.Equal(1, statuses.Count(status => status == InstanceStatus.Stopping));
        Assert.DoesNotContain(InstanceStatus.Crashed, statuses);
    }

    [Fact]
    public async Task MinecraftProcess_HaltAfterCrashPreservesFirstTerminalState()
    {
        using var process = new InstanceProcess(CreateMinecraftCrashThenWaitStartInfo(), InstanceType.MCJava);
        var crashed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            if (status == InstanceStatus.Crashed)
                crashed.TrySetResult();
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 20));
        process.WriteLine("crash");
        await crashed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        process.KillProcess();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(InstanceStatus.Crashed, process.Status);
        Assert.Equal(InstanceStatus.Crashed, statuses.Last());
        Assert.Equal(1, statuses.Count(status => status == InstanceStatus.Crashed));
        Assert.DoesNotContain(InstanceStatus.Stopped, statuses);
    }

    [Theory]
    [InlineData("Done (0.001s)! For help, type 'help'")]
    [InlineData("Done (12.3s)! For help, type \"help\"")]
    [InlineData("[Server thread/INFO]: Done (2.12s)! For help, type 'help' or '?'")]
    [InlineData("Done (2.123s)! For help, type \"help\" or \"?\"   ")]
    public void MinecraftObserver_AcceptsOnlyDocumentedReadyVariants(string message)
    {
        var observer = MinecraftInstanceLifecycleObserver.Instance;

        Assert.Equal(InstanceLifecycleSignal.Ready, observer.ObserveLog(message, isStandardError: false));
    }

    [Theory]
    [InlineData("Done (1s)! For help, type 'help'")]
    [InlineData("Done (1.1234s)! For help, type 'help'")]
    [InlineData("Done (1.1s)! For help, type help")]
    [InlineData("done (1.1s)! For help, type 'help'")]
    [InlineData("Done (1.1s)! For help, type 'help' trailing")]
    [InlineData("prefix Done (1.1s)! For help, type 'help' suffix")]
    [InlineData("A fatal error log was written to hs_err_pid1234.log")]
    public void MinecraftObserver_RejectsUndocumentedReadyAndCrashText(string message)
    {
        var observer = MinecraftInstanceLifecycleObserver.Instance;

        Assert.Equal(InstanceLifecycleSignal.None, observer.ObserveLog(message, isStandardError: false));
    }

    [Fact]
    public void MinecraftObserver_UsesStdoutOnlyOrdinalCrashSignal()
    {
        var observer = MinecraftInstanceLifecycleObserver.Instance;

        Assert.Equal(
            InstanceLifecycleSignal.None,
            observer.ObserveLog("Done (0.001s)! For help, type 'help'", isStandardError: true));
        Assert.Equal(
            InstanceLifecycleSignal.None,
            observer.ObserveLog("Minecraft has crashed", isStandardError: true));
        Assert.Equal(
            InstanceLifecycleSignal.None,
            observer.ObserveLog("minecraft has crashed", isStandardError: false));
        Assert.Equal(
            InstanceLifecycleSignal.Crashed,
            observer.ObserveLog("Minecraft has crashed", isStandardError: false));
    }

    [Fact]
    public async Task StartAsync_CancellationAfterStartingCommitDoesNotRollbackSpawn()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), InstanceType.Universal);
        var startingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStarting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += async (status, _) =>
        {
            if (status != InstanceStatus.Starting)
                return;

            startingEntered.TrySetResult();
            await releaseStarting.Task;
        };
        using var cancellation = new CancellationTokenSource();

        var startTask = process.StartAsync(delayToCheck: 100, cancellation.Token);
        await startingEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        Assert.False(startTask.IsCompleted);
        Assert.Equal(InstanceStatus.Starting, process.Status);

        releaseStarting.TrySetResult();
        Assert.True(await startTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(process.HasExit);

        process.KillProcess();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task QuickExit_StartSucceedsAndLaterPublishesStopped()
    {
        using var process = new InstanceProcess(CreateQuickExitStartInfo(), InstanceType.Universal);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        process.OnStatusChanged += (status, _) =>
        {
            statuses.Enqueue(status);
            return Task.CompletedTask;
        };

        Assert.True(await process.StartAsync(delayToCheck: 100));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(InstanceStatus.Starting, statuses.First());
        Assert.Equal(InstanceStatus.Stopped, statuses.Last());
        Assert.Equal(InstanceStatus.Stopped, process.Status);
    }

    [Fact]
    public async Task ApplicationStart_QuickExitSucceedsThenCatalogObservesStopped()
    {
        var config = CreateConfig() with
        {
            InstanceType = InstanceType.Universal,
            TargetType = TargetType.Script
        };
        var workingDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        var target = OperatingSystem.IsWindows() ? "quick-exit.cmd" : "quick-exit.sh";
        var targetPath = Path.Combine(workingDirectory, target);
        await File.WriteAllTextAsync(
            targetPath,
            OperatingSystem.IsWindows() ? "@exit /b 0\r\n" : "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var instance = new ResettableInstance(config with { Target = target });
        var manager = new InstanceManager();
        manager.ReplaceInstance(config.Uuid, instance);
        var application = new LocalInstanceApplication(manager);

        try
        {
            var result = await application.StartInstanceAsync(
                new InstanceReference(config.Uuid),
                CancellationToken.None);

            Assert.True(result.IsOk(out _));
            var process = Assert.IsType<InstanceProcess>(instance.Process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(InstanceStatus.Stopped, instance.Status);
            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var snapshot));
            Assert.Equal(InstanceStatus.Stopped, snapshot.Status);
            Assert.False(snapshot.ReadyTimedOut);
        }
        finally
        {
            instance.Dispose();
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BlockedRunningHandler_HaltCommitsImmediatelyAndPublishesInOrder()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), InstanceType.Universal);
        var statuses = new ConcurrentQueue<InstanceStatus>();
        var runningEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += async (status, _) =>
        {
            statuses.Enqueue(status);
            if (status == InstanceStatus.Running)
            {
                runningEntered.TrySetResult();
                await releaseRunning.Task;
            }
        };

        Assert.True(await process.StartAsync(delayToCheck: 100));
        await runningEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        process.KillProcess();

        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.Equal(-1, process.ServerProcessId);
        Assert.Equal([InstanceStatus.Starting, InstanceStatus.Running], statuses.ToArray());

        releaseRunning.TrySetResult();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(
            [InstanceStatus.Starting, InstanceStatus.Running, InstanceStatus.Stopped],
            statuses.ToArray());
    }

    [Fact]
    public async Task InstanceBase_OldGenerationStatusesCannotRegressRestartedCatalog()
    {
        using var oldProcess = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
        using var currentProcess = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
        var config = CreateConfig();
        var instance = new FactoryBackedInstance(config, oldProcess, currentProcess);
        var manager = new InstanceManager();
        manager.ReplaceInstance(config.Uuid, instance);
        var observedStatuses = new ConcurrentQueue<InstanceStatus>();
        var oldRunningEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.InstanceStatusChanged += async (_, status, _) =>
        {
            if (status == InstanceStatus.Running && ReferenceEquals(instance.Process, oldProcess))
            {
                oldRunningEntered.TrySetResult();
                await releaseOldRunning.Task;
            }
        };
        var currentRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.InstanceStatusChanged += (_, status, _) =>
        {
            observedStatuses.Enqueue(status);
            if (status == InstanceStatus.Running && ReferenceEquals(instance.Process, currentProcess))
                currentRunning.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            Assert.Same(instance, await manager.TryStartInstance(config.Uuid));
            oldProcess.WriteLine("continue");
            await oldRunningEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var haltTask = manager.KillInstanceAsync(config.Uuid);
            await WaitUntilAsync(() => oldProcess.Status == InstanceStatus.Stopped);
            var restartTask = manager.TryStartInstance(config.Uuid);

            Assert.False(haltTask.IsCompleted);
            Assert.False(restartTask.IsCompleted);
            Assert.Same(oldProcess, instance.Process);

            releaseOldRunning.TrySetResult();
            await haltTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(oldProcess.Completion.IsCompletedSuccessfully);
            Assert.NotSame(oldProcess, instance.Process);
            Assert.NotEqual(1, ((IInstanceProcessGenerationSource)instance).CurrentProcessGeneration);
            Assert.Same(instance, await restartTask.WaitAsync(TimeSpan.FromSeconds(3)));
            currentProcess.WriteLine("continue");
            await currentRunning.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var snapshot));
            Assert.Equal(InstanceStatus.Running, snapshot.Status);
            Assert.Equal(InstanceStatus.Running, instance.Status);
            var observed = observedStatuses.ToArray();
            Assert.Equal(2, observed.Count(status => status == InstanceStatus.Running));
            Assert.True(
                Array.LastIndexOf(observed, InstanceStatus.Stopped) <
                Array.LastIndexOf(observed, InstanceStatus.Starting));
        }
        finally
        {
            releaseOldRunning.TrySetResult();
            await instance.ForceKillAndClearAsync();
            instance.Dispose();
        }
    }

    [Fact]
    public async Task InstanceBase_OldGenerationReadyTimeoutCannotRegressRestartedCatalog()
    {
        var time = new ManualTimeProvider();
        using var oldProcess = new InstanceProcess(
            CreateMinecraftDoneStartInfo(),
            MinecraftInstanceLifecycleObserver.Instance,
            timeProvider: time,
            readyTimeout: TimeSpan.FromMinutes(1));
        using var currentProcess = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
        var config = CreateConfig();
        var instance = new FactoryBackedInstance(config, oldProcess, currentProcess);
        var manager = new InstanceManager();
        manager.ReplaceInstance(config.Uuid, instance);
        var oldTimeoutEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTimeout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.InstanceStatusChanged += (_, status, _) =>
        {
            if (status == InstanceStatus.Running && ReferenceEquals(instance.Process, currentProcess))
                currentRunning.TrySetResult();
            return Task.CompletedTask;
        };
        try
        {
            Assert.Same(instance, await manager.TryStartInstance(config.Uuid));
            // InstanceBase attaches its generation-aware manager handler during Start. Install
            // the barrier afterward so reaching it proves the authoritative commit already ran.
            oldProcess.OnReportFactChanged += async (fact, _) =>
            {
                if (!fact.ReadyTimedOut)
                    return;

                oldTimeoutEntered.TrySetResult();
                await releaseOldTimeout.Task;
            };
            time.Advance(TimeSpan.FromMinutes(1));
            await oldTimeoutEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var timedOut));
            Assert.True(timedOut.ReadyTimedOut);

            var haltTask = manager.KillInstanceAsync(config.Uuid);
            await WaitUntilAsync(() => oldProcess.Status == InstanceStatus.Stopped);
            var restartTask = manager.TryStartInstance(config.Uuid);
            Assert.False(haltTask.IsCompleted);
            Assert.False(restartTask.IsCompleted);

            releaseOldTimeout.TrySetResult();
            await haltTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Same(instance, await restartTask.WaitAsync(TimeSpan.FromSeconds(3)));
            currentProcess.WriteLine("continue");
            await currentRunning.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var after));
            Assert.Equal(InstanceStatus.Running, after.Status);
            Assert.False(after.ReadyTimedOut);
            Assert.False(instance.ReadyTimedOut);
        }
        finally
        {
            releaseOldTimeout.TrySetResult();
            await instance.ForceKillAndClearAsync();
            instance.Dispose();
        }
    }

    [Fact]
    public async Task InstanceManager_ReplacementDuringStatusFanOutFencesRemainingSubscribers()
    {
        var config = CreateConfig();
        var oldInstance = new ControllableGenerationInstance(config, InstanceStatus.Running);
        var replacement = new ControllableGenerationInstance(
            config with { Name = "replacement" },
            InstanceStatus.Crashed);
        var manager = new InstanceManager();
        manager.ReplaceInstance(config.Uuid, oldInstance);
        var firstSubscriberEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSubscriber = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterSubscriberCalls = 0;
        manager.InstanceStatusChanged += async (_, status, _) =>
        {
            if (status != InstanceStatus.Stopped)
                return;

            firstSubscriberEntered.TrySetResult();
            await releaseFirstSubscriber.Task;
        };
        manager.InstanceStatusChanged += (_, _, _) =>
        {
            Interlocked.Increment(ref laterSubscriberCalls);
            return Task.CompletedTask;
        };

        var oldPublication = oldInstance.PublishStatusAsync(InstanceStatus.Stopped);
        await firstSubscriberEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        manager.ReplaceInstance(config.Uuid, replacement);
        releaseFirstSubscriber.TrySetResult();
        await oldPublication.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, Volatile.Read(ref laterSubscriberCalls));
        Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var snapshot));
        Assert.Equal("replacement", snapshot.Name);
        Assert.Equal(InstanceStatus.Crashed, snapshot.Status);
    }

    [Fact]
    public async Task InstanceBase_StopRejectsTerminalStateWhileItsPublicationIsBlocked()
    {
        using var process = new InstanceProcess(CreateMinecraftDoneStartInfo(), InstanceType.MCJava);
        var terminalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnStatusChanged += async (status, _) =>
        {
            if (status != InstanceStatus.Stopped)
                return;

            terminalEntered.TrySetResult();
            await releaseTerminal.Task;
        };
        var config = CreateConfig();
        var instance = new FactoryBackedInstance(config, process);
        var manager = new InstanceManager();
        manager.ReplaceInstance(config.Uuid, instance);
        var application = new LocalInstanceApplication(manager);

        try
        {
            Assert.Same(instance, await manager.TryStartInstance(config.Uuid));
            process.KillProcess();
            await terminalEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));

            var result = await application.StopInstanceAsync(
                new InstanceReference(config.Uuid),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.Equal("instance.not_running", error!.Code);
            Assert.Equal(InstanceStatus.Stopped, process.Status);
        }
        finally
        {
            releaseTerminal.TrySetResult();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            instance.Dispose();
        }
    }

    [Fact]
    public async Task DefaultReadyTimeout_IsExactlyTwoMinutes()
    {
        var time = new ManualTimeProvider();
        using var process = new InstanceProcess(
            CreateMinecraftDoneStartInfo(),
            MinecraftInstanceLifecycleObserver.Instance,
            timeProvider: time);

        try
        {
            Assert.True(await process.StartAsync(delayToCheck: 100));
            Assert.Equal(TimeSpan.FromMinutes(2), time.LastScheduledDueTime);
        }
        finally
        {
            process.KillProcess();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public async Task StartAsync_PreStartCancellation_DoesNotStartProcess()
    {
        using var process = new InstanceProcess(CreateReadyThenLongRunningStartInfo(), InstanceType.Universal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process.StartAsync(delayToCheck: 20, cancellation.Token));

        Assert.Equal(InstanceStatus.Stopped, process.Status);
        Assert.True(process.Completion.IsCompleted);
    }

    [Fact]
    public async Task InstanceBase_StopReturnsAfterStoppingBeforeFinalizerCompletes()
    {
        var config = CreateConfig() with { InstanceType = InstanceType.Universal, TargetType = TargetType.Script };
        var workingDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
        var target = OperatingSystem.IsWindows() ? "stop-intermediate.cmd" : "stop-intermediate.sh";
        var targetPath = Path.Combine(workingDirectory, target);
        await File.WriteAllTextAsync(
            targetPath,
            OperatingSystem.IsWindows() ? "@echo ready\r\n@set /p line=\r\n" : "#!/bin/sh\nprintf 'ready\\n'\nread line\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var instance = new ResettableInstance(config with { Target = target });
        var stoppedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.OnStatusChanged += async (_, status, _) =>
        {
            if (status == InstanceStatus.Stopped)
            {
                stoppedEntered.TrySetResult();
                await releaseStopped.Task;
            }
        };

        try
        {
            Assert.True(await instance.StartAsync(delayToCheck: 20));
            var process = Assert.IsType<InstanceProcess>(instance.Process);

            var stopTask = instance.StopAsync();
            await stoppedEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(await stopTask.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(process.Completion.IsCompleted);

            releaseStopped.TrySetResult();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(InstanceStatus.Stopped, process.Status);
        }
        finally
        {
            releaseStopped.TrySetResult();
            instance.Dispose();
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InstanceBase_FailedOsStartResetsProcessAndCanRetry()
    {
        var config = CreateConfig() with
        {
            InstanceType = InstanceType.Universal,
            // Use a missing path so the OS start fails immediately (no antivirus/image parse delay).
            Target = "missing-binary.exe",
            TargetType = TargetType.Executable
        };
        var workingDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(workingDirectory);
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
            await instance.StopAsync();
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
            await instance.StopAsync();
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
            await instance.StopAsync();
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
        using var liveProcess = new InstanceProcess(CreateStdinStopStartInfo(), InstanceType.Universal);
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
        manager.Instances[failedConfig.Uuid] = new ThrowingProcessAccessorInstance(failedConfig);
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

    private static ProcessStartInfo CreateReadyStdinStopStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo ready&set /p line=&echo stopped")
            : CreateStartInfo("/bin/sh", "-c", "printf 'ready\\n'; read line; printf 'stopped\\n'");
    }

    private static ProcessStartInfo CreateMinecraftDoneStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo booted&set /p line=&echo Done ^(0.001s^)! For help, type 'help'&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'booted\\n'; read line; printf 'Done (0.001s)! For help, type \"help\"\\n'; read line");
    }

    private static ProcessStartInfo CreateMinecraftCrashStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo booted&set /p line=&echo Minecraft has crashed&exit /b 1")
            : CreateStartInfo("/bin/sh", "-c", "printf 'booted\\n'; read line; printf 'Minecraft has crashed\\n'; exit 1");
    }

    private static ProcessStartInfo CreatePtyMinecraftCrashStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /q /c \"echo booted&set /p line=&echo Minecraft has crashed&exit /b 1\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"printf 'booted\\n'; read line; printf 'Minecraft has crashed\\n'; exit 1\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
    }

    private static ProcessStartInfo CreateMinecraftDuplicateSignalsStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo(
                "cmd.exe",
                "/d",
                "/c",
                "echo booted&set /p line=&echo Done ^(0.001s^)! For help, type 'help'&echo Done ^(0.001s^)! For help, type 'help'&echo Minecraft has crashed&echo Minecraft has crashed&exit /b 1")
            : CreateStartInfo(
                "/bin/sh",
                "-c",
                "printf 'booted\\n'; read line; printf 'Done (0.001s)! For help, type \"help\"\\nDone (0.001s)! For help, type \"help\"\\nMinecraft has crashed\\nMinecraft has crashed\\n'; exit 1");
    }

    private static ProcessStartInfo CreateMinecraftCrashThenWaitStartInfo()
    {
        return OperatingSystem.IsWindows()
            ? CreateStartInfo("cmd.exe", "/d", "/c", "echo booted&set /p line=&echo Minecraft has crashed&set /p line=")
            : CreateStartInfo("/bin/sh", "-c", "printf 'booted\\n'; read line; printf 'Minecraft has crashed\\n'; read line");
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
        while (directory is not null && !File.Exists(Path.Combine(directory, "MCServerLauncher.slnx")))
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

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            _process.KillProcess();
            return Task.FromResult(true);
        }

        public Task ForceKillAndClearAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _process.KillProcess();
            return Task.CompletedTask;
        }
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

    private sealed class ThrowingProcessAccessorInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => throw new InvalidOperationException("process accessor failed");
        public InstanceStatus Status => InstanceStatus.Running;
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

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new InstanceReport(Status, Config, [], [], default));

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> StopAsync(CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task ForceKillAndClearAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(TestTimeout.TotalSeconds * Stopwatch.Frequency);
        while (!predicate())
        {
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException("The expected lifecycle condition was not observed.");

            await Task.Delay(10);
        }
    }

    private sealed class FactoryBackedInstance : InstanceBase
    {
        internal FactoryBackedInstance(InstanceConfig config, params InstanceProcess[] processes)
            : this(config, new Queue<InstanceProcess>(processes))
        {
        }

        private FactoryBackedInstance(InstanceConfig config, Queue<InstanceProcess> processes)
            : base(config, (_, _, _) => processes.Dequeue())
        {
        }
    }

    private sealed class ControllableGenerationInstance : IInstance, IInstanceProcessGenerationSource
    {
        private InstanceStatus _status;

        internal ControllableGenerationInstance(InstanceConfig config, InstanceStatus status)
        {
            Config = config;
            _status = status;
        }

        public InstanceConfig Config { get; }
        public InstanceProcess? Process => null;
        public InstanceStatus Status => _status;
        public bool ReadyTimedOut => false;
        public int ServerProcessId => -1;
        long IInstanceProcessGenerationSource.CurrentProcessGeneration => 1;

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

        public event Func<IInstance, long, string, CancellationToken, Task>? ProcessLogReceived
        {
            add { }
            remove { }
        }

        public event Func<IInstance, long, InstanceStatus, CancellationToken, Task>? ProcessStatusChanged;

        public event Func<IInstance, long, InstanceReportFact, CancellationToken, Task>? ProcessReportFactChanged
        {
            add { }
            remove { }
        }

        internal async Task PublishStatusAsync(InstanceStatus status)
        {
            _status = status;
            var handlers = ProcessStatusChanged?.GetInvocationList()
                .Cast<Func<IInstance, long, InstanceStatus, CancellationToken, Task>>()
                .ToArray() ?? [];
            foreach (var handler in handlers)
                await handler(this, 1, status, CancellationToken.None);
        }

        public Task<InstanceReport> GetReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new InstanceReport(_status, Config, [], [], default));

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> StopAsync(CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task ForceKillAndClearAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];

        internal TimeSpan? LastScheduledDueTime { get; private set; }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            LastScheduledDueTime = dueTime;
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            ManualTimer[] due;
            lock (_gate)
            {
                foreach (var timer in _timers)
                    timer.Advance(duration);
                due = _timers.Where(static timer => timer.IsDue).ToArray();
                foreach (var timer in due)
                    timer.RescheduleAfterFire();
            }

            foreach (var timer in due)
                timer.Fire();
        }

        private void Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimeout(dueTime, nameof(dueTime));
            ValidateTimeout(period, nameof(period));
            lock (_gate)
            {
                timer.Remaining = dueTime;
                timer.Period = period;
                _timers.Add(timer);
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
                _timers.Remove(timer);
        }

        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(name);
        }

        private sealed class ManualTimer(
            ManualTimeProvider provider,
            TimerCallback callback,
            object? state) : ITimer
        {
            private int _disposed;

            internal TimeSpan Remaining { get; set; } = Timeout.InfiniteTimeSpan;
            internal TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;
            internal bool IsDue => Volatile.Read(ref _disposed) == 0 && Remaining <= TimeSpan.Zero;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return false;
                provider.Schedule(this, dueTime, period);
                return true;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    provider.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void Advance(TimeSpan duration)
            {
                if (Remaining != Timeout.InfiniteTimeSpan)
                    Remaining -= duration;
            }

            internal void RescheduleAfterFire()
            {
                Remaining = Period == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : Period;
            }

            internal void Fire()
            {
                if (Volatile.Read(ref _disposed) == 0)
                    callback(state);
            }
        }
    }
}
