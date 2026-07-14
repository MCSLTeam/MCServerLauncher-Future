using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using RuntimeInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

public sealed class InstanceManagerCreateTransactionTests
{
    [Fact]
    public async Task TryAddInstance_ConcurrentSameRequestedUuid_ReservesDistinctFactoryDirectories()
    {
        var requestedId = Guid.NewGuid();
        var firstFactoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedSettings = new ConcurrentBag<InstanceFactoryConfiguration>();
        var factoryInvocations = 0;
        var manager = CreateManager(async setting =>
        {
            observedSettings.Add(setting);
            await WriteFactoryMarkerAsync(setting);

            if (Interlocked.Increment(ref factoryInvocations) == 1)
            {
                firstFactoryEntered.TrySetResult(true);
                await releaseFirstFactory.Task;
            }

            return ResultExt.Ok(setting.Configuration);
        });

        try
        {
            var firstCreate = manager.TryAddInstance(CreateSetting(requestedId, "first"));
            await firstFactoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var secondCreate = manager.TryAddInstance(CreateSetting(requestedId, "second"));
            var secondResult = await secondCreate.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(secondResult.IsOk(out var secondConfig));

            releaseFirstFactory.TrySetResult(true);
            var firstResult = await firstCreate.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(firstResult.IsOk(out var firstConfig));

            Assert.NotEqual(firstConfig.InstanceId, secondConfig.InstanceId);
            Assert.Contains(observedSettings, setting => setting.Configuration.InstanceId == firstConfig.InstanceId);
            Assert.Contains(observedSettings, setting => setting.Configuration.InstanceId == secondConfig.InstanceId);
            Assert.All([firstConfig, secondConfig], config =>
            {
                var workingDirectory = config.GetWorkingDirectory();
                Assert.True(Directory.Exists(workingDirectory));
                Assert.True(File.Exists(Path.Combine(workingDirectory, "factory-marker.txt")));
                Assert.Equal(config.InstanceId, manager.Instances[config.InstanceId].Config.Uuid);
                var persisted = FileManager.ReadJson<InstanceConfig>(
                    Path.Combine(workingDirectory, InstanceConfig.FileName));
                Assert.NotNull(persisted);
                Assert.Equal(config.InstanceId, persisted.Uuid);
            });
        }
        finally
        {
            releaseFirstFactory.TrySetResult(true);
            RemoveDirectories(requestedId, observedSettings.Select(setting => setting.Configuration.InstanceId));
        }
    }

    [Fact]
    public async Task TryAddInstance_ExistingRequestedUuid_AllocatesBeforeFactoryAndPersistsAtAllocatedPath()
    {
        var requestedId = Guid.NewGuid();
        var existingConfig = CreateConfig(requestedId, "existing");
        var existingDirectory = existingConfig.GetWorkingDirectory();
        Directory.CreateDirectory(existingDirectory);
        FileManager.WriteJsonAndBackup(Path.Combine(existingDirectory, InstanceConfig.FileName), existingConfig);
        await File.WriteAllTextAsync(Path.Combine(existingDirectory, "existing-marker.txt"), "existing");

        InstanceFactoryConfiguration? factorySetting = null;
        var manager = CreateManager(async setting =>
        {
            factorySetting = setting;
            await WriteFactoryMarkerAsync(setting);
            return ResultExt.Ok(setting.Configuration);
        });
        manager.ReplaceInstance(requestedId, new TransactionTestInstance(existingConfig));

        try
        {
            var result = await manager.TryAddInstance(CreateSetting(requestedId, "replacement-request"));

            Assert.True(result.IsOk(out var createdConfig));
            Assert.NotEqual(requestedId, createdConfig.InstanceId);
            Assert.NotNull(factorySetting);
            Assert.Equal(createdConfig.InstanceId, factorySetting.Configuration.InstanceId);

            var createdDirectory = createdConfig.GetWorkingDirectory();
            Assert.True(Directory.Exists(createdDirectory));
            Assert.True(File.Exists(Path.Combine(createdDirectory, "factory-marker.txt")));
            var persisted = FileManager.ReadJson<InstanceConfig>(Path.Combine(createdDirectory, InstanceConfig.FileName));
            Assert.NotNull(persisted);
            Assert.Equal(createdConfig.InstanceId, persisted.Uuid);
            Assert.Equal(createdConfig.InstanceId, manager.Instances[createdConfig.InstanceId].Config.Uuid);

            Assert.True(File.Exists(Path.Combine(existingDirectory, "existing-marker.txt")));
            var existingPersisted = FileManager.ReadJson<InstanceConfig>(
                Path.Combine(existingDirectory, InstanceConfig.FileName));
            Assert.NotNull(existingPersisted);
            Assert.Equal(requestedId, existingPersisted.Uuid);
        }
        finally
        {
            RemoveDirectories(
                requestedId,
                factorySetting is null ? [] : [factorySetting.Configuration.InstanceId]);
        }
    }

    [Fact]
    public async Task TryAddInstance_CanceledCreate_CleansOnlyItsReservedDirectory()
    {
        var requestedId = Guid.NewGuid();
        var firstFactoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedSettings = new ConcurrentBag<InstanceFactoryConfiguration>();
        var factoryInvocations = 0;
        var manager = CreateManager(async setting =>
        {
            observedSettings.Add(setting);
            await WriteFactoryMarkerAsync(setting);

            if (Interlocked.Increment(ref factoryInvocations) == 1)
            {
                firstFactoryEntered.TrySetResult(true);
                await releaseFirstFactory.Task;
            }

            return ResultExt.Ok(setting.Configuration);
        });
        using var cancellationSource = new CancellationTokenSource();

        try
        {
            var canceledCreate = manager.TryAddInstance(CreateSetting(requestedId, "canceled"), cancellationSource.Token);
            await firstFactoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var survivingResult = await manager
                .TryAddInstance(CreateSetting(requestedId, "surviving"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(survivingResult.IsOk(out var survivingConfig));

            cancellationSource.Cancel();
            releaseFirstFactory.TrySetResult(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => canceledCreate.WaitAsync(TimeSpan.FromSeconds(3)));

            Assert.True(Directory.Exists(survivingConfig.GetWorkingDirectory()));
            Assert.True(File.Exists(Path.Combine(survivingConfig.GetWorkingDirectory(), InstanceConfig.FileName)));
            Assert.True(manager.Instances.ContainsKey(survivingConfig.InstanceId));
            Assert.False(manager.Instances.ContainsKey(requestedId));
            Assert.False(Directory.Exists(Path.Combine(FileManager.InstancesRoot, requestedId.ToString())));
        }
        finally
        {
            releaseFirstFactory.TrySetResult(true);
            RemoveDirectories(requestedId, observedSettings.Select(setting => setting.Configuration.InstanceId));
        }
    }

    [Fact]
    public async Task TryAddInstance_FailedCreate_CleansOnlyItsReservedDirectory()
    {
        var requestedId = Guid.NewGuid();
        var firstFactoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedSettings = new ConcurrentBag<InstanceFactoryConfiguration>();
        var factoryInvocations = 0;
        var manager = CreateManager(async setting =>
        {
            observedSettings.Add(setting);
            await WriteFactoryMarkerAsync(setting);

            if (Interlocked.Increment(ref factoryInvocations) == 1)
            {
                firstFactoryEntered.TrySetResult(true);
                await releaseFirstFactory.Task;
                return ResultExt.Err<InstanceConfiguration>("expected factory failure");
            }

            return ResultExt.Ok(setting.Configuration);
        });

        try
        {
            var failedCreate = manager.TryAddInstance(CreateSetting(requestedId, "failed"));
            await firstFactoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var survivingResult = await manager
                .TryAddInstance(CreateSetting(requestedId, "surviving"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(survivingResult.IsOk(out var survivingConfig));

            releaseFirstFactory.TrySetResult(true);
            var failedResult = await failedCreate.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(failedResult.IsErr(out _));

            Assert.True(Directory.Exists(survivingConfig.GetWorkingDirectory()));
            Assert.True(File.Exists(Path.Combine(survivingConfig.GetWorkingDirectory(), InstanceConfig.FileName)));
            Assert.True(manager.Instances.ContainsKey(survivingConfig.InstanceId));
            Assert.False(manager.Instances.ContainsKey(requestedId));
            Assert.False(Directory.Exists(Path.Combine(FileManager.InstancesRoot, requestedId.ToString())));
        }
        finally
        {
            releaseFirstFactory.TrySetResult(true);
            RemoveDirectories(requestedId, observedSettings.Select(setting => setting.Configuration.InstanceId));
        }
    }

    [Fact]
    public async Task TryAddInstance_OrphanedRootFromPriorFailedCleanup_AllocatesNewUuid()
    {
        var requestedId = Guid.NewGuid();
        var orphanDirectory = Path.Combine(FileManager.InstancesRoot, requestedId.ToString());
        Directory.CreateDirectory(orphanDirectory);
        await File.WriteAllTextAsync(Path.Combine(orphanDirectory, "orphan-marker.txt"), "preserve-me");

        InstanceFactoryConfiguration? observedSetting = null;
        var manager = CreateManager(async setting =>
        {
            observedSetting = setting;
            await WriteFactoryMarkerAsync(setting);
            return ResultExt.Ok(setting.Configuration);
        });

        try
        {
            var result = await manager.TryAddInstance(CreateSetting(requestedId, "fresh"));

            Assert.True(result.IsOk(out var createdConfig));
            Assert.NotEqual(requestedId, createdConfig.InstanceId);
            Assert.NotNull(observedSetting);
            Assert.Equal(createdConfig.InstanceId, observedSetting.Configuration.InstanceId);
            Assert.Equal("preserve-me", await File.ReadAllTextAsync(Path.Combine(orphanDirectory, "orphan-marker.txt")));
            Assert.True(File.Exists(Path.Combine(createdConfig.GetWorkingDirectory(), InstanceConfig.FileName)));
        }
        finally
        {
            RemoveDirectories(requestedId, observedSetting is null ? [] : [observedSetting.Configuration.InstanceId]);
        }
    }

    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("nested/outside.jar")]
    [InlineData("C:\\outside.jar")]
    public async Task TryAddInstance_UnsafeTarget_IsRejectedBeforeFactoryOrDirectoryCreation(string target)
    {
        var requestedId = Guid.NewGuid();
        var factoryCalled = false;
        var manager = CreateManager(setting =>
        {
            factoryCalled = true;
            return Task.FromResult(ResultExt.Ok(setting.Configuration));
        });

        var result = await manager.TryAddInstance(CreateSetting(requestedId, "unsafe", target));

        Assert.True(result.IsErr(out _));
        Assert.False(factoryCalled);
        Assert.False(Directory.Exists(Path.Combine(FileManager.InstancesRoot, requestedId.ToString())));
        Assert.False(manager.Instances.ContainsKey(requestedId));
    }

    private static InstanceManager CreateManager(
        Func<InstanceFactoryConfiguration, Task<Result<InstanceConfiguration, Error>>> applyInstanceFactory)
    {
        return new InstanceManager(
            config => new TransactionTestInstance(config),
            applyInstanceFactory);
    }

    private static InstanceFactoryConfiguration CreateSetting(Guid uuid, string name, string target = "server.exe")
    {
        return new InstanceFactoryConfiguration(
            new InstanceConfiguration(
                uuid,
                name,
                target,
                InstanceType.Universal,
                TargetType.Executable,
                string.Empty,
                "utf-8",
                "utf-8",
                string.Empty,
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                JsonSerializer.SerializeToElement(Array.Empty<object>())),
            "transaction-test-source",
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);
    }

    private static InstanceConfig CreateConfig(Guid uuid, string name)
    {
        return new InstanceConfig
        {
            Uuid = uuid,
            Name = name,
            Target = "server.exe",
            TargetType = TargetType.Executable,
            InstanceType = InstanceType.Universal
        };
    }

    private static async Task WriteFactoryMarkerAsync(InstanceFactoryConfiguration setting)
    {
        await File.WriteAllTextAsync(
            Path.Combine(setting.Configuration.GetWorkingDirectory(), "factory-marker.txt"),
            setting.Configuration.Name);
    }

    private static void RemoveDirectories(Guid requestedId, IEnumerable<Guid> ids)
    {
        var directories = ids
            .Append(requestedId)
            .Distinct()
            .Select(id => Path.Combine(FileManager.InstancesRoot, id.ToString()));

        foreach (var directory in directories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private sealed class TransactionTestInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;

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

        public Task<RuntimeInstanceReport> GetReportAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new RuntimeInstanceReport(Status, Config, [], [], default));
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
