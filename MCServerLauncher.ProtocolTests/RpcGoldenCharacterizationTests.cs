using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

public class RpcGoldenCharacterizationTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_PingEmptyParams_ClientRpcBoundary_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeClientRpc(request),
            RpcFixturePaths.ActionRequestDir,
            "ping-empty-params.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SubscribeEventNullMeta_ClientRpcBoundary_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = SerializeClientPayloadElement(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = null
            }),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeClientRpc(request),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-null-meta.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SubscribeEventConcreteMeta_ClientRpcBoundary_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = SerializeClientPayloadElement(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = SerializeClientPayloadElement(new InstanceLogEventMeta
                {
                    InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                })
            }),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeClientRpc(request),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-concrete-meta.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SaveEventRulesNestedParameter_ClientRpcBoundary_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = SerializeClientPayloadElement(new SaveEventRulesParameter
            {
                InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Rules =
                [
                    new EventRule
                    {
                        Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        Name = "rule-one",
                        Description = "nested-shape",
                        IsEnabled = true,
                        TriggerCondition = "Any",
                        Triggers =
                        [
                            new ConsoleOutputTrigger
                            {
                                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                                Pattern = "joined",
                                IsRegex = false
                            }
                        ],
                        ActionExecutionMode = "Sequential",
                        Rulesets =
                        [
                            new AlwaysTrueRuleset
                            {
                                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")
                            }
                        ],
                        Actions =
                        [
                            new SendCommandAction
                            {
                                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                                Command = "say hi"
                            }
                        ]
                    }
                ]
            }),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeClientRpc(request),
            RpcFixturePaths.ActionRequestDir,
            "save-event-rules-nested-parameter.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_SuccessTypedData_DaemonRpcBoundary_MatchesFrozenFixture()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = StjJsonSerializer.SerializeToElement(new PingResult { Time = 1717171717171 }, DaemonRpcJsonBoundary.StjOptions),
            Id = FixedResponseId
        };

        AssertMatchesFixture(
            SerializeDaemonRpc(response),
            RpcFixturePaths.ActionResponseDir,
            "success-typed-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_SuccessEmptyObjectData_DaemonRpcBoundary_MatchesFrozenFixture()
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
            SerializeDaemonRpc(response),
            RpcFixturePaths.ActionResponseDir,
            "success-empty-object-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_ErrorNullDataMessageRetcodeShape_DaemonRpcBoundary_MatchesFrozenFixture()
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
            SerializeDaemonRpc(response),
            RpcFixturePaths.ActionResponseDir,
            "error-null-data-message-retcode-shape.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void EventPacket_WithMetaAndData_DaemonRpcBoundary_MatchesFrozenFixture()
    {
        var packet = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = SerializeDaemonPayloadBuffer(new InstanceLogEventMeta
            {
                InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            }),
            EventData = SerializeDaemonPayloadBuffer(new InstanceLogEventData
            {
                Log = "[12:00:00] [Server thread/INFO]: Hello"
            }),
            Timestamp = 1717171717000
        };

        AssertMatchesFixture(
            SerializeDaemonRpc(packet),
            RpcFixturePaths.EventPacketDir,
            "with-meta-and-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void EventPacket_NullMetaStructuredData_DaemonRpcBoundary_MatchesFrozenFixture()
    {
        var packet = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = null,
            EventData = SerializeDaemonPayloadBuffer(new DaemonReportEventData
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
            SerializeDaemonRpc(packet),
            RpcFixturePaths.EventPacketDir,
            "null-meta-structured-data.json");
    }

    private static string SerializeClientRpc<T>(T value)
    {
        return StjJsonSerializer.Serialize(value, DaemonClientRpcJsonBoundary.StjOptions);
    }

    private static string SerializeDaemonRpc<T>(T value)
    {
        return StjJsonSerializer.Serialize(value, DaemonRpcJsonBoundary.StjOptions);
    }

    private static JsonElement SerializeClientPayloadElement<T>(T value)
    {
        return StjJsonSerializer.SerializeToElement(value, DaemonClientRpcJsonBoundary.StjOptions);
    }

    private static JsonPayloadBuffer SerializeDaemonPayloadBuffer<T>(T payload)
    {
        return JsonPayloadBuffer.FromObject(payload, DaemonRpcJsonBoundary.StjOptions);
    }

    private static void AssertMatchesFixture(string actualJson, string fixtureDir, string fixtureFile)
    {
        var expected = FixtureHarness.LoadFixture(fixtureDir, fixtureFile);
        var actual = FixtureHarness.ParseJson(actualJson);
        FixtureHarness.AssertStructuralEquals(expected, actual, fixtureFile);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
