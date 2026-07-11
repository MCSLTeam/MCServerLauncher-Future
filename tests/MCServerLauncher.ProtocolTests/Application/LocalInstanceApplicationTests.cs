using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Storage;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class LocalInstanceApplicationTests
{
    [Fact]
    public async Task HaltInstance_MissingInstance_CompletesSuccessfully()
    {
        var application = new LocalInstanceApplication(new InstanceManager());

        var result = await application.HaltInstanceAsync(
            new InstanceReference(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsOk(out _));
    }

    [Fact]
    public async Task HaltInstance_ProcessFailure_ReturnsTypedInternalError()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.Instances[config.Uuid] = new TestInstance(config, () => throw new InvalidOperationException("kill failed"));
        var application = new LocalInstanceApplication(manager);

        var result = await application.HaltInstanceAsync(new InstanceReference(config.Uuid), CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        var internalError = Assert.IsType<InternalDaemonError>(error);
        Assert.Equal("instance.halt_failed", internalError.Code);
    }

    [Fact]
    public async Task StartInstance_AlreadyRunning_ReturnsConflict()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var instance = new TestInstance(config, () => null, InstanceStatus.Running);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;
        var application = new LocalInstanceApplication(manager);

        var result = await application.StartInstanceAsync(new InstanceReference(config.Uuid), CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.IsType<ConflictDaemonError>(error);
        Assert.Equal("instance.already_running", error.Code);
    }

    [Fact]
    public async Task StartInstance_CanceledDuringStart_PropagatesCancellationAndClearsRunningState()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var startEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Instances[config.Uuid] = new TestInstance(
            config,
            () => null,
            startAsync: async cancellationToken =>
            {
                startEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            });
        var application = new LocalInstanceApplication(manager);
        using var cancellationSource = new CancellationTokenSource();

        var startTask = application.StartInstanceAsync(new InstanceReference(config.Uuid), cancellationSource.Token);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(manager.RunningInstances.ContainsKey(config.Uuid));
    }

    [Fact]
    public async Task StopInstance_WaitsForConcurrentStartToCommit()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var startEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCalls = 0;
        var instance = new TestInstance(
            config,
            () => null,
            startAsync: async _ =>
            {
                startEntered.TrySetResult(true);
                await releaseStart.Task;
                return true;
            },
            stop: () => Interlocked.Increment(ref stopCalls));
        manager.Instances[config.Uuid] = instance;

        var startTask = manager.TryStartInstance(config.Uuid);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var stopTask = manager.TryStopInstance(config.Uuid);
        Assert.False(stopTask.IsCompleted);

        releaseStart.TrySetResult(true);
        Assert.Same(instance, await startTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await stopTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, Volatile.Read(ref stopCalls));
        Assert.False(manager.RunningInstances.ContainsKey(config.Uuid));
    }

    [Fact]
    public async Task RemoveInstance_CanceledWhileMutationIsHeld_DoesNotRemoveLater()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
        var application = new LocalInstanceApplication(manager);
        using var mutation = manager.AcquireInstanceMutation(config.Uuid);
        using var cancellationSource = new CancellationTokenSource();

        var removeTask = application.RemoveInstanceAsync(
            new InstanceReference(config.Uuid),
            cancellationSource.Token);
        Assert.False(removeTask.IsCompleted);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => removeTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(manager.Instances.ContainsKey(config.Uuid));
        Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out _));
    }

    [Fact]
    public async Task StopInstance_CanceledWhileMutationIsHeld_DoesNotStopLater()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var stopCalls = 0;
        var instance = new TestInstance(
            config,
            () => null,
            InstanceStatus.Running,
            stop: () => Interlocked.Increment(ref stopCalls));
        manager.ReplaceInstance(config.Uuid, instance);
        manager.RunningInstances[config.Uuid] = instance;
        var application = new LocalInstanceApplication(manager);
        using var mutation = manager.AcquireInstanceMutation(config.Uuid);
        using var cancellationSource = new CancellationTokenSource();

        var stopTask = application.StopInstanceAsync(
            new InstanceReference(config.Uuid),
            cancellationSource.Token);
        Assert.False(stopTask.IsCompleted);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stopTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, Volatile.Read(ref stopCalls));
        Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));
    }

    [Fact]
    public async Task GetSettings_CanceledWhileMutationIsHeld_PropagatesCancellation()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
        var application = new LocalInstanceApplication(manager);
        using var mutation = manager.AcquireInstanceMutation(config.Uuid);
        using var cancellationSource = new CancellationTokenSource();

        var getTask = application.GetInstanceSettingsAsync(
            new InstanceReference(config.Uuid),
            cancellationSource.Token);
        Assert.False(getTask.IsCompleted);

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => getTask.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void AuthoritativeCatalog_OnlyPublishesActualCatalogOrStatusChanges()
    {
        var config = CreateConfig();
        var instance = new TestInstance(config, () => null);
        var source = new AuthoritativeInstanceSnapshotSource(
        [
            new KeyValuePair<Guid, IInstance>(config.Uuid, instance)
        ]);

        Assert.Equal(0, source.Current.Version);

        source.Upsert(instance);
        Assert.Equal(0, source.Current.Version);

        instance.Status = InstanceStatus.Running;
        source.Upsert(instance);
        Assert.Equal(1, source.Current.Version);

        source.Upsert(instance);
        Assert.Equal(1, source.Current.Version);

        source.Remove(config.Uuid);
        Assert.Equal(2, source.Current.Version);
        Assert.False(source.TryGet(config.Uuid, out _));
    }

    [Fact]
    public async Task RemoveInstance_DisposeFailure_PreservesCommittedRemoval()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var disposeCalls = 0;
        var instance = new TestInstance(
            config,
            () => null,
            dispose: () =>
            {
                Interlocked.Increment(ref disposeCalls);
                throw new InvalidOperationException("dispose failed");
            });
        manager.ReplaceInstance(config.Uuid, instance);
        var beforeVersion = manager.InstanceSnapshotSource.Current.Version;

        var removed = await manager.TryRemoveInstance(config.Uuid);

        Assert.True(removed);
        Assert.Equal(1, Volatile.Read(ref disposeCalls));
        Assert.False(manager.Instances.ContainsKey(config.Uuid));
        Assert.False(manager.InstanceSnapshotSource.TryGet(config.Uuid, out _));
        Assert.Equal(beforeVersion + 1, manager.InstanceSnapshotSource.Current.Version);
    }

    [Fact]
    public async Task AuthoritativeCatalog_ReportLogPlayerAndPerformanceActivity_DoesNotRepublish()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var instance = new ActivityInstance(config);
        manager.ReplaceInstance(config.Uuid, instance);
        var application = new LocalInstanceApplication(manager);
        var before = manager.InstanceSnapshotSource.Current;

        instance.AddLog("player joined");
        instance.SetReport(
            new Dictionary<string, string> { ["motd"] = "ready" },
            [new Player("Alex", Guid.NewGuid())],
            new InstancePerformanceCounter(37.5, 512));
        instance.RaiseLog("player joined");

        var reportResult = await application.GetInstanceReportAsync(
            new InstanceReference(config.Uuid),
            CancellationToken.None);
        var logResult = await application.GetInstanceLogAsync(
            new InstanceLogQuery(config.Uuid),
            CancellationToken.None);

        Assert.True(reportResult.IsOk(out var report));
        Assert.Single(report.Players);
        Assert.Equal(37.5, report.PerformanceCounter.Cpu);
        Assert.True(logResult.IsOk(out var logs));
        Assert.Single(logs.Logs);
        Assert.Equal("player joined", logs.Logs[0]);
        Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
        Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
    }

    [Fact]
    public async Task UpdateSettings_PersistenceFailure_KeepsLiveConfigAndCatalogUnchanged()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;
            var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
            File.Delete(configPath);
            Directory.CreateDirectory(configPath);

            var result = await application.UpdateInstanceSettingsAsync(
                CreateSettingsRequest(config, name: "persist-failure"),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<StorageDaemonError>(error);
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_InstanceConstructionFailure_DoesNotPersistOrPublishChanges()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(instanceDirectory);
        FileManager.WriteJsonAndBackup(Path.Combine(instanceDirectory, InstanceConfig.FileName), config);

        try
        {
            var manager = new InstanceManager(_ => throw new InvalidOperationException("construction failed"));
            manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                CreateSettingsRequest(config, name: "construction-failure"),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<StorageDaemonError>(error);
            var persisted = FileManager.ReadJson<InstanceConfig>(Path.Combine(instanceDirectory, InstanceConfig.FileName));
            Assert.NotNull(persisted);
            Assert.Equal(config.Name, persisted.Name);
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_RuntimeCommitFailure_RollsBackConfigAndCoreJournal()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var replacementTargetPath = Path.Combine(instanceDirectory, "replacement.jar");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"phase2-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        FileManager.WriteJsonAndBackup(configPath, config);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalBackupBytes = "original-config-backup"u8.ToArray();
        await File.WriteAllBytesAsync(configPath + ".bak", originalBackupBytes);

        try
        {
            var manager = new InstanceManager(_ => new SnapshotFaultInstance());
            var originalInstance = new TestInstance(config, () => null);
            manager.ReplaceInstance(config.Uuid, originalInstance);
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;
            var request = new UpdateInstanceSettingsRequest(
                config.Uuid,
                "runtime-commit-failure",
                config.InstanceType,
                config.JavaPath,
                config.Arguments.ToImmutableArray(),
                config.Version,
                new InstanceCoreReplacementRequest(uploadedPath, Path.GetFileName(replacementTargetPath)),
                false);

            var result = await application.UpdateInstanceSettingsAsync(request, CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<StorageDaemonError>(error);
            var persisted = FileManager.ReadJson<InstanceConfig>(configPath);
            Assert.NotNull(persisted);
            Assert.Equal(config.Name, persisted.Name);
            Assert.Equal(config.Target, persisted.Target);
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalBackupBytes, await File.ReadAllBytesAsync(configPath + ".bak"));
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.False(File.Exists(replacementTargetPath));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            var backupDirectory = Path.Combine(instanceDirectory, "backup");
            if (Directory.Exists(backupDirectory))
                Assert.Empty(Directory.EnumerateFileSystemEntries(backupDirectory));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_RuntimeCommitFailure_RestoresAbsentConfigBackup()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"phase2-absent-backup-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(Path.Combine(instanceDirectory, config.Target), "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        FileManager.WriteJsonAndBackup(configPath, config);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        Assert.False(File.Exists(configPath + ".bak"));

        try
        {
            var manager = new InstanceManager(_ => new SnapshotFaultInstance());
            manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
            var application = new LocalInstanceApplication(manager);

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "runtime-commit-failure",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    false),
                CancellationToken.None);

            Assert.True(result.IsErr(out _));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.False(File.Exists(configPath + ".bak"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("nested/outside.jar")]
    [InlineData("C:\\outside.jar")]
    public async Task UpdateSettings_ReplacementTargetTraversal_IsRejectedBeforeStorageMutation(string target)
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var outsidePath = Path.Combine(FileManager.InstancesRoot, $"outside-{Guid.NewGuid():N}.txt");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"target-validation-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(outsidePath, "outside-sentinel");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");

        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "unsafe-target",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, target),
                    false),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("outside-sentinel", await File.ReadAllTextAsync(outsidePath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_TamperedGeneratedPath_IsRejectedBeforeStorageMutation()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var outsidePath = Path.Combine(FileManager.InstancesRoot, $"outside-generated-{Guid.NewGuid():N}.txt");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"metadata-validation-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(outsidePath, "outside-sentinel");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        InstanceInstallMetadataStore.Write(instanceDirectory, new MCServerLauncher.Common.ProtoType.Action.InstanceInstallMetadata
        {
            GeneratedPaths = ["../" + Path.GetFileName(outsidePath)]
        });

        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "unsafe-metadata",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("outside-sentinel", await File.ReadAllTextAsync(outsidePath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            if (File.Exists(outsidePath))
                File.Delete(outsidePath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task ExternalConfigWrite_DoesNotChangeRuntimeCatalogOrInstance()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;
            var externalConfig = config with { Name = "external-write" };
            FileManager.WriteJsonAndBackup(
                Path.Combine(instanceDirectory, InstanceConfig.FileName),
                externalConfig);

            var report = await application.GetInstanceReportAsync(
                new InstanceReference(config.Uuid),
                CancellationToken.None);

            Assert.True(report.IsOk(out var value));
            Assert.Equal(config.Name, value.Config.Name);
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var snapshot));
            Assert.Equal(config.Name, snapshot.Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_ConcurrentRequests_ConvergesCatalogAndPersistence()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            var application = new LocalInstanceApplication(manager);
            var beforeVersion = manager.InstanceSnapshotSource.Current.Version;
            var updates = new[]
            {
                ("concurrent-settings-0", InstanceType.MCJava, "concurrent-version-0"),
                ("concurrent-settings-1", InstanceType.Universal, "concurrent-version-1"),
                ("concurrent-settings-2", InstanceType.MCBedrock, "concurrent-version-2"),
                ("concurrent-settings-3", InstanceType.Terraria, "concurrent-version-3")
            };

            var results = await Task.WhenAll(updates.Select(update =>
                    application.UpdateInstanceSettingsAsync(
                        CreateSettingsRequest(config, update.Item1, update.Item2, update.Item3),
                        CancellationToken.None)))
                .WaitAsync(TimeSpan.FromSeconds(3));

            var successfulConfigs = results.Select(result =>
            {
                Assert.True(result.IsOk(out var value));
                return value.Config;
            }).ToArray();

            var persisted = FileManager.ReadJson<InstanceConfig>(Path.Combine(instanceDirectory, InstanceConfig.FileName));
            Assert.NotNull(persisted);
            var runtime = manager.Instances[config.Uuid].Config;
            Assert.True(manager.InstanceSnapshotSource.TryGet(config.Uuid, out var snapshot));

            Assert.Equal(beforeVersion + updates.Length, manager.InstanceSnapshotSource.Current.Version);
            Assert.Equal(persisted.Name, runtime.Name);
            Assert.Equal(persisted.Version, runtime.Version);
            Assert.Equal(persisted.InstanceType, runtime.InstanceType);
            Assert.Equal(persisted.Name, snapshot.Name);
            Assert.Equal(persisted.Version, snapshot.Version);
            Assert.Equal(persisted.InstanceType, snapshot.InstanceType);
            Assert.Contains(successfulConfigs, candidate =>
                candidate.Name == persisted.Name &&
                candidate.Version == persisted.Version &&
                candidate.InstanceType == persisted.InstanceType);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public void AuthoritativeCatalog_ConcurrentChanges_PreservesConsistentCatalogAndVersions()
    {
        var instances = Enumerable.Range(0, 32)
            .Select(index => new TestInstance(CreateConfig(name: $"concurrent-{index}"), () => null))
            .ToArray();
        var source = new AuthoritativeInstanceSnapshotSource([]);
        var failures = new ConcurrentQueue<Exception>();

        Parallel.ForEach(instances, instance =>
        {
            try
            {
                source.Upsert(instance);
                source.Upsert(instance);
                source.Remove(instance.Config.Uuid);
                source.Upsert(instance);
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        Assert.Empty(failures);
        Assert.Equal(instances.Length * 3, source.Current.Version);
        Assert.Equal(instances.Length, source.Current.Value.Instances.Count);
        Assert.All(instances, instance => Assert.True(source.TryGet(instance.Config.Uuid, out _)));
    }

    [Fact]
    public async Task StatusChange_InterleavedWithRemove_DoesNotRepublishRemovedInstance()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var instance = new StatusInterleavingInstance(config);
        manager.ReplaceInstance(config.Uuid, instance);
        instance.BlockNextConfigRead();

        var statusTask = Task.Run(() => instance.RaiseStatusChanged(InstanceStatus.Running));
        await instance.ConfigReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var removeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removeTask = Task.Run(() =>
        {
            removeStarted.TrySetResult(true);
            return manager.TryRemoveInstance(config.Uuid).GetAwaiter().GetResult();
        });
        await removeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        instance.ReleaseConfigRead();
        await statusTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(await removeTask.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(manager.InstanceSnapshotSource.TryGet(config.Uuid, out _));
    }

    private static (InstanceManager Manager, InstanceConfig Config, string InstanceDirectory) CreateLoadedManager()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(instanceDirectory);
        FileManager.WriteJsonAndBackup(Path.Combine(instanceDirectory, InstanceConfig.FileName), config);

        return (Assert.IsType<InstanceManager>(InstanceManager.Create([instanceDirectory])), config, instanceDirectory);
    }

    private static UpdateInstanceSettingsRequest CreateSettingsRequest(
        InstanceConfig config,
        string name,
        InstanceType? instanceType = null,
        string? version = null)
    {
        return new UpdateInstanceSettingsRequest(
            config.Uuid,
            name,
            instanceType ?? config.InstanceType,
            config.JavaPath,
            config.Arguments.ToImmutableArray(),
            version ?? config.Version,
            null,
            false);
    }

    private static void DeleteInstanceDirectory(string instanceDirectory)
    {
        if (Directory.Exists(instanceDirectory))
            Directory.Delete(instanceDirectory, true);
    }

    private static InstanceConfig CreateConfig(string? name = null)
    {
        return new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = name ?? "application-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            Version = "1.20.1",
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private sealed class TestInstance(
        InstanceConfig config,
        Func<InstanceProcess?> process,
        InstanceStatus status = InstanceStatus.Stopped,
        Func<CancellationToken, Task<bool>>? startAsync = null,
        Action? stop = null,
        Action? dispose = null) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => process();
        public InstanceStatus Status { get; set; } = status;
        public int ServerProcessId => -1;

        public event Action<Guid, string>? OnLog
        {
            add { }
            remove { }
        }

        public event Action<Guid, InstanceStatus>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LegacyInstanceReport(Status, Config, [], [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return startAsync?.Invoke(ct) ?? Task.FromResult(false);
        }

        public void Stop()
        {
            stop?.Invoke();
        }

        public IReadOnlyList<string> GetLogHistory()
        {
            return [];
        }

        public void Dispose()
        {
            dispose?.Invoke();
        }
    }

    private sealed class ActivityInstance(InstanceConfig config) : IInstance
    {
        private readonly List<string> _logs = [];
        private Dictionary<string, string> _properties = [];
        private Player[] _players = [];
        private InstancePerformanceCounter _performance;

        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => null;
        public InstanceStatus Status => InstanceStatus.Stopped;
        public int ServerProcessId => -1;
        public event Action<Guid, string>? OnLog;
        public event Action<Guid, InstanceStatus>? OnStatusChanged;

        public void AddLog(string log)
        {
            _logs.Add(log);
        }

        public void SetReport(
            Dictionary<string, string> properties,
            Player[] players,
            InstancePerformanceCounter performance)
        {
            _properties = properties;
            _players = players;
            _performance = performance;
        }

        public void RaiseLog(string log)
        {
            OnLog?.Invoke(Config.Uuid, log);
        }

        public void RaiseStatus(InstanceStatus status)
        {
            OnStatusChanged?.Invoke(Config.Uuid, status);
        }

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LegacyInstanceReport(Status, Config, _properties, _players, _performance));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory() => _logs;

        public void Dispose()
        {
        }
    }

    private sealed class StatusInterleavingInstance(InstanceConfig config) : IInstance
    {
        private readonly TaskCompletionSource<bool> _configReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseConfigRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<Guid, InstanceStatus>? _statusChanged;
        private int _blockConfigRead;

        public TaskCompletionSource<bool> ConfigReadStarted => _configReadStarted;

        public InstanceConfig Config
        {
            get
            {
                if (Interlocked.Exchange(ref _blockConfigRead, 0) == 1)
                {
                    _configReadStarted.TrySetResult(true);
                    _releaseConfigRead.Task.GetAwaiter().GetResult();
                }

                return config;
            }
        }

        public InstanceProcess? Process => null;

        public InstanceStatus Status => InstanceStatus.Stopped;

        public int ServerProcessId => -1;

        public event Action<Guid, string>? OnLog
        {
            add { }
            remove { }
        }

        public event Action<Guid, InstanceStatus>? OnStatusChanged
        {
            add => _statusChanged += value;
            remove => _statusChanged -= value;
        }

        public void BlockNextConfigRead()
        {
            Volatile.Write(ref _blockConfigRead, 1);
        }

        public void RaiseStatusChanged(InstanceStatus status)
        {
            _statusChanged?.Invoke(config.Uuid, status);
        }

        public void ReleaseConfigRead()
        {
            _releaseConfigRead.TrySetResult(true);
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

        public IReadOnlyList<string> GetLogHistory()
        {
            return [];
        }

        public void Dispose()
        {
        }
    }

    private sealed class SnapshotFaultInstance : IInstance
    {
        public InstanceConfig Config => throw new InvalidOperationException("snapshot failed");

        public InstanceProcess? Process => null;

        public InstanceStatus Status => InstanceStatus.Stopped;

        public int ServerProcessId => -1;

        public event Action<Guid, string>? OnLog
        {
            add { }
            remove { }
        }

        public event Action<Guid, InstanceStatus>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Stop() => throw new NotSupportedException();

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }
}
