using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using ContractDriveInfo = MCServerLauncher.Common.Contracts.System.DriveInfo;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class ApplicationDtoJsonMetadataTests
{
    [Fact]
    public void EveryApplicationDtoHasProductionSourceGeneratedMetadata()
    {
        var context = ApplicationContractJsonContext.Default;
        var contractTypes = typeof(CreateInstanceRequest).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("MCServerLauncher.Common.Contracts", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace?.StartsWith("MCServerLauncher.Common.Contracts.Protocol", StringComparison.Ordinal) != true)
            .Where(type => !typeof(JsonSerializerContext).IsAssignableFrom(type));

        Assert.All(contractTypes, type => Assert.NotNull(context.GetTypeInfo(type)));
    }

    [Fact]
    public void EveryApplicationDtoRoundTripsThroughProductionMetadata()
    {
        foreach (var sample in CreateSamples())
        {
            var typeInfo = ApplicationContractJsonContext.Default.GetTypeInfo(sample.GetType());
            Assert.NotNull(typeInfo);

            var json = JsonSerializer.Serialize(sample, typeInfo);
            var roundTripped = JsonSerializer.Deserialize(json, typeInfo);

            Assert.NotNull(roundTripped);
            Assert.IsType(sample.GetType(), roundTripped);
            Assert.Equal(json, JsonSerializer.Serialize(roundTripped, typeInfo));
        }
    }

    [Fact]
    public void ProductionMetadataUsesSnakeCaseStringEnums()
    {
        var samples = CreateSamples();
        var configuration = Assert.IsType<InstanceConfiguration>(
            samples.Single(sample => sample.GetType() == typeof(InstanceConfiguration)));
        var factory = Assert.IsType<InstanceFactoryConfiguration>(
            samples.Single(sample => sample.GetType() == typeof(InstanceFactoryConfiguration)));
        var report = Assert.IsType<ContractInstanceReport>(
            samples.Single(sample => sample.GetType() == typeof(ContractInstanceReport)));

        var configurationJson = JsonSerializer.Serialize(
            configuration,
            ApplicationContractJsonContext.Default.InstanceConfiguration);
        var factoryJson = JsonSerializer.Serialize(
            factory,
            ApplicationContractJsonContext.Default.InstanceFactoryConfiguration);
        var reportJson = JsonSerializer.Serialize(
            report,
            ApplicationContractJsonContext.Default.InstanceReport);

        Assert.Contains("\"instance_type\":\"mc_java\"", configurationJson, StringComparison.Ordinal);
        Assert.Contains("\"target_type\":\"jar\"", configurationJson, StringComparison.Ordinal);
        Assert.Contains("\"source_type\":\"core\"", factoryJson, StringComparison.Ordinal);
        Assert.Contains("\"mirror\":\"none\"", factoryJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"running\"", reportJson, StringComparison.Ordinal);
    }

    private static IReadOnlyList<object> CreateSamples()
    {
        var instanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var rulesDocument = JsonDocument.Parse("[{\"id\":\"33333333-3333-3333-3333-333333333333\"}]");
        var rules = rulesDocument.RootElement;

        var configuration = new InstanceConfiguration(
            instanceId,
            "Example",
            "server.jar",
            InstanceType.MCJava,
            TargetType.Jar,
            "1.21.5",
            "utf-8",
            "utf-8",
            "C:/Java/bin/java.exe",
            ["-Xmx4G"],
            ImmutableDictionary<string, string>.Empty.Add("JAVA_HOME", "{JAVA_HOME}"),
            rules);
        var factory = new InstanceFactoryConfiguration(
            configuration,
            "uploads/server.jar",
            SourceType.Core,
            InstanceFactoryMirror.None,
            false);
        var installMetadata = new InstanceInstallMetadata(
            "MCJava",
            "uploads/server.jar",
            ["server.jar"],
            "server.jar",
            DateTimeOffset.Parse("2026-07-11T00:00:00+00:00"));
        var report = new ContractInstanceReport(
            InstanceStatus.Running,
            configuration,
            ImmutableDictionary<string, string>.Empty.Add("motd", "hello"),
            [new InstancePlayer("Alex", Guid.Parse("44444444-4444-4444-4444-444444444444"))],
            new InstancePerformance(12.5, 1024),
            1234);
        var fileMetadata = new FileMetadata(
            DateTimeOffset.UnixEpoch,
            false,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            true,
            42);
        var directoryMetadata = new DirectoryMetadata(
            DateTimeOffset.UnixEpoch,
            false,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var drive = new ContractDriveInfo("NTFS", 1024, 512, "C:\\");
        var systemInfo = new SystemInfo(
            new OperatingSystemInfo("Windows", "x64"),
            new ProcessorInfo("GenuineIntel", "CPU", 16, 5.5, 8, 16),
            new MemoryInfo(32768, 16384),
            drive,
            [drive],
            "2.0.0");

        return
        [
            new EventRuleQuery(instanceId),
            new EventRuleSet(instanceId, rules),
            new EventRuleUpdateRequest(instanceId, rules),
            new PathRequest("instances/example"),
            new PathRenameRequest("instances/example/a.txt", "b.txt"),
            new PathTransferRequest("instances/example/a.txt", "instances/example/b.txt"),
            new DeleteDirectoryRequest("instances/example/cache", true),
            new FileSystemMetadata(DateTimeOffset.UnixEpoch, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            fileMetadata,
            directoryMetadata,
            new FileEntry("server.jar", fileMetadata),
            new DirectoryEntry("plugins", directoryMetadata),
            new FileDetails(fileMetadata),
            new DirectoryDetails(
                "instances",
                [new FileEntry("server.jar", fileMetadata)],
                [new DirectoryEntry("plugins", directoryMetadata)]),
            new UploadOpenRequest("instances/example/server.jar", 42, "sha256"),
            new UploadSession(sessionId, 65536, DateTimeOffset.Parse("2026-07-11T01:00:00+00:00")),
            new UploadChunkRequest(sessionId, 0, ImmutableArray.Create<byte>(1, 2, 3)),
            new DownloadOpenRequest("instances/example/server.jar"),
            new DownloadSession(sessionId, 42, "sha256", 65536, DateTimeOffset.Parse("2026-07-11T01:00:00+00:00")),
            new DownloadChunkRequest(sessionId, 0, 1024),
            new DownloadChunk(0, ImmutableArray.Create<byte>(1, 2, 3), true),
            configuration,
            factory,
            new CreateInstanceRequest(factory),
            new CreateInstanceResult(configuration),
            new InstanceReference(instanceId),
            new InstanceCommandRequest(instanceId, "say hello"),
            new InstanceCoreReplacementRequest("uploads/new.jar", "server.jar"),
            new UpdateInstanceSettingsRequest(
                instanceId,
                "Example",
                InstanceType.MCJava,
                "java",
                ["-Xmx4G"],
                "1.21.5",
                new InstanceCoreReplacementRequest("uploads/new.jar", "server.jar"),
                false),
            installMetadata,
            new InstanceSettingsResult(configuration, "instances/example", true, true, null, installMetadata),
            new UpdateInstanceSettingsResult(configuration, true, false, ["old.jar"], ["backup/old.jar"]),
            new InstancePlayer("Alex", Guid.Parse("44444444-4444-4444-4444-444444444444")),
            new InstancePerformance(12.5, 1024),
            report,
            new InstanceReportList(ImmutableDictionary<Guid, ContractInstanceReport>.Empty.Add(instanceId, report)),
            new InstanceLogQuery(instanceId),
            new InstanceLogResult(["ready"]),
            new OperatingSystemInfo("Windows", "x64"),
            new ProcessorInfo("GenuineIntel", "CPU", 16, 5.5, 8, 16),
            new MemoryInfo(32768, 16384),
            drive,
            systemInfo,
            new JavaRuntime("C:/Java/bin/java.exe", "21.0.7", "x64"),
            new JavaRuntimeList([new JavaRuntime("C:/Java/bin/java.exe", "21.0.7", "x64")])
        ];
    }
}
