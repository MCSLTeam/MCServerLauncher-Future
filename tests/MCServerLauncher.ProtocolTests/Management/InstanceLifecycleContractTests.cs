using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;
using RuntimeInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.ProtocolTests.Management;

public sealed class InstanceLifecycleContractTests
{
    public static TheoryData<InstanceStatus> Preview1Statuses =>
    [
        InstanceStatus.Running,
        InstanceStatus.Stopped,
        InstanceStatus.Crashed,
        InstanceStatus.Starting,
        InstanceStatus.Stopping
    ];

    [Theory]
    [MemberData(nameof(Preview1Statuses))]
    public void MapperAndSourceGeneratedJsonPreserveEveryLifecycleStatusAndReadyTimeout(
        InstanceStatus status)
    {
        var config = new InstanceConfig
        {
            Name = "ready-timeout",
            Target = "server.jar",
            InstanceType = InstanceType.MCJava,
            TargetType = TargetType.Jar
        };
        var runtime = new RuntimeInstanceReport(
            status,
            config,
            new Dictionary<string, string>(),
            [],
            default,
            ReadyTimedOut: true);

        ContractInstanceReport contract = InstanceConfigurationMapper.ToContract(runtime, processId: 42);

        Assert.True(contract.ReadyTimedOut);
        Assert.Equal(42, contract.ProcessId);

        var json = JsonSerializer.Serialize(
            contract,
            ApplicationContractJsonContext.Default.InstanceReport);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("ready_timed_out").GetBoolean());

        var roundTrip = JsonSerializer.Deserialize(
            json,
            ApplicationContractJsonContext.Default.InstanceReport);
        Assert.NotNull(roundTrip);
        Assert.True(roundTrip.ReadyTimedOut);
        Assert.Equal(status, roundTrip.Status);

        var catalogItem = new InstanceCatalogItem(
            config.Uuid,
            config.Name,
            config.InstanceType,
            config.Version,
            status,
            readyTimedOut: true);
        var catalogJson = JsonSerializer.Serialize(
            catalogItem,
            BuiltInProtocolJsonContext.Default.InstanceCatalogItem);
        using var catalogDocument = JsonDocument.Parse(catalogJson);
        Assert.True(catalogDocument.RootElement.GetProperty("ready_timed_out").GetBoolean());
        var catalogRoundTrip = JsonSerializer.Deserialize(
            catalogJson,
            BuiltInProtocolJsonContext.Default.InstanceCatalogItem);
        Assert.NotNull(catalogRoundTrip);
        Assert.Equal(status, catalogRoundTrip.Status);
        Assert.True(catalogRoundTrip.ReadyTimedOut);
    }
}
