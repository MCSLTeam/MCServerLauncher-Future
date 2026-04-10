using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using System.Text.Json;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

public class DaemonOutboundTransportSerializationTests
{
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void ActionResponse_OutboundStjBoundaryWriter_SuccessTypedData_MatchesFrozenFixture()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = ParseJsonElement("""{"time":1717171717171}"""),
            Id = FixedResponseId
        };

        AssertMatchesFixture(
            SerializeOutbound(response),
            RpcFixturePaths.ActionResponseDir,
            "success-typed-data.json");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void ActionResponse_OutboundStjBoundaryWriter_SuccessEmptyObjectData_MatchesFrozenFixture()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = ParseJsonElement("{}"),
            Id = FixedResponseId
        };

        AssertMatchesFixture(
            SerializeOutbound(response),
            RpcFixturePaths.ActionResponseDir,
            "success-empty-object-data.json");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void ActionResponse_OutboundStjBoundaryWriter_ErrorNullData_MatchesFrozenFixture()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            Retcode = ActionRetcode.BadRequest.Code,
            Message = ActionRetcode.BadRequest.Message,
            Data = null,
            Id = FixedResponseId
        };

        AssertMatchesFixture(
            SerializeOutbound(response),
            RpcFixturePaths.ActionResponseDir,
            "error-null-data-message-retcode-shape.json");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void EventPacket_OutboundStjBoundaryWriter_WithMetaAndData_MatchesFrozenFixture()
    {
        var packet = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = SerializePayloadBuffer(new InstanceLogEventMeta
            {
                InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            }),
            EventData = SerializePayloadBuffer(new InstanceLogEventData
            {
                Log = "[12:00:00] [Server thread/INFO]: Hello"
            }),
            Timestamp = 1717171717000
        };

        AssertMatchesFixture(
            SerializeOutbound(packet),
            RpcFixturePaths.EventPacketDir,
            "with-meta-and-data.json");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void EventPacket_OutboundStjBoundaryWriter_NullMetaStructuredData_MatchesFrozenFixture()
    {
        var packet = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = SerializePayloadBuffer(new DaemonReportEventData
            {
                Report = new DaemonReport(
                    new OsInfo("Windows", "x64"),
                    new CpuInfo("GenuineIntel", "Intel(R)", 16, 0.25d),
                    new MemInfo(1024UL * 1024UL, 512UL * 1024UL),
                    new DriveInformation("NTFS", 1_000_000_000UL, 500_000_000UL),
                    1717171717000)
            }),
            Timestamp = 1717171717999
        };

        AssertMatchesFixture(
            SerializeOutbound(packet),
            RpcFixturePaths.EventPacketDir,
            "null-meta-structured-data.json");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void EventPacket_DirectStjEventPacket_WithMetaAndData_StructurallyMatchesPreparedEventPacket()
    {
        var meta = new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };
        var data = new InstanceLogEventData
        {
            Log = "[12:00:00] [Server thread/INFO]: Hello"
        };
        const long frozenTimestamp = 1717171717000;

        // Direct EventPacket (what PrivateSendEvent produces via PreparePayload)
        var directPacket = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = SerializePayloadBuffer(meta),
            EventData = SerializePayloadBuffer(data),
            Timestamp = frozenTimestamp
        };

        // Prepared EventPacket (what PrivateSendPreparedEvent produces from pre-buffered payloads)
        var preparedPacket = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = new JsonPayloadBuffer(StjJsonSerializer.SerializeToElement(meta, DaemonRpcJsonBoundary.StjOptions)),
            EventData = new JsonPayloadBuffer(StjJsonSerializer.SerializeToElement(data, DaemonRpcJsonBoundary.StjOptions)),
            Timestamp = frozenTimestamp
        };

        var directJson = SerializeOutbound(directPacket);
        var preparedJson = SerializeOutbound(preparedPacket);

        var direct = FixtureHarness.ParseJson(directJson);
        var prepared = FixtureHarness.ParseJson(preparedJson);
        FixtureHarness.AssertStructuralEquals(direct, prepared, "prepared-vs-direct-meta-and-data");
    }

    [Fact]
    [Trait("Category", "DaemonOutbound")]
    public void EventPacket_DirectStjEventPacket_NullMetaStructuredData_StructurallyMatchesPreparedEventPacket()
    {
        var data = new DaemonReportEventData
        {
            Report = new DaemonReport(
                new OsInfo("Windows", "x64"),
                new CpuInfo("GenuineIntel", "Intel(R)", 16, 0.25d),
                new MemInfo(1024UL * 1024UL, 512UL * 1024UL),
                new DriveInformation("NTFS", 1_000_000_000UL, 500_000_000UL),
                1717171717000)
        };
        const long frozenTimestamp = 1717171717999;

        var directPacket = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = SerializePayloadBuffer(data),
            Timestamp = frozenTimestamp
        };

        var preparedPacket = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = new JsonPayloadBuffer(StjJsonSerializer.SerializeToElement(data, DaemonRpcJsonBoundary.StjOptions)),
            Timestamp = frozenTimestamp
        };

        var directJson = SerializeOutbound(directPacket);
        var preparedJson = SerializeOutbound(preparedPacket);

        var direct = FixtureHarness.ParseJson(directJson);
        var prepared = FixtureHarness.ParseJson(preparedJson);
        FixtureHarness.AssertStructuralEquals(direct, prepared, "prepared-vs-direct-null-meta");
    }

    [Fact]
    [Trait("Category", "DaemonOutboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void DaemonOutboundFiles_DoNotUseJsonConvertSerializeObjectAtTransportBoundary()
    {
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/WsActionPlugin.cs", "JsonConvert.SerializeObject(");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/Action/ActionExecutor.cs", "JsonConvert.SerializeObject(");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/WsEventPlugin.cs", "JsonConvert.SerializeObject(");
    }

    [Fact]
    [Trait("Category", "DaemonOutboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void DaemonOutboundEventFile_DoesNotUseJTokenFromObjectAtTransportBoundary()
    {
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/WsEventPlugin.cs", "JToken.FromObject(");
    }

    private static string SerializeOutbound<T>(T value)
    {
        return StjJsonSerializer.Serialize(value, DaemonRpcJsonBoundary.StjOptions);
    }

    private static JsonPayloadBuffer SerializePayloadBuffer(object payload)
    {
        var element = StjJsonSerializer.SerializeToElement(payload, DaemonRpcJsonBoundary.StjOptions);
        return new JsonPayloadBuffer(element);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static void AssertMatchesFixture(string actualJson, string fixtureDir, string fixtureFile)
    {
        var expected = FixtureHarness.LoadFixture(fixtureDir, fixtureFile);
        var actual = FixtureHarness.ParseJson(actualJson);
        FixtureHarness.AssertStructuralEquals(expected, actual, fixtureFile);
    }

    private static void AssertFileDoesNotContain(string relativePath, string forbiddenText)
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, relativePath));
        Assert.DoesNotContain(forbiddenText, source, StringComparison.Ordinal);
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MCServerLauncher.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
