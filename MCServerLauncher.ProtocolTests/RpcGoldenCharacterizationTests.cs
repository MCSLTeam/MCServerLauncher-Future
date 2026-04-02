using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using MCServerLauncher.Common.ProtoType.Serialization;
using System.Text.Json;
using Newtonsoft.Json;

namespace MCServerLauncher.ProtocolTests;

public class RpcGoldenCharacterizationTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_PingEmptyParams_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        // Schema-lock fixture: keep envelope field names/shape for RPC compatibility.
        // Required/null cleanup can be intentionally changed later, but shape must remain explicit.
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(request, JsonSettings.Settings),
            RpcFixturePaths.ActionRequestDir,
            "ping-empty-params.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SubscribeEventNullMeta_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = ParseViaNewtonsoft(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = null
            }),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(request, JsonSettings.Settings),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-null-meta.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SubscribeEventConcreteMeta_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = ParseViaNewtonsoft(new SubscribeEventParameter
            {
                Type = EventType.InstanceLog,
                Meta = ParseViaNewtonsoft(new InstanceLogEventMeta
                {
                    InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                })
            }),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(request, JsonSettings.Settings),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-concrete-meta.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionRequest_SaveEventRulesNestedParameter_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SaveEventRules,
            Parameter = ParseJsonElement(
                """
                {
                  "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "rules": [
                    {
                      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                      "name": "rule-one",
                      "description": "nested-shape",
                      "is_enabled": true,
                      "trigger_condition": "Any",
                      "triggers": [
                        {
                          "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                          "type": "ConsoleOutput",
                          "pattern": "joined",
                          "is_regex": false
                        }
                      ],
                      "action_execution_mode": "Sequential",
                      "rulesets": [
                        {
                          "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
                          "type": "AlwaysTrue"
                        }
                      ],
                      "actions": [
                        {
                          "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                          "type": "SendCommand",
                          "command": "say hi"
                        }
                      ]
                    }
                  ]
                }
                """),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(request, JsonSettings.Settings),
            RpcFixturePaths.ActionRequestDir,
            "save-event-rules-nested-parameter.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_SuccessTypedData_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = ParseViaNewtonsoft(new PingResult { Time = 1717171717171 }),
            Id = FixedResponseId
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(response, JsonSettings.Settings),
            RpcFixturePaths.ActionResponseDir,
            "success-typed-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_SuccessEmptyObjectData_SchemaLockedFixture_MatchesNewtonsoftBaseline()
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
            JsonConvert.SerializeObject(response, JsonSettings.Settings),
            RpcFixturePaths.ActionResponseDir,
            "success-empty-object-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void ActionResponse_ErrorNullDataMessageRetcodeShape_SchemaLockedFixture_MatchesNewtonsoftBaseline()
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
            JsonConvert.SerializeObject(response, JsonSettings.Settings),
            RpcFixturePaths.ActionResponseDir,
            "error-null-data-message-retcode-shape.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void EventPacket_WithMetaAndData_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var packet = new EventPacket
        {
            EventType = EventType.InstanceLog,
            EventMeta = JsonPayloadBuffer.FromObject(new InstanceLogEventMeta
            {
                InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            }, JsonSettings.Settings),
            EventData = JsonPayloadBuffer.FromObject(new InstanceLogEventData
            {
                Log = "[12:00:00] [Server thread/INFO]: Hello"
            }, JsonSettings.Settings),
            Timestamp = 1717171717000
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(packet, JsonSettings.Settings),
            RpcFixturePaths.EventPacketDir,
            "with-meta-and-data.json");
    }

    [Fact]
    [Trait("Category", "RpcGolden")]
    public void EventPacket_NullMetaStructuredData_SchemaLockedFixture_MatchesNewtonsoftBaseline()
    {
        var packet = new EventPacket
        {
            EventType = EventType.DaemonReport,
            EventMeta = JsonPayloadBuffer.FromObject(null, JsonSettings.Settings),
            EventData = JsonPayloadBuffer.FromObject(new DaemonReportEventData
            {
                Report = new DaemonReport(
                    new OsInfo("Windows", "x64"),
                    new CpuInfo("GenuineIntel", "Intel(R)", 16, 0.25d),
                    new MemInfo(1024UL * 1024UL, 512UL * 1024UL),
                    new DriveInformation("NTFS", 1_000_000_000UL, 500_000_000UL),
                    1717171717000)
            }, JsonSettings.Settings),
            Timestamp = 1717171717999
        };

        AssertMatchesFixture(
            JsonConvert.SerializeObject(packet, JsonSettings.Settings),
            RpcFixturePaths.EventPacketDir,
            "null-meta-structured-data.json");
    }

    private static void AssertMatchesFixture(string actualJson, string fixtureDir, string fixtureFile)
    {
        var expected = FixtureHarness.LoadFixture(fixtureDir, fixtureFile);
        var actual = FixtureHarness.ParseJson(actualJson);
        FixtureHarness.AssertStructuralEquals(expected, actual, fixtureFile);
    }

    private static JsonElement ParseViaNewtonsoft(object payload)
    {
        var json = JsonConvert.SerializeObject(payload, JsonSettings.Settings);
        return ParseJsonElement(json);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
