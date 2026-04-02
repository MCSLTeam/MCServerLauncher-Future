using System.Reflection;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using MCServerLauncher.ProtocolTests.Fixtures.Rpc;
using MCServerLauncher.ProtocolTests.Helpers;
using RustyOptions;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using Xunit.Abstractions;
using RResult = RustyOptions.Result;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Coordinated-cutover only.
/// These tests prove the migrated daemon and migrated daemonclient seams agree on the approved transport contract.
/// They do not claim mixed-version runtime compatibility.
/// </summary>
public class T18DaemonDaemonClientConvergenceTests
{
    private static readonly Guid FixedRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedResponseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public void ClientOutboundToDaemonInbound_PingEmptyParams_RoundTripsThroughRealSeams()
    {
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        };

        var wireJson = SerializeAtClientSendSeam(request);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionRequestDir, "ping-empty-params.json");

        var parsedRequest = ParseAtDaemonInboundSeam(wireJson);
        var parameter = MaterializeDaemonParameter<EmptyActionParameter>(parsedRequest.Parameter);

        Assert.Equal(ActionType.Ping, parsedRequest.ActionType);
        Assert.Equal(FixedRequestId, parsedRequest.Id);
        Assert.NotNull(parameter);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public void ClientOutboundToDaemonInbound_SubscribeEventNullMeta_RoundTripsThroughRealSeams()
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

        var wireJson = SerializeAtClientSendSeam(request);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionRequestDir, "subscribe-event-null-meta.json");

        var parsedRequest = ParseAtDaemonInboundSeam(wireJson);
        var parameter = MaterializeDaemonParameter<SubscribeEventParameter>(parsedRequest.Parameter);

        Assert.Equal(ActionType.SubscribeEvent, parsedRequest.ActionType);
        Assert.Equal(EventType.InstanceLog, parameter.Type);
        Assert.True(parameter.Meta is null || parameter.Meta.Value.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public void ClientOutboundToDaemonInbound_SubscribeEventConcreteMeta_RoundTripsThroughRealSeams()
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

        var wireJson = SerializeAtClientSendSeam(request);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionRequestDir, "subscribe-event-concrete-meta.json");

        var parsedRequest = ParseAtDaemonInboundSeam(wireJson);
        var parameter = MaterializeDaemonParameter<SubscribeEventParameter>(parsedRequest.Parameter);
        var meta = AssertJsonElementDeserialize<InstanceLogEventMeta>(parameter.Meta, DaemonRpcJsonBoundary.StjOptions);

        Assert.Equal(ActionType.SubscribeEvent, parsedRequest.ActionType);
        Assert.Equal(EventType.InstanceLog, parameter.Type);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), meta.InstanceId);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public void ClientOutboundToDaemonInbound_SaveEventRulesNestedParameter_RoundTripsThroughRealSeams()
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

        var wireJson = SerializeAtClientSendSeam(request);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionRequestDir, "save-event-rules-nested-parameter.json");

        var parsedRequest = ParseAtDaemonInboundSeam(wireJson);
        var parameter = MaterializeDaemonParameter<SaveEventRulesParameter>(parsedRequest.Parameter);

        Assert.Equal(ActionType.SaveEventRules, parsedRequest.ActionType);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), parameter.InstanceId);

        var rule = Assert.Single(parameter.Rules);
        Assert.Equal("rule-one", rule.Name);
        Assert.Equal("nested-shape", rule.Description);
        Assert.True(rule.IsEnabled);
        Assert.Equal("Any", rule.TriggerCondition);

        var trigger = Assert.IsType<ConsoleOutputTrigger>(Assert.Single(rule.Triggers));
        Assert.Equal("joined", trigger.Pattern);
        Assert.False(trigger.IsRegex);

        Assert.IsType<AlwaysTrueRuleset>(Assert.Single(rule.Rulesets));

        var action = Assert.IsType<SendCommandAction>(Assert.Single(rule.Actions));
        Assert.Equal("say hi", action.Command);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public async Task DaemonOutboundToClientInbound_SuccessTypedDataResponse_RoundTripsThroughRealSeams()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = StjJsonSerializer.SerializeToElement(new PingResult { Time = 1717171717171 }, DaemonRpcJsonBoundary.StjOptions),
            Id = FixedResponseId
        };

        var wireJson = await SerializeAtDaemonActionPluginSeamAsync(response);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionResponseDir, "success-typed-data.json");

        var parsedResponse = WsReceivedPlugin.ParseActionResponse(wireJson);
        var typed = AssertJsonElementDeserialize<PingResult>(parsedResponse.Data, DaemonClientRpcJsonBoundary.StjOptions);

        Assert.Equal(ActionRequestStatus.Ok, parsedResponse.RequestStatus);
        Assert.Equal(FixedResponseId, parsedResponse.Id);
        Assert.Equal(1717171717171L, typed.Time);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public async Task DaemonOutboundToClientInbound_ErrorNullDataResponse_RoundTripsThroughRealSeams()
    {
        var response = new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            Retcode = ActionRetcode.BadRequest.Code,
            Message = ActionRetcode.BadRequest.Message,
            Data = null,
            Id = FixedResponseId
        };

        var wireJson = await SerializeAtDaemonActionPluginSeamAsync(response);
        AssertMatchesFixture(wireJson, RpcFixturePaths.ActionResponseDir, "error-null-data-message-retcode-shape.json");

        var parsedResponse = WsReceivedPlugin.ParseActionResponse(wireJson);

        Assert.Equal(ActionRequestStatus.Error, parsedResponse.RequestStatus);
        Assert.Equal(ActionRetcode.BadRequest.Code, parsedResponse.Retcode);
        Assert.Equal(ActionRetcode.BadRequest.Message, parsedResponse.Message);
        Assert.Equal(FixedResponseId, parsedResponse.Id);
        Assert.Null(parsedResponse.Data);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public async Task DaemonOutboundToClientInbound_InstanceLogEvent_RoundTripsThroughRealSeams()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var wireJson = await SerializeAtDaemonEventPluginSeamAsync(
            EventType.InstanceLog,
            new InstanceLogEventMeta
            {
                InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            },
            new InstanceLogEventData
            {
                Log = "[12:00:00] [Server thread/INFO]: Hello"
            });
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        AssertEventMatchesFixtureExceptTimestamp(
            wireJson,
            Path.Combine(RpcFixturePaths.EventPacketDir, "with-meta-and-data.json"),
            before,
            after);

        var parsedPacket = WsReceivedPlugin.ParseEventPacket(wireJson);
        var meta = Assert.IsType<InstanceLogEventMeta>(WsReceivedPlugin.MaterializeEventMeta(parsedPacket.EventType, parsedPacket.EventMeta));
        var data = Assert.IsType<InstanceLogEventData>(WsReceivedPlugin.MaterializeEventData(parsedPacket.EventType, parsedPacket.EventData));

        Assert.Equal(EventType.InstanceLog, parsedPacket.EventType);
        Assert.InRange(parsedPacket.Timestamp, before, after);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), meta.InstanceId);
        Assert.Equal("[12:00:00] [Server thread/INFO]: Hello", data.Log);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "CompatibilityConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public async Task DaemonOutboundToClientInbound_DaemonReportEventWithNullMeta_RoundTripsThroughRealSeams()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var wireJson = await SerializeAtDaemonEventPluginSeamAsync(
            EventType.DaemonReport,
            null,
            new DaemonReportEventData
            {
                Report = new DaemonReport(
                    new OsInfo("Windows", "x64"),
                    new CpuInfo("GenuineIntel", "Intel(R)", 16, 0.25d),
                    new MemInfo(1024UL * 1024UL, 512UL * 1024UL),
                    new DriveInformation("NTFS", 1_000_000_000UL, 500_000_000UL),
                    1717171717000)
            });
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        AssertEventMatchesFixtureExceptTimestamp(
            wireJson,
            Path.Combine(RpcFixturePaths.EventPacketDir, "null-meta-structured-data.json"),
            before,
            after);

        var parsedPacket = WsReceivedPlugin.ParseEventPacket(wireJson);
        var meta = WsReceivedPlugin.MaterializeEventMeta(parsedPacket.EventType, parsedPacket.EventMeta);
        var data = Assert.IsType<DaemonReportEventData>(WsReceivedPlugin.MaterializeEventData(parsedPacket.EventType, parsedPacket.EventData));

        Assert.Equal(EventType.DaemonReport, parsedPacket.EventType);
        Assert.InRange(parsedPacket.Timestamp, before, after);
        Assert.True(parsedPacket.EventMeta.HasValue);
        Assert.True(parsedPacket.EventMeta.Value.IsExplicitJsonNull);
        Assert.Null(meta);
        Assert.Equal("Windows", data.Report.Os.Name);
        Assert.Equal("x64", data.Report.Os.Arch);
    }

    [Fact]
    [Trait("Category", "T18")]
    [Trait("Category", "SchemaLockConvergence")]
    [Trait("Category", "EndToEndIntegration")]
    public void CoordinatedCutoverOnly_ConvergenceSuite_ReassertsRpcGoldenPersistenceAndDocumentationLocks()
    {
        AssertRpcGoldenFixturesStillPass();
        AssertRepresentativePersistenceChecksStillPass();
        AssertDocumentationSchemaProxyStillPass();
    }

    private static void AssertRpcGoldenFixturesStillPass()
    {
        var rpcGolden = new RpcGoldenCharacterizationTests();

        rpcGolden.ActionRequest_PingEmptyParams_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionRequest_SubscribeEventNullMeta_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionRequest_SubscribeEventConcreteMeta_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionRequest_SaveEventRulesNestedParameter_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionResponse_SuccessTypedData_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionResponse_SuccessEmptyObjectData_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.ActionResponse_ErrorNullDataMessageRetcodeShape_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.EventPacket_WithMetaAndData_SchemaLockedFixture_MatchesNewtonsoftBaseline();
        rpcGolden.EventPacket_NullMetaStructuredData_SchemaLockedFixture_MatchesNewtonsoftBaseline();
    }

    private static void AssertRepresentativePersistenceChecksStillPass()
    {
        var persistence = new PersistenceMigrationCharacterizationTests();

        persistence.PersistenceGolden_ConfigFixture_DeserializeAndReserialize_MatchesCurrentJsonContract();
        persistence.PersistenceGolden_EventRuleHeavyInstanceConfigFixture_DeserializeAndReserialize_MatchesCurrentJsonContract();
        persistence.PersistenceGolden_ReadJsonOr_MissingFile_WritesDefaultAndReturnsIt();
        persistence.BackupBehavior_WriteJsonAndBackup_ValidExistingFile_CreatesBakThenWritesNewContent();
    }

    private static void AssertDocumentationSchemaProxyStillPass()
    {
        var documentation = new DocumentationValidationTests(new NullTestOutputHelper());

        documentation.PolicyDocument_Exists_And_IsReadable();
        documentation.PolicyDocument_RpcFixtureFiles_ExistOnDisk();
        documentation.PolicyDocument_ContainsCriticalSections();
        documentation.PolicyDocument_RequiredNullPolicy_IsDocumentedPerField();
    }

    private static string SerializeAtClientSendSeam(ActionRequest request)
    {
        return Encoding.UTF8.GetString(ClientConnection.SerializeActionRequestForTransport(request));
    }

    private static ActionRequest ParseAtDaemonInboundSeam(string wireJson)
    {
        var executor = new FakeExecutor();
        var parsed = executor.ParseRequest(wireJson);

        Assert.True(parsed.IsOk(out var request));
        return request;
    }

    private static TParam MaterializeDaemonParameter<TParam>(JsonElement? parameter)
        where TParam : class, IActionParameter
    {
        var handler = new ParameterProbeHandler<TParam>();
        var parsed = ((IActionHandlerBase<TParam, EmptyActionResult>)handler).ParseParameter(parameter);

        Assert.True(parsed.IsOk(out var typedParameter));
        return typedParameter;
    }

    private static T AssertJsonElementDeserialize<T>(JsonElement? element, JsonSerializerOptions options)
    {
        Assert.True(element.HasValue);
        return StjJsonSerializer.Deserialize<T>(element.Value.GetRawText(), options)!;
    }

    private static async Task<string> SerializeAtDaemonActionPluginSeamAsync(ActionResponse response)
    {
        var clientId = "t18-client";
        var container = new WsContextContainer();
        container.CreateContext(clientId, Guid.Empty, "*", DateTime.UtcNow.AddHours(1));

        var executor = new ActionPluginExecutor((_, _) => response);
        var plugin = new WsActionPlugin(executor, CreateProxy<IHttpService>((_, _) => null), container);

        string? sent = null;
        var sessionClient = CreateProxy<IHttpSessionClient>((method, _) =>
        {
            return method.Name switch
            {
                "get_Id" => clientId,
                _ => GetDefaultReturnValue(method.ReturnType)
            };
        });

        var webSocket = CreateProxy<IWebSocket>((method, args) =>
        {
            return method.Name switch
            {
                "get_Client" => sessionClient,
                "SendAsync" when method.GetParameters().Length == 3 && method.GetParameters()[0].ParameterType == typeof(string)
                    => CaptureSentString((string)args![0]!, value => sent = value),
                _ => GetDefaultReturnValue(method.ReturnType)
            };
        });

        var requestJson = SerializeAtClientSendSeam(new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseJsonElement("{}"),
            Id = FixedRequestId
        });

        var frame = new WSDataFrame(Encoding.UTF8.GetBytes(requestJson))
        {
            Opcode = WSDataType.Text,
            FIN = true
        };

        await plugin.OnWebSocketReceived(webSocket, new WSDataFrameEventArgs(frame));
        Assert.NotNull(sent);
        return sent!;
    }

    private static async Task<string> SerializeAtDaemonEventPluginSeamAsync(
        EventType eventType,
        IEventMeta? meta,
        IEventData? data)
    {
        string? sent = null;
        var webSocket = CreateProxy<IWebSocket>((method, args) =>
        {
            return method.Name switch
            {
                "SendAsync" when method.GetParameters().Length == 3 && method.GetParameters()[0].ParameterType == typeof(string)
                    => CaptureSentString((string)args![0]!, value => sent = value),
                _ => GetDefaultReturnValue(method.ReturnType)
            };
        });

        var privateSendEvent = typeof(WsEventPlugin).GetMethod("PrivateSendEvent", BindingFlags.Static | BindingFlags.NonPublic)
                               ?? throw new MissingMethodException(typeof(WsEventPlugin).FullName, "PrivateSendEvent");

        var invoked = privateSendEvent.Invoke(null, [eventType, meta, data, webSocket]);
        switch (invoked)
        {
            case ValueTask valueTask:
                await valueTask;
                break;
            case Task task:
                await task;
                break;
        }

        Assert.NotNull(sent);
        return sent!;
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

    private static void AssertEventMatchesFixtureExceptTimestamp(
        string actualJson,
        string fixturePath,
        long minTimestamp,
        long maxTimestamp)
    {
        var expected = FixtureHarness.LoadFixture(fixturePath);
        var actual = FixtureHarness.ParseJson(actualJson);

        Assert.Equal(expected.GetProperty("event").GetString(), actual.GetProperty("event").GetString());
        Assert.True(JsonElement.DeepEquals(expected.GetProperty("meta"), actual.GetProperty("meta")));
        Assert.True(JsonElement.DeepEquals(expected.GetProperty("data"), actual.GetProperty("data")));
        Assert.InRange(actual.GetProperty("time").GetInt64(), minTimestamp, maxTimestamp);
    }

    private static Task CaptureSentString(string value, Action<string> setter)
    {
        setter(value);
        return Task.CompletedTask;
    }

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceDispatchProxy>();
        ((InterfaceDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? GetDefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType == typeof(ValueTask))
        {
            return ValueTask.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [resultValue]);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var resultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return Activator.CreateInstance(returnType, resultValue);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private sealed class FakeExecutor : IActionExecutor
    {
        public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; } =
            new Dictionary<ActionType, ActionHandlerMeta>();

        public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
            SyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>();

        public IReadOnlyDictionary<ActionType,
                Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
            AsyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>();

        public ActionResponse? ProcessAction(string text, WsContext ctx)
        {
            throw new NotSupportedException();
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ActionPluginExecutor(Func<string, WsContext, ActionResponse> responseFactory) : IActionExecutor
    {
        public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; } =
            new Dictionary<ActionType, ActionHandlerMeta>();

        public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
            SyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>();

        public IReadOnlyDictionary<ActionType,
                Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
            AsyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>();

        public ActionResponse? ProcessAction(string text, WsContext ctx)
        {
            return responseFactory(text, ctx);
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ParameterProbeHandler<TParam> : IActionHandler<TParam, EmptyActionResult>
        where TParam : class, IActionParameter
    {
        public Result<EmptyActionResult, ActionError> Handle(
            TParam param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return RResult.Ok<EmptyActionResult, ActionError>(new EmptyActionResult());
        }
    }

    private sealed class NullTestOutputHelper : ITestOutputHelper
    {
        public void WriteLine(string message)
        {
        }

        public void WriteLine(string format, params object[] args)
        {
        }
    }

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}
