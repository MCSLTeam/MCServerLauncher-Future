using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.ProtocolTests;

public sealed class LocalEventRuleApplicationTests
{
    [Fact]
    public async Task UpdateEventRules_PersistsStagedRulesBeforeCompleting()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager(CreateRule("original"));
        try
        {
            var application = CreateApplication(manager);
            var replacementRule = CreateRule("replacement");

            var result = await application.UpdateEventRulesAsync(
                CreateUpdateRequest(config.Uuid, replacementRule),
                CancellationToken.None);

            Assert.True(result.IsOk(out _));
            var persisted = FileManager.ReadJson<InstanceConfig>(Path.Combine(instanceDirectory, InstanceConfig.FileName));
            Assert.NotNull(persisted);
            var persistedRule = Assert.Single(persisted.EventRules);
            var liveRule = Assert.Single(manager.Instances[config.Uuid].Config.EventRules);
            Assert.Equal(replacementRule.Name, persistedRule.Name);
            Assert.Equal(persistedRule.Name, liveRule.Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateEventRules_PersistenceFailure_LeavesLiveRulesUnchanged()
    {
        var originalRule = CreateRule("original");
        var (manager, config, instanceDirectory) = CreateLoadedManager(originalRule);
        try
        {
            var application = CreateApplication(manager);
            var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
            File.Delete(configPath);
            Directory.CreateDirectory(configPath);

            var result = await application.UpdateEventRulesAsync(
                CreateUpdateRequest(config.Uuid, CreateRule("replacement")),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<StorageDaemonError>(error);
            var liveRules = manager.Instances[config.Uuid].Config.EventRules;
            var liveRule = Assert.Single(liveRules);
            Assert.Equal(originalRule.Name, liveRule.Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateEventRules_MidWriteFailure_PreservesPrimaryAndBackupBytes()
    {
        var originalRule = CreateRule("original");
        var (manager, config, instanceDirectory) = CreateLoadedManager(originalRule);
        var configPath = Path.Combine(instanceDirectory, InstanceConfig.FileName);
        var originalConfigBytes = await File.ReadAllBytesAsync(configPath);
        var originalBackupBytes = "original-event-rules-backup"u8.ToArray();
        await File.WriteAllBytesAsync(configPath + ".bak", originalBackupBytes);

        try
        {
            var application = CreateApplication(manager, static (path, value) =>
                FileManager.WriteJsonAndBackupWithTemporaryFileWriterForTests(
                    path,
                    value,
                    static (temporaryPath, serialized) =>
                    {
                        File.WriteAllText(temporaryPath, serialized[..Math.Min(8, serialized.Length)]);
                        throw new IOException("injected temporary write failure");
                    }));

            var result = await application.UpdateEventRulesAsync(
                CreateUpdateRequest(config.Uuid, CreateRule("replacement")),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.IsType<StorageDaemonError>(error);
            Assert.Equal(originalConfigBytes, await File.ReadAllBytesAsync(configPath));
            Assert.Equal(originalBackupBytes, await File.ReadAllBytesAsync(configPath + ".bak"));
            Assert.Equal(originalRule.Name, Assert.Single(manager.Instances[config.Uuid].Config.EventRules).Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task GetInstanceSettings_ReturnsRuleSnapshotIndependentOfLaterUpdate()
    {
        var originalRule = CreateRule("original");
        var (manager, config, instanceDirectory) = CreateLoadedManager(originalRule);
        try
        {
            var eventRules = CreateApplication(manager);
            var settings = await manager.GetInstanceSettings(config.Uuid, CancellationToken.None);
            Assert.True(settings.IsOk(out var settingsValue));

            var update = await eventRules.UpdateEventRulesAsync(
                CreateUpdateRequest(config.Uuid, CreateRule("replacement")),
                CancellationToken.None);

            Assert.True(update.IsOk(out _));
            Assert.Equal("original", Assert.Single(settingsValue.Config.EventRules).Name);
            Assert.Equal("replacement", Assert.Single(manager.Instances[config.Uuid].Config.EventRules).Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    [Fact]
    public async Task UpdateEventRules_ConcurrentSettingsUpdate_PreservesBothCommittedChanges()
    {
        var (manager, config, instanceDirectory) = CreateLoadedManager();
        try
        {
            var eventRules = CreateApplication(manager);
            var instances = new LocalInstanceApplication(manager);
            var updatedRule = CreateRule("concurrent-rule");
            var eventUpdate = CreateUpdateRequest(config.Uuid, updatedRule);
            var settingsUpdate = new UpdateInstanceSettingsRequest(
                config.Uuid,
                "concurrent-settings",
                config.InstanceType,
                config.JavaPath,
                config.Arguments.ToImmutableArray(),
                "concurrent-version",
                null,
                false);

            var eventTask = Task.Run(() => eventRules.UpdateEventRulesAsync(eventUpdate, CancellationToken.None));
            var settingsTask = Task.Run(() => instances.UpdateInstanceSettingsAsync(settingsUpdate, CancellationToken.None));
            var eventResult = await eventTask.WaitAsync(TimeSpan.FromSeconds(3));
            var settingsResult = await settingsTask.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(eventResult.IsOk(out _));
            Assert.True(settingsResult.IsOk(out _));

            var persisted = FileManager.ReadJson<InstanceConfig>(Path.Combine(instanceDirectory, InstanceConfig.FileName));
            Assert.NotNull(persisted);
            var runtime = manager.Instances[config.Uuid].Config;

            Assert.Equal("concurrent-settings", persisted.Name);
            Assert.Equal("concurrent-version", persisted.Version);
            Assert.Equal(persisted.Name, runtime.Name);
            Assert.Equal(persisted.Version, runtime.Version);
            Assert.Single(persisted.EventRules);
            Assert.Single(runtime.EventRules);
            Assert.Equal(updatedRule.Name, persisted.EventRules[0].Name);
            Assert.Equal(updatedRule.Name, runtime.EventRules[0].Name);
        }
        finally
        {
            DeleteInstanceDirectory(instanceDirectory);
        }
    }

    private static LocalEventRuleApplication CreateApplication(
        InstanceManager manager,
        Action<string, InstanceConfig>? writeConfig = null)
    {
        return new LocalEventRuleApplication(manager, NullLogger<LocalEventRuleApplication>.Instance, writeConfig);
    }

    private static EventRuleUpdateRequest CreateUpdateRequest(Guid instanceId, params EventRule[] rules)
    {
        return new EventRuleUpdateRequest(
            instanceId,
            JsonSerializer.SerializeToElement(new List<EventRule>(rules), EventRuleJsonContext.Default.EventRuleList));
    }

    private static EventRule CreateRule(string name)
    {
        return new EventRule
        {
            Name = name
        };
    }

    private static (InstanceManager Manager, InstanceConfig Config, string InstanceDirectory) CreateLoadedManager(
        EventRule? initialRule = null)
    {
        var config = new InstanceConfig
        {
            Uuid = Guid.NewGuid(),
            Name = "event-rule-application-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            Version = "1.20.1",
            JavaPath = "java",
            Arguments = ["nogui"]
        };
        if (initialRule is not null)
            config.EventRules.Add(initialRule);

        var instanceDirectory = config.GetWorkingDirectory();
        Directory.CreateDirectory(instanceDirectory);
        FileManager.WriteJsonAndBackup(Path.Combine(instanceDirectory, InstanceConfig.FileName), config);

        return (Assert.IsType<InstanceManager>(InstanceManager.Create([instanceDirectory])), config, instanceDirectory);
    }

    private static void DeleteInstanceDirectory(string instanceDirectory)
    {
        if (Directory.Exists(instanceDirectory))
            Directory.Delete(instanceDirectory, true);
    }
}
