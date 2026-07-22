using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

[Collection("InstanceFactoryRegistryIsolation")]
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
    public async Task HaltInstance_ProcessFailure_ReturnsTypedFailure()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        manager.Instances[config.Uuid] = new TestInstance(config, () => throw new InvalidOperationException("kill failed"));
        var application = new LocalInstanceApplication(manager);

        var result = await application.HaltInstanceAsync(new InstanceReference(config.Uuid), CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.IsType<InternalDaemonError>(error);
        Assert.Equal("instance.halt_failed", error.Code);
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
        // Stub Stop does not publish a terminal status; the instance remains mapped until one does.
        Assert.True(manager.RunningInstances.ContainsKey(config.Uuid));
    }

    [Fact]
    public async Task HaltInstance_WaitsForConcurrentStartThenKillsThatCommittedGeneration()
    {
        var manager = new InstanceManager();
        var config = CreateConfig();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var haltCalls = 0;
        var instance = new TestInstance(
            config,
            () => null,
            startAsync: async _ =>
            {
                startEntered.TrySetResult();
                await releaseStart.Task;
                return true;
            },
            halt: () => Interlocked.Increment(ref haltCalls));
        manager.ReplaceInstance(config.Uuid, instance);
        var application = new LocalInstanceApplication(manager);

        var startTask = application.StartInstanceAsync(
            new InstanceReference(config.Uuid),
            CancellationToken.None);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var haltTask = application.HaltInstanceAsync(
            new InstanceReference(config.Uuid),
            CancellationToken.None);
        Assert.False(haltTask.IsCompleted);

        releaseStart.TrySetResult();
        Assert.True((await startTask.WaitAsync(TimeSpan.FromSeconds(3))).IsOk(out _));
        Assert.True((await haltTask.WaitAsync(TimeSpan.FromSeconds(3))).IsOk(out _));
        Assert.Equal(1, Volatile.Read(ref haltCalls));
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
    public async Task GetSettings_CorruptInstallMetadata_PreservesTypedStorageError()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            await File.WriteAllTextAsync(
                InstanceInstallMetadataStore.GetPath(instanceDirectory),
                "{ not valid json }");
            var application = new LocalInstanceApplication(manager);

            var result = await application.GetInstanceSettingsAsync(
                new InstanceReference(config.Uuid),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var storage = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.install_metadata.read_failed", storage.Code);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public void AuthoritativeCatalog_OnlyPublishesActualCatalogOrStatusChanges()
    {
        var config = CreateConfig();
        var instance = new TestInstance(config, () => null);
        var source = new AuthoritativeInstanceSnapshotSource(
        [
            new KeyValuePair<Guid, IInstance>(config.Uuid, instance)
        ], new InstanceCatalogCommitFeed());

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
        await instance.RaiseLogAsync("player joined");

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
            var internalError = Assert.IsType<InternalDaemonError>(error);
            Assert.Equal("instance.construct_failed", internalError.Code);
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
            var internalError = Assert.IsType<InternalDaemonError>(error);
            Assert.Equal("instance.update.commit_failed", internalError.Code);
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

    [Theory]
    [InlineData(InstanceConfig.FileName)]
    [InlineData(InstanceConfig.FileName + ".bak")]
    [InlineData(InstanceInstallMetadataStore.FileName)]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak")]
    [InlineData(InstanceConfig.FileName + ".")]
    [InlineData(InstanceConfig.FileName + " ")]
    [InlineData(InstanceConfig.FileName + ".bak.")]
    [InlineData(InstanceConfig.FileName + ".bak ")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".")]
    [InlineData(InstanceInstallMetadataStore.FileName + " ")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak.")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak ")]
    [InlineData("NUL")]
    [InlineData("nul.txt")]
    [InlineData("CON")]
    [InlineData("PRN.log")]
    [InlineData("AUX")]
    [InlineData("CLOCK$")]
    [InlineData("COM1")]
    [InlineData("com9.jar")]
    [InlineData("LPT1")]
    [InlineData("lpt9.log")]
    public async Task UpdateSettings_ReservedReplacementTarget_IsRejectedBeforeStorageMutation(string target)
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"reserved-target-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "reserved-target-test",
                null,
                ImmutableArray<string>.Empty,
                config.Target,
                DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
        await File.WriteAllTextAsync(metadataPath + ".bak", "metadata-backup");
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);
        var originalMetadataBackupBytes = await File.ReadAllBytesAsync(metadataPath + ".bak");

        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "reserved-target",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, target),
                    false),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var validation = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("instance.settings_invalid", validation.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath));
            Assert.Equal(originalMetadataBackupBytes, await File.ReadAllBytesAsync(metadataPath + ".bak"));
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateSettings_ExistingReplacementDestination_IsRejectedBeforeStorageMutation(
        bool destinationIsDirectory)
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var collisionPath = Path.Combine(instanceDirectory, "world");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"destination-collision-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        if (destinationIsDirectory)
        {
            Directory.CreateDirectory(collisionPath);
            await File.WriteAllTextAsync(Path.Combine(collisionPath, "level.dat"), "world-data");
        }
        else
        {
            await File.WriteAllTextAsync(collisionPath, "existing-file");
        }

        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);

        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "destination-collision",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, Path.GetFileName(collisionPath)),
                    false),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var conflict = Assert.IsType<ConflictDaemonError>(error);
            Assert.Equal("instance.replacement_core.target_conflict", conflict.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            if (destinationIsDirectory)
                Assert.Equal("world-data", await File.ReadAllTextAsync(Path.Combine(collisionPath, "level.dat")));
            else
                Assert.Equal("existing-file", await File.ReadAllTextAsync(collisionPath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_ReplacementSourceTraversal_ReturnsTypedValidationError()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            var application = new LocalInstanceApplication(manager);
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "unsafe-source",
                    config.InstanceType,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest("../outside.jar", "replacement.jar"),
                    false),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var validation = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("instance.replacement_core.invalid", validation.Code);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_CanceledInstallerWithoutMetadata_RollsBackGeneratedOutputs()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var replacementTargetPath = Path.Combine(instanceDirectory, "replacement.jar");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"rollback-{Guid.NewGuid():N}.jar");
        var librariesPath = Path.Combine(instanceDirectory, "libraries");
        var runScriptPath = Path.Combine(instanceDirectory, "run.bat");
        var userLibraryPath = Path.Combine(librariesPath, "user-owned.jar");
        var installerLogPath = Path.Combine(instanceDirectory, "installer.log");
        var eulaPath = Path.Combine(instanceDirectory, "eula.txt");
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(librariesPath);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        await File.WriteAllTextAsync(userLibraryPath, "user-library");
        await File.WriteAllTextAsync(runScriptPath, "user-run-script");
        FileManager.WriteJsonAndBackup(configPath, config);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        Assert.False(File.Exists(InstanceInstallMetadataStore.GetPath(instanceDirectory)));

        var manager = new InstanceManager();
        var originalInstance = new TestInstance(config, () => null);
        manager.ReplaceInstance(config.Uuid, originalInstance);
        var application = new LocalInstanceApplication(manager);
        using var cancellationSource = new CancellationTokenSource();
        PartialOutputCancelingFactory.CancellationSource = cancellationSource;
        InstanceFactoryRegistry.Reset();
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(PartialOutputCancelingFactory));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                application.UpdateInstanceSettingsAsync(
                    new UpdateInstanceSettingsRequest(
                        config.Uuid,
                        "canceled-installer",
                        InstanceType.MCBedrock,
                        config.JavaPath,
                        config.Arguments.ToImmutableArray(),
                        config.Version,
                        new InstanceCoreReplacementRequest(uploadedPath, Path.GetFileName(replacementTargetPath)),
                        true),
                    cancellationSource.Token));

            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.False(File.Exists(replacementTargetPath));
            Assert.Equal("user-library", await File.ReadAllTextAsync(userLibraryPath));
            Assert.Equal("user-run-script", await File.ReadAllTextAsync(runScriptPath));
            Assert.False(File.Exists(Path.Combine(librariesPath, "partial.jar")));
            Assert.False(File.Exists(installerLogPath));
            Assert.False(File.Exists(eulaPath));
            Assert.False(File.Exists(InstanceInstallMetadataStore.GetPath(instanceDirectory)));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(PartialOutputCancelingFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(PartialOutputCancelingFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            PartialOutputCancelingFactory.CancellationSource = null;
            DeleteFactoryWorkspace(PartialOutputCancelingFactory.LastWorkingDirectory);
            PartialOutputCancelingFactory.LastWorkingDirectory = null;
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Theory]
    [InlineData(false, "instance.factory.test_failed")]
    [InlineData(true, "instance.factory.failed")]
    public async Task UpdateSettings_FactoryFailureWithIncompleteMetadata_LeavesLiveFilesUnchanged(
        bool throwException,
        string expectedCode)
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"factory-error-{Guid.NewGuid():N}.jar");
        var librariesPath = Path.Combine(instanceDirectory, "libraries");
        var userLibraryPath = Path.Combine(librariesPath, "user-owned.jar");
        var runScriptPath = Path.Combine(instanceDirectory, "run.bat");
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        Directory.CreateDirectory(librariesPath);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        await File.WriteAllTextAsync(userLibraryPath, "user-library");
        await File.WriteAllTextAsync(runScriptPath, "user-run-script");
        FileManager.WriteJsonAndBackup(configPath, config);
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "legacy-test",
                "legacy-installer.jar",
                ImmutableArray.Create("libraries/declared-output.jar"),
                config.Target,
                DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
        await File.WriteAllTextAsync(metadataPath + ".bak", "metadata-backup-sentinel");
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);
        var originalMetadataBackupBytes = await File.ReadAllBytesAsync(metadataPath + ".bak");

        var manager = new InstanceManager();
        var originalInstance = new TestInstance(config, () => null);
        manager.ReplaceInstance(config.Uuid, originalInstance);
        var application = new LocalInstanceApplication(manager);
        InstanceFactoryRegistry.Reset();
        PartialOutputFailureFactory.ThrowException = throwException;
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(PartialOutputFailureFactory));

        try
        {
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "failed-installer",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.Equal(expectedCode, error!.Code);
            if (throwException)
                Assert.IsType<InternalDaemonError>(error);
            else
                Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal("user-library", await File.ReadAllTextAsync(userLibraryPath));
            Assert.Equal("user-run-script", await File.ReadAllTextAsync(runScriptPath));
            Assert.False(File.Exists(Path.Combine(librariesPath, "partial.jar")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "installer.log")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "eula.txt")));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath));
            Assert.Equal(originalMetadataBackupBytes, await File.ReadAllBytesAsync(metadataPath + ".bak"));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(PartialOutputFailureFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(PartialOutputFailureFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            DeleteFactoryWorkspace(PartialOutputFailureFactory.LastWorkingDirectory);
            PartialOutputFailureFactory.LastWorkingDirectory = null;
            PartialOutputFailureFactory.ThrowException = false;
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_SuccessfulInstallerMerge_ConstructsFromCommittedOutputs()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"factory-success-{Guid.NewGuid():N}.jar");
        var librariesPath = Path.Combine(instanceDirectory, "libraries");
        var userLibraryPath = Path.Combine(librariesPath, "user-owned.jar");
        Directory.CreateDirectory(librariesPath);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        await File.WriteAllTextAsync(userLibraryPath, "user-library");
        FileManager.WriteJsonAndBackup(configPath, config);

        string? observedProperties = null;
        var observedTargetExists = false;
        var manager = new InstanceManager(updatedConfig =>
        {
            observedProperties = File.ReadAllText(Path.Combine(instanceDirectory, "server.properties"));
            observedTargetExists = File.Exists(Path.Combine(instanceDirectory, updatedConfig.Target));
            return new TestInstance(updatedConfig, () => null);
        });
        manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
        var application = new LocalInstanceApplication(manager);
        InstanceFactoryRegistry.Reset();
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(SuccessfulReplacementFactory));

        try
        {
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "successful-installer",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsOk(out var update));
            Assert.Equal(SuccessfulReplacementFactory.LaunchTarget, update.Config.Target);
            Assert.Equal("server-port=25570", observedProperties);
            Assert.True(observedTargetExists);
            Assert.Equal("user-library", await File.ReadAllTextAsync(userLibraryPath));
            Assert.Equal(
                "factory-library",
                await File.ReadAllTextAsync(Path.Combine(librariesPath, "factory.jar")));
            Assert.Equal("installer-log", await File.ReadAllTextAsync(Path.Combine(instanceDirectory, "installer.log")));
            Assert.Equal("eula=true", await File.ReadAllTextAsync(Path.Combine(instanceDirectory, "eula.txt")));
            Assert.Equal("generated-launcher", await File.ReadAllTextAsync(
                Path.Combine(instanceDirectory, SuccessfulReplacementFactory.LaunchTarget)));
            Assert.False(File.Exists(originalTargetPath));
            var preservedPath = Assert.Single(update.PreservedOriginalPaths);
            Assert.Equal("original-core", await File.ReadAllTextAsync(preservedPath));
            Assert.Empty(update.DeletedGeneratedPaths);

            var metadata = InstanceInstallMetadataStore.Read(instanceDirectory);
            Assert.NotNull(metadata);
            Assert.Equal(InstanceType.MCBedrock.ToString(), metadata.InstallerKind);
            Assert.Equal(uploadedPath, metadata.InstallerSourcePath);
            Assert.Equal(SuccessfulReplacementFactory.LaunchTarget, metadata.ResolvedLaunchTarget);
            Assert.Equal(
                ["eula.txt", SuccessfulReplacementFactory.LaunchTarget, "installer.log", "libraries", "server.properties"],
                metadata.GeneratedPaths.ToArray());
            Assert.Equal(SuccessfulReplacementFactory.LaunchTarget, manager.Instances[config.Uuid].Config.Target);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(SuccessfulReplacementFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(SuccessfulReplacementFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            DeleteFactoryWorkspace(SuccessfulReplacementFactory.LastWorkingDirectory);
            SuccessfulReplacementFactory.LastWorkingDirectory = null;
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_ConstructionFailureAfterInstallerMerge_RollsBackStorageAndMetadata()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"construction-rollback-{Guid.NewGuid():N}.jar");
        var librariesPath = Path.Combine(instanceDirectory, "libraries");
        var userLibraryPath = Path.Combine(librariesPath, "user-owned.jar");
        var propertiesPath = Path.Combine(instanceDirectory, "server.properties");
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        Directory.CreateDirectory(librariesPath);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        await File.WriteAllTextAsync(userLibraryPath, "user-library");
        await File.WriteAllTextAsync(propertiesPath, "server-port=25560");
        FileManager.WriteJsonAndBackup(configPath, config);
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "old-installer",
                "old-source.jar",
                ImmutableArray<string>.Empty,
                config.Target,
                DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
        await File.WriteAllTextAsync(metadataPath + ".bak", "old-metadata-backup");
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);
        var originalMetadataBackupBytes = await File.ReadAllBytesAsync(metadataPath + ".bak");

        var manager = new InstanceManager(_ => throw new InvalidOperationException("construction failed"));
        var originalInstance = new TestInstance(config, () => null);
        manager.ReplaceInstance(config.Uuid, originalInstance);
        var application = new LocalInstanceApplication(manager);
        InstanceFactoryRegistry.Reset();
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(SuccessfulReplacementFactory));

        try
        {
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "construction-rollback",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var internalError = Assert.IsType<InternalDaemonError>(error);
            Assert.Equal("instance.construct_failed", internalError.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal("user-library", await File.ReadAllTextAsync(userLibraryPath));
            Assert.Equal("server-port=25560", await File.ReadAllTextAsync(propertiesPath));
            Assert.False(File.Exists(Path.Combine(librariesPath, "factory.jar")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "installer.log")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "eula.txt")));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, SuccessfulReplacementFactory.LaunchTarget)));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath));
            Assert.Equal(originalMetadataBackupBytes, await File.ReadAllBytesAsync(metadataPath + ".bak"));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(SuccessfulReplacementFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(SuccessfulReplacementFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            DeleteFactoryWorkspace(SuccessfulReplacementFactory.LastWorkingDirectory);
            SuccessfulReplacementFactory.LastWorkingDirectory = null;
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_InstallerOutputTypeCollision_RollsBackWithoutDeletingUserDirectory()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var worldPath = Path.Combine(instanceDirectory, "world");
        var worldDataPath = Path.Combine(worldPath, "level.dat");
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"factory-collision-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(worldPath);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(worldDataPath, "world-data");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        FileManager.WriteJsonAndBackup(configPath, config);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);

        var manager = new InstanceManager();
        var originalInstance = new TestInstance(config, () => null);
        manager.ReplaceInstance(config.Uuid, originalInstance);
        var application = new LocalInstanceApplication(manager);
        var before = manager.InstanceSnapshotSource.Current;
        InstanceFactoryRegistry.Reset();
        SuccessfulReplacementFactory.CreateTypeCollision = true;
        InstanceFactoryRegistry.LoadFactoryFromType(typeof(SuccessfulReplacementFactory));

        try
        {
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "factory-collision",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var storage = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.settings.commit_failed", storage.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal("world-data", await File.ReadAllTextAsync(worldDataPath));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, SuccessfulReplacementFactory.LaunchTarget)));
            Assert.False(File.Exists(Path.Combine(instanceDirectory, "installer.log")));
            Assert.False(File.Exists(InstanceInstallMetadataStore.GetPath(instanceDirectory)));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(SuccessfulReplacementFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(SuccessfulReplacementFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            SuccessfulReplacementFactory.CreateTypeCollision = false;
            DeleteFactoryWorkspace(SuccessfulReplacementFactory.LastWorkingDirectory);
            SuccessfulReplacementFactory.LastWorkingDirectory = null;
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_FactoryOutputDirectoryReparsePoint_DoesNotMoveExternalFiles()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"factory-reparse-{Guid.NewGuid():N}.jar");
        var externalDirectory = Path.Combine(Path.GetTempPath(), $"mcsl-factory-external-{Guid.NewGuid():N}");
        var externalSentinelPath = Path.Combine(externalDirectory, "sentinel.txt");
        var probeLinkPath = Path.Combine(Path.GetTempPath(), $"mcsl-factory-link-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(FileManager.UploadRoot);
        Directory.CreateDirectory(externalDirectory);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        await File.WriteAllTextAsync(externalSentinelPath, "external-sentinel");
        FileManager.WriteJsonAndBackup(configPath, config);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);

        var manager = new InstanceManager();
        var originalInstance = new TestInstance(config, () => null);
        manager.ReplaceInstance(config.Uuid, originalInstance);
        var application = new LocalInstanceApplication(manager);
        var before = manager.InstanceSnapshotSource.Current;

        try
        {
            if (!TryCreateDirectorySymbolicLink(probeLinkPath, externalDirectory))
                return;
            Directory.Delete(probeLinkPath);

            InstanceFactoryRegistry.Reset();
            ReparsePointReplacementFactory.ExternalDirectory = externalDirectory;
            InstanceFactoryRegistry.LoadFactoryFromType(typeof(ReparsePointReplacementFactory));

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "factory-reparse",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var storage = Assert.IsType<StorageDaemonError>(error);
            Assert.Equal("instance.settings.commit_failed", storage.Code);
            Assert.Equal("external-sentinel", await File.ReadAllTextAsync(externalSentinelPath));
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "linked-output")));
            Assert.False(File.Exists(InstanceInstallMetadataStore.GetPath(instanceDirectory)));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
            Assert.NotNull(ReparsePointReplacementFactory.LastWorkingDirectory);
            Assert.False(Directory.Exists(ReparsePointReplacementFactory.LastWorkingDirectory));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            ReparsePointReplacementFactory.ExternalDirectory = null;
            DeleteFactoryWorkspace(ReparsePointReplacementFactory.LastWorkingDirectory);
            ReparsePointReplacementFactory.LastWorkingDirectory = null;
            if (Directory.Exists(probeLinkPath))
                Directory.Delete(probeLinkPath);
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
            if (Directory.Exists(externalDirectory))
                Directory.Delete(externalDirectory, true);
        }
    }

    [Fact]
    public async Task UpdateSettings_CaseSensitiveWindowsAliases_AreRejectedBeforeStorageMutation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"factory-case-sensitive-{Guid.NewGuid():N}.jar");
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        Directory.CreateDirectory(instanceDirectory);

        try
        {
            if (!await TryEnableDirectoryCaseSensitivityAsync(instanceDirectory))
                return;
            if (!await TryCreateCaseDistinctFilesAsync(instanceDirectory))
                return;

            Directory.CreateDirectory(FileManager.UploadRoot);
            await File.WriteAllTextAsync(originalTargetPath, "original-core");
            await File.WriteAllTextAsync(uploadedPath, "replacement-core");
            Assert.Equal(
                ["upper-original", "lower-original"],
                await ReadCaseDistinctFilesAsync(instanceDirectory));
            FileManager.WriteJsonAndBackup(configPath, config);
            InstanceInstallMetadataStore.Write(
                instanceDirectory,
                new InstanceInstallMetadata(
                    "case-sensitive-test",
                    null,
                    ImmutableArray.Create("Core.jar", "core.jar"),
                    config.Target,
                    DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
            var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
            var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);

            var manager = new InstanceManager();
            var originalInstance = new TestInstance(config, () => null);
            manager.ReplaceInstance(config.Uuid, originalInstance);
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "case-sensitive-rollback",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var validationError = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal("instance.generated_path.invalid", validationError.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal(
                ["upper-original", "lower-original"],
                await ReadCaseDistinctFilesAsync(instanceDirectory));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath));
            Assert.Same(originalInstance, manager.Instances[config.Uuid]);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            InstanceFactoryRegistry.Reset();
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateSettings_DirectReplacement_UpdatesMetadataAndPreservesPreviousBackup()
    {
        var config = CreateConfig();
        var instanceDirectory = config.GetWorkingDirectory();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"direct-replacement-{Guid.NewGuid():N}.jar");
        var replacementTargetPath = Path.Combine(instanceDirectory, "replacement.jar");
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        Directory.CreateDirectory(instanceDirectory);
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        FileManager.WriteJsonAndBackup(configPath, config);
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "old-installer",
                "old-source.jar",
                ImmutableArray.Create("libraries"),
                config.Target,
                DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
        var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);

        var manager = new InstanceManager(updatedConfig => new TestInstance(updatedConfig, () => null));
        manager.ReplaceInstance(config.Uuid, new TestInstance(config, () => null));
        var application = new LocalInstanceApplication(manager);

        try
        {
            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "direct-replacement",
                    InstanceType.MCJava,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, Path.GetFileName(replacementTargetPath)),
                    false),
                CancellationToken.None);

            Assert.True(result.IsOk(out var update));
            Assert.Equal("replacement-core", await File.ReadAllTextAsync(replacementTargetPath));
            Assert.False(File.Exists(originalTargetPath));
            Assert.Equal("original-core", await File.ReadAllTextAsync(Assert.Single(update.PreservedOriginalPaths)));
            var metadata = InstanceInstallMetadataStore.Read(instanceDirectory);
            Assert.NotNull(metadata);
            Assert.Equal(InstanceType.MCJava.ToString(), metadata.InstallerKind);
            Assert.Equal(uploadedPath, metadata.InstallerSourcePath);
            Assert.Equal(Path.GetFileName(replacementTargetPath), metadata.ResolvedLaunchTarget);
            Assert.Equal(["libraries"], metadata.GeneratedPaths.ToArray());
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath + ".bak"));
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
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
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "test",
                null,
                ImmutableArray.Create("../" + Path.GetFileName(outsidePath)),
                null,
                default));

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

    [Theory]
    [InlineData(InstanceConfig.FileName, "instance.generated_path.reserved")]
    [InlineData(InstanceConfig.FileName + ".bak", "instance.generated_path.reserved")]
    [InlineData(InstanceInstallMetadataStore.FileName, "instance.generated_path.reserved")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak", "instance.generated_path.reserved")]
    [InlineData(InstanceConfig.FileName + ".", "instance.generated_path.invalid")]
    [InlineData(InstanceConfig.FileName + " ", "instance.generated_path.invalid")]
    [InlineData(InstanceConfig.FileName + ".bak.", "instance.generated_path.invalid")]
    [InlineData(InstanceConfig.FileName + ".bak ", "instance.generated_path.invalid")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".", "instance.generated_path.invalid")]
    [InlineData(InstanceInstallMetadataStore.FileName + " ", "instance.generated_path.invalid")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak.", "instance.generated_path.invalid")]
    [InlineData(InstanceInstallMetadataStore.FileName + ".bak ", "instance.generated_path.invalid")]
    [InlineData("foo:bar", "instance.generated_path.invalid")]
    [InlineData("daemon_instance.json::$DATA", "instance.generated_path.invalid")]
    [InlineData("NUL", "instance.generated_path.invalid")]
    [InlineData("nul.txt", "instance.generated_path.invalid")]
    [InlineData("COM1", "instance.generated_path.invalid")]
    [InlineData("lpt9.log", "instance.generated_path.invalid")]
    public async Task UpdateSettings_ReservedGeneratedPath_IsRejectedBeforeStorageMutation(
        string generatedPath,
        string expectedCode)
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalTargetPath = Path.Combine(instanceDirectory, config.Target);
        var metadataPath = InstanceInstallMetadataStore.GetPath(instanceDirectory);
        var uploadedPath = Path.Combine(FileManager.UploadRoot, $"reserved-generated-{Guid.NewGuid():N}.jar");
        Directory.CreateDirectory(FileManager.UploadRoot);
        await File.WriteAllTextAsync(originalTargetPath, "original-core");
        await File.WriteAllTextAsync(uploadedPath, "replacement-core");
        InstanceInstallMetadataStore.Write(
            instanceDirectory,
            new InstanceInstallMetadata(
                "reserved-generated-test",
                null,
                ImmutableArray.Create(generatedPath),
                config.Target,
                DateTimeOffset.Parse("2026-07-01T00:00:00+00:00")));
        await File.WriteAllTextAsync(metadataPath + ".bak", "metadata-backup");
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalMetadataBytes = await File.ReadAllBytesAsync(metadataPath);
        var originalMetadataBackupBytes = await File.ReadAllBytesAsync(metadataPath + ".bak");

        try
        {
            var application = new LocalInstanceApplication(manager);
            var before = manager.InstanceSnapshotSource.Current;

            var result = await application.UpdateInstanceSettingsAsync(
                new UpdateInstanceSettingsRequest(
                    config.Uuid,
                    "reserved-generated",
                    InstanceType.MCBedrock,
                    config.JavaPath,
                    config.Arguments.ToImmutableArray(),
                    config.Version,
                    new InstanceCoreReplacementRequest(uploadedPath, "replacement.jar"),
                    true),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            var validation = Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal(expectedCode, validation.Code);
            Assert.Equal("original-core", await File.ReadAllTextAsync(originalTargetPath));
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalMetadataBytes, await File.ReadAllBytesAsync(metadataPath));
            Assert.Equal(originalMetadataBackupBytes, await File.ReadAllBytesAsync(metadataPath + ".bak"));
            Assert.Equal(config.Name, manager.Instances[config.Uuid].Config.Name);
            Assert.Equal(before.Version, manager.InstanceSnapshotSource.Current.Version);
            Assert.Same(before.Value, manager.InstanceSnapshotSource.Current.Value);
            Assert.Empty(Directory.EnumerateDirectories(instanceDirectory, ".instance-update-*"));
        }
        finally
        {
            if (File.Exists(uploadedPath))
                File.Delete(uploadedPath);
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
        var source = new AuthoritativeInstanceSnapshotSource([], new InstanceCatalogCommitFeed());
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

        var statusTask = Task.Run(() => instance.RaiseStatusChangedAsync(InstanceStatus.Running));
        Task<bool> removeTask;
        try
        {
            await instance.ConfigReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var removeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Keep the test's remove signal independent of a blocked ThreadPool status callback.
            removeTask = Task.Factory.StartNew(() =>
            {
                removeStarted.TrySetResult(true);
                return manager.TryRemoveInstance(config.Uuid).GetAwaiter().GetResult();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await removeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            instance.ReleaseConfigRead();
        }

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

    private static void DeleteFactoryWorkspace(string? workingDirectory)
    {
        if (workingDirectory is not null && Directory.Exists(workingDirectory))
            Directory.Delete(workingDirectory, true);
    }

    private static bool TryCreateDirectorySymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
        catch (IOException)
        {
            return TryCreateDirectoryJunction(link, target);
        }
    }

    private static bool TryCreateDirectoryJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 &&
                   Directory.Exists(link) &&
                   File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<bool> TryEnableDirectoryCaseSensitivityAsync(string directory)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("fsutil.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("file");
        startInfo.ArgumentList.Add("setCaseSensitiveInfo");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("enable");

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return false;

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(standardOutput, standardError);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<bool> TryCreateCaseDistinctFilesAsync(string directory)
    {
        const string script = """
            Set-Content -LiteralPath (Join-Path $env:MCSL_CASE_DIR 'Core.jar') -Value 'upper-original' -NoNewline
            Set-Content -LiteralPath (Join-Path $env:MCSL_CASE_DIR 'core.jar') -Value 'lower-original' -NoNewline
            """;
        try
        {
            var result = await RunWindowsPowerShellAsync(directory, script);
            if (result.ExitCode != 0)
                return false;

            var names = Directory.EnumerateFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .ToArray();
            return names.Contains("Core.jar", StringComparer.Ordinal) &&
                   names.Contains("core.jar", StringComparer.Ordinal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string[]> ReadCaseDistinctFilesAsync(string directory)
    {
        const string script = """
            $upper = Get-Content -Raw -LiteralPath (Join-Path $env:MCSL_CASE_DIR 'Core.jar')
            $lower = Get-Content -Raw -LiteralPath (Join-Path $env:MCSL_CASE_DIR 'core.jar')
            [Console]::Out.Write($upper)
            [Console]::Out.Write([char]0)
            [Console]::Out.Write($lower)
            """;
        var result = await RunWindowsPowerShellAsync(directory, script);
        if (result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Failed to read case-distinct files: {result.Error}");
        }

        return result.Output.Split('\0');
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWindowsPowerShellAsync(
        string directory,
        string script)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment["MCSL_CASE_DIR"] = directory;

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Windows PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static void WritePartialFactoryOutputs(string workingDirectory)
    {
        Directory.CreateDirectory(Path.Combine(workingDirectory, "libraries"));
        File.WriteAllText(Path.Combine(workingDirectory, "libraries", "partial.jar"), "partial");
        File.WriteAllText(Path.Combine(workingDirectory, "run.bat"), "partial");
        File.WriteAllText(Path.Combine(workingDirectory, "installer.log"), "partial-log");
        File.WriteAllText(Path.Combine(workingDirectory, "eula.txt"), "eula=true");
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

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class PartialOutputCancelingFactory : ICoreInstanceFactory
    {
        public static CancellationTokenSource? CancellationSource { get; set; }

        public static string? LastWorkingDirectory { get; set; }

        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            var workingDirectory = setting.Configuration.GetWorkingDirectory();
            LastWorkingDirectory = workingDirectory;
            WritePartialFactoryOutputs(workingDirectory);
            CancellationSource!.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ResultExt.Ok(setting.Configuration));
        }
    }

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class PartialOutputFailureFactory : ICoreInstanceFactory
    {
        public static string? LastWorkingDirectory { get; set; }

        public static bool ThrowException { get; set; }

        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workingDirectory = setting.Configuration.GetWorkingDirectory();
            LastWorkingDirectory = workingDirectory;
            WritePartialFactoryOutputs(workingDirectory);
            if (ThrowException)
                throw new InvalidOperationException("The test factory failed after writing partial output.");

            return Task.FromResult(ResultExt.Err<InstanceConfiguration>(new StorageDaemonError(
                "instance.factory.test_failed",
                "The test factory failed after writing partial output.")));
        }
    }

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class ReparsePointReplacementFactory : ICoreInstanceFactory
    {
        private const string LaunchTarget = "generated-launcher.jar";

        public static string? ExternalDirectory { get; set; }

        public static string? LastWorkingDirectory { get; set; }

        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workingDirectory = setting.Configuration.GetWorkingDirectory();
            LastWorkingDirectory = workingDirectory;
            File.WriteAllText(Path.Combine(workingDirectory, LaunchTarget), "generated-launcher");
            var externalDirectory = ExternalDirectory ??
                                    throw new InvalidOperationException("External directory was not configured.");
            if (!TryCreateDirectorySymbolicLink(
                    Path.Combine(workingDirectory, "linked-output"),
                    externalDirectory))
            {
                throw new InvalidOperationException("The test could not create a directory reparse point.");
            }
            return Task.FromResult(ResultExt.Ok(InstanceConfigurationMapper.WithTarget(
                setting.Configuration,
                LaunchTarget,
                TargetType.Jar)));
        }
    }

    [InstanceFactory(InstanceType.MCBedrock, SourceType.Core)]
    private sealed class SuccessfulReplacementFactory : ICoreInstanceFactory
    {
        public const string LaunchTarget = "generated-launcher.jar";

        public static string? LastWorkingDirectory { get; set; }

        public static bool CreateTypeCollision { get; set; }

        public Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
            InstanceFactoryConfiguration setting,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workingDirectory = setting.Configuration.GetWorkingDirectory();
            LastWorkingDirectory = workingDirectory;
            Directory.CreateDirectory(Path.Combine(workingDirectory, "libraries"));
            File.WriteAllText(Path.Combine(workingDirectory, "libraries", "factory.jar"), "factory-library");
            File.WriteAllText(Path.Combine(workingDirectory, LaunchTarget), "generated-launcher");
            File.WriteAllText(Path.Combine(workingDirectory, "server.properties"), "server-port=25570");
            File.WriteAllText(Path.Combine(workingDirectory, "installer.log"), "installer-log");
            File.WriteAllText(Path.Combine(workingDirectory, "eula.txt"), "eula=true");
            if (CreateTypeCollision)
                File.WriteAllText(Path.Combine(workingDirectory, "world"), "factory-world-file");
            return Task.FromResult(ResultExt.Ok(InstanceConfigurationMapper.WithTarget(
                setting.Configuration,
                LaunchTarget,
                TargetType.Jar)));
        }
    }

    private sealed class TestInstance(
        InstanceConfig config,
        Func<InstanceProcess?> process,
        InstanceStatus status = InstanceStatus.Stopped,
        Func<CancellationToken, Task<bool>>? startAsync = null,
        Action? stop = null,
        Action? dispose = null,
        Action? halt = null) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public InstanceProcess? Process => process();
        public InstanceStatus Status { get; set; } = status;
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

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new LegacyInstanceReport(Status, Config, [], [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default)
        {
            return startAsync?.Invoke(ct) ?? Task.FromResult(false);
        }

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            stop?.Invoke();
            return Task.FromResult(true);
        }

        public Task ForceKillAndClearAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            halt?.Invoke();
            _ = process();
            return Task.CompletedTask;
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
        public event Func<Guid, string, CancellationToken, Task>? OnLog;
        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged;

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

        public Task RaiseLogAsync(string log)
        {
            return OnLog?.Invoke(Config.Uuid, log, CancellationToken.None) ?? Task.CompletedTask;
        }

        public Task RaiseStatusAsync(InstanceStatus status)
        {
            return OnStatusChanged?.Invoke(Config.Uuid, status, CancellationToken.None) ?? Task.CompletedTask;
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

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => Task.CompletedTask;

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
        private Func<Guid, InstanceStatus, CancellationToken, Task>? _statusChanged;
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

        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }

        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add => _statusChanged += value;
            remove => _statusChanged -= value;
        }

        public void BlockNextConfigRead()
        {
            Volatile.Write(ref _blockConfigRead, 1);
        }

        public Task RaiseStatusChangedAsync(InstanceStatus status)
        {
            return _statusChanged?.Invoke(config.Uuid, status, CancellationToken.None) ?? Task.CompletedTask;
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

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => Task.CompletedTask;

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

        public Task<LegacyInstanceReport> GetReportAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> StopAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }
}
