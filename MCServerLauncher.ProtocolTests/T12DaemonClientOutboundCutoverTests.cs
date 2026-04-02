using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;

namespace MCServerLauncher.ProtocolTests;

public class T12DaemonClientOutboundCutoverTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    public void ClientOutboundSendSeam_InterfaceTypedGetInstanceReportParameter_PreservesIdPayload()
    {
        IActionParameter param = new GetInstanceReportParameter
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        var request = new ActionRequest
        {
            ActionType = ActionType.GetInstanceReport,
            Parameter = InvokePrivateSerializeParameterForTransport(param),
            Id = FixedRequestId
        };

        var actual = FixtureHarness.ParseJson(SerializeAtClientSendSeam(request));
        var payload = actual.GetProperty("params");

        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), payload.GetProperty("id").GetGuid());
    }

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    public void ClientOutboundSendSeam_ActionRequestPingEmptyParams_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeAtClientSendSeam(request),
            RpcFixturePaths.ActionRequestDir,
            "ping-empty-params.json");
    }

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    public void ClientOutboundSendSeam_ActionRequestSubscribeEventNullMeta_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = ParseJsonElement(
                """
                {
                  "type": "instance_log",
                  "meta": null
                }
                """),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeAtClientSendSeam(request),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-null-meta.json");
    }

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    public void ClientOutboundSendSeam_ActionRequestSubscribeEventConcreteMeta_MatchesFrozenFixture()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.SubscribeEvent,
            Parameter = ParseJsonElement(
                """
                {
                  "type": "instance_log",
                  "meta": {
                    "instance_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                  }
                }
                """),
            Id = FixedRequestId
        };

        AssertMatchesFixture(
            SerializeAtClientSendSeam(request),
            RpcFixturePaths.ActionRequestDir,
            "subscribe-event-concrete-meta.json");
    }

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    public void ClientOutboundSendSeam_ActionRequestSaveEventRulesNestedParameter_MatchesFrozenFixture()
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
            SerializeAtClientSendSeam(request),
            RpcFixturePaths.ActionRequestDir,
            "save-event-rules-nested-parameter.json");
    }

    [Fact]
    [Trait("Category", "ClientOutbound")]
    [Trait("Category", "ClientOutboundRoundTrip")]
    [Trait("Category", "CleanupValidation")]
    public void ClientOutboundSendSeam_DoesNotUseJsonConvertSerializeObject()
    {
        var source = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "MCServerLauncher.DaemonClient/Connection/ClientConnection.cs"));

        Assert.DoesNotContain("JsonConvert.SerializeObject(", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ClientCallbackCorrelation")]
    public async Task ClientCallbackCorrelation_DispatchAction_UsesExactResponseIdToResolvePendingEntry()
    {
        var pending = new ConnectionPendingRequests(size: 4);
        var targetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var otherId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var targetTcs = new TaskCompletionSource<ActionResponse>();
        var otherTcs = new TaskCompletionSource<ActionResponse>();

        Assert.True(await pending.AddPendingAsync(targetId, targetTcs, timeout: 1000));
        Assert.True(await pending.AddPendingAsync(otherId, otherTcs, timeout: 1000));

        var plugin = new WsReceivedPlugin(pending);

        InvokePrivateDispatchAction(plugin, BuildActionResponseEnvelope(targetId));

        var response = await targetTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(targetId, response.Id);
        Assert.False(otherTcs.Task.IsCompleted);
        Assert.False(pending.TryGetPending(targetId, out _));
        Assert.True(pending.TryGetPending(otherId, out _));

        pending.TryRemovePending(otherId, out _);
    }

    [Fact]
    [Trait("Category", "ClientCallbackCorrelation")]
    public async Task ClientCallbackCorrelation_DispatchAction_UnknownResponseId_IsIgnoredWithoutCompletingKnownPending()
    {
        var pending = new ConnectionPendingRequests(size: 2);
        var knownId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var unknownId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var knownTcs = new TaskCompletionSource<ActionResponse>();
        Assert.True(await pending.AddPendingAsync(knownId, knownTcs, timeout: 1000));

        var plugin = new WsReceivedPlugin(pending);

        InvokePrivateDispatchAction(plugin, BuildActionResponseEnvelope(unknownId));

        Assert.False(knownTcs.Task.IsCompleted);
        Assert.True(pending.TryGetPending(knownId, out _));

        pending.TryRemovePending(knownId, out _);
    }

    [Fact]
    [Trait("Category", "ClientCallbackCorrelation")]
    public void ClientCallbackCorrelation_DispatchAction_GuidEmptyResponseId_ThrowsArgumentNullException()
    {
        var pending = new ConnectionPendingRequests(size: 1);
        var plugin = new WsReceivedPlugin(pending);

        var ex = Assert.Throws<ArgumentNullException>(() => InvokePrivateDispatchAction(plugin, BuildActionResponseEnvelope(Guid.Empty)));
        Assert.Equal("Id", ex.ParamName);
    }

    private static string SerializeAtClientSendSeam(ActionRequest request)
    {
        return Encoding.UTF8.GetString(ClientConnection.SerializeActionRequestForTransport(request));
    }

    private static JsonElement InvokePrivateSerializeParameterForTransport(IActionParameter? param)
    {
        var method = typeof(ClientConnection).GetMethod("SerializeParameterForTransport", BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(ClientConnection).FullName, "SerializeParameterForTransport");

        return (JsonElement)method.Invoke(null, new object?[] { param })!;
    }

    private static void InvokePrivateDispatchAction(WsReceivedPlugin plugin, string received)
    {
        var method = typeof(WsReceivedPlugin).GetMethod("DispatchAction", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(typeof(WsReceivedPlugin).FullName, "DispatchAction");

        try
        {
            method.Invoke(plugin, new object[] { received });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static string BuildActionResponseEnvelope(Guid id)
    {
        return
            $$"""
              {
                "status": "ok",
                "retcode": 0,
                "data": {},
                "message": "OK",
                "id": "{{id}}"
              }
              """;
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
