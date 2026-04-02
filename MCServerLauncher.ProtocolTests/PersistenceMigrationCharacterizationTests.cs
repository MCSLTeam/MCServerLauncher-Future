using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Persistence;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

public class PersistenceMigrationCharacterizationTests
{
    private static readonly Type FileManagerType =
        Type.GetType("MCServerLauncher.Daemon.Storage.FileManager, MCServerLauncher.Daemon", throwOnError: true)!;

    private static readonly Type AppConfigType =
        Type.GetType("MCServerLauncher.Daemon.AppConfig, MCServerLauncher.Daemon", throwOnError: true)!;

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_ConfigFixture_DeserializeAndReserialize_MatchesCurrentJsonContract()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.ConfigDir, "valid-config.json");
        var json = File.ReadAllText(fixturePath);

        var deserialized = JsonSerializer.Deserialize(json, AppConfigType, DaemonPersistenceJsonBoundary.StjOptions);
        Assert.NotNull(deserialized);

        var reserialized = JsonSerializer.Serialize(deserialized, AppConfigType,
            DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);

        FixtureHarness.AssertStructuralEquals(
            FixtureHarness.LoadFixture(fixturePath),
            FixtureHarness.ParseJson(reserialized),
            "config.json round-trip should match frozen baseline");
    }

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_InstanceConfigFixture_DeserializeAndReserialize_MatchesCurrentJsonContract()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.InstanceConfigDir, "representative-daemon-instance.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var parsed = ReadJsonWithFileManager(fixture.GetRawText());
        Assert.NotNull(parsed);

        var canonical = SerializeWithDaemonSettings(parsed!);
        FixtureHarness.AssertStructuralEquals(fixture, FixtureHarness.ParseJson(canonical),
            "daemon_instance.json baseline should remain stable");
    }

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_EventRuleHeavyInstanceConfigFixture_DeserializeAndReserialize_MatchesCurrentJsonContract()
    {
        var fixturePath = Path.Combine(PersistenceFixturePaths.InstanceConfigDir, "event-rule-heavy-daemon-instance.json");
        var fixture = FixtureHarness.LoadFixture(fixturePath);

        var parsed = ReadJsonWithFileManager(fixture.GetRawText());
        Assert.NotNull(parsed);
        Assert.NotEmpty(parsed!.EventRules);
        Assert.All(parsed.EventRules, rule =>
        {
            Assert.NotEmpty(rule.Triggers);
            Assert.NotEmpty(rule.Rulesets);
            Assert.NotEmpty(rule.Actions);
        });

        var canonical = SerializeWithDaemonSettings(parsed);
        FixtureHarness.AssertStructuralEquals(fixture, FixtureHarness.ParseJson(canonical),
            "event rules are persisted inside daemon_instance.json");
    }

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_ReadJson_InvalidJson_ThrowsJsonException()
    {
        var path = CreateTempFile("{\"name\":");
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => InvokeReadJsonMethod<InstanceConfig>(path));
            var root = UnwrapInvocationException(ex);
            Assert.Contains("Json", root.GetType().Name, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_ReadJsonOr_MissingFile_WritesDefaultAndReturnsIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"readjsonor-missing-{Guid.NewGuid():N}.json");
        var defaultValue = new InstanceConfig
        {
            Name = "missing-default",
            Target = "server.jar",
            InstanceType = InstanceType.MCVanilla,
            TargetType = TargetType.Jar,
            EventRules =
            [
                new EventRule
                {
                    Name = "persisted-rule"
                }
            ]
        };

        try
        {
            var loaded = InvokeReadJsonOrMethod(path, () => defaultValue);

            Assert.Equal(defaultValue.Name, loaded.Name);
            Assert.Equal(defaultValue.Target, loaded.Target);
            Assert.True(File.Exists(path));

            var written = FixtureHarness.ParseJson(File.ReadAllText(path));
            var expected = FixtureHarness.ParseJson(SerializeWithDaemonSettings(defaultValue));
            FixtureHarness.AssertStructuralEquals(expected, written,
                "ReadJsonOr should persist default content when file is missing");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    [Trait("Category", "PersistenceGolden")]
    public void PersistenceGolden_ReadJsonOr_InvalidExistingFile_BubblesJsonFailureAndDoesNotOverwrite()
    {
        var path = CreateTempFile("{\"name\":");
        var before = File.ReadAllText(path);

        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => InvokeReadJsonOrMethod(path, () => new InstanceConfig
            {
                Name = "should-not-overwrite",
                Target = "server.jar",
                InstanceType = InstanceType.MCVanilla,
                TargetType = TargetType.Jar
            }));

            var root = UnwrapInvocationException(ex);
            Assert.Contains("Json", root.GetType().Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "BackupBehavior")]
    public void BackupBehavior_WriteJsonAndBackup_ValidExistingFile_CreatesBakThenWritesNewContent()
    {
        var existing = FixtureHarness.LoadFixture(PersistenceFixturePaths.InstanceConfigDir, "representative-daemon-instance.json");
        var updated = BuildUpdatedInstanceConfig();

        var path = CreateTempFile(existing.GetRawText());
        var bakPath = path + ".bak";

        try
        {
            InvokeWriteJsonAndBackup(path, updated);

            Assert.True(File.Exists(bakPath));

            var backupJson = FixtureHarness.ParseJson(File.ReadAllText(bakPath));
            FixtureHarness.AssertStructuralEquals(existing, backupJson,
                "valid existing file should be backed up before write");

            var expectedUpdated = FixtureHarness.ParseJson(SerializeWithDaemonSettings(updated));
            var actualUpdated = FixtureHarness.ParseJson(File.ReadAllText(path));
            FixtureHarness.AssertStructuralEquals(expectedUpdated, actualUpdated,
                "write should persist new content after backup");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "BackupBehavior")]
    public void BackupBehavior_WriteJsonAndBackup_InvalidExistingFile_WritesNewContentWithoutBak()
    {
        var updated = BuildUpdatedInstanceConfig();
        var path = CreateTempFile("{\"name\":");
        var bakPath = path + ".bak";

        try
        {
            InvokeWriteJsonAndBackup(path, updated);

            Assert.False(File.Exists(bakPath));

            var expectedUpdated = FixtureHarness.ParseJson(SerializeWithDaemonSettings(updated));
            var actualUpdated = FixtureHarness.ParseJson(File.ReadAllText(path));
            FixtureHarness.AssertStructuralEquals(expectedUpdated, actualUpdated,
                "invalid existing content should skip backup but still write new content");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "BackupBehavior")]
    public void BackupBehavior_AppConfigTrySave_DirectWritePath_DoesNotCreateBak()
    {
        var config = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(PersistenceFixturePaths.ConfigDir, "valid-config.json")),
            AppConfigType,
            DaemonPersistenceJsonBoundary.StjOptions);

        Assert.NotNull(config);

        var path = Path.Combine(Path.GetTempPath(), $"appconfig-save-{Guid.NewGuid():N}.json");
        var bakPath = path + ".bak";
        File.WriteAllText(path, "invalid-existing-content");

        try
        {
            var trySave = AppConfigType.GetMethod("TrySave", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(trySave);

            var saved = trySave!.Invoke(config, [path]);
            Assert.True(saved is true);

            Assert.False(File.Exists(bakPath));
            var parsed = FixtureHarness.ParseJson(File.ReadAllText(path));
            Assert.Equal(11452, parsed.GetProperty("port").GetInt32());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }
        }
    }

    private static T? InvokeReadJsonMethod<T>(string path)
    {
        var method = FileManagerType.GetMethod("ReadJson", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));
        return (T?)method.Invoke(null, [path]);
    }

    private static T InvokeReadJsonOrMethod<T>(string path, Func<T> defaultFactory)
    {
        var method = FileManagerType.GetMethod("ReadJsonOr", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));

        return (T)method.Invoke(null, [path, defaultFactory])!;
    }

    private static InstanceConfig ReadJsonWithFileManager(string content)
    {
        var path = CreateTempFile(content);
        try
        {
            return InvokeReadJsonMethod<InstanceConfig>(path)!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void InvokeWriteJsonAndBackup<T>(string path, T value)
    {
        var method = FileManagerType.GetMethod("WriteJsonAndBackup", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T));
        method.Invoke(null, [path, value]);
    }

    private static string SerializeWithDaemonSettings<T>(T value)
    {
        return JsonSerializer.Serialize(value, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions);
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcsl-persistence-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static Exception UnwrapInvocationException(Exception ex)
    {
        if (ex is TargetInvocationException tie && tie.InnerException is not null)
        {
            return tie.InnerException;
        }

        return ex;
    }

    private static InstanceConfig BuildUpdatedInstanceConfig()
    {
        return new InstanceConfig
        {
            Name = "updated-instance",
            Target = "updated-server.jar",
            InstanceType = InstanceType.MCFabric,
            TargetType = TargetType.Jar,
            McVersion = "1.21.1",
            Arguments = ["nogui"],
            Env = new Dictionary<string, PlaceHolderString>
            {
                ["JAVA_HOME"] = new("{JAVA_HOME}")
            },
            EventRules =
            [
                new EventRule
                {
                    Name = "updated-rule",
                    TriggerCondition = "All",
                    Triggers =
                    [
                        new ConsoleOutputTrigger
                        {
                            Pattern = "Done",
                            IsRegex = false
                        }
                    ],
                    Rulesets =
                    [
                        new AlwaysTrueRuleset()
                    ],
                    Actions =
                    [
                        new SendCommandAction
                        {
                            Command = "say migrated"
                        }
                    ]
                }
            ]
        };
    }
}
