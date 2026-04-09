using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Action.Handlers;
using MCServerLauncher.Daemon.Remote.Authentication;
using RustyOptions;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using TouchSocket.Core;
using RResult = RustyOptions.Result;

namespace MCServerLauncher.ProtocolTests;

public class T9DaemonInboundParsingTests
{
    private static readonly Guid FixedId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void ParseRequest_ValidActionEnvelope_ParsesWithStjAdapter()
    {
        var executor = new FakeExecutor();
        var json =
            """
            {
              "action": 2,
              "params": {},
              "id": "33333333-3333-3333-3333-333333333333"
            }
            """;

        var result = executor.ParseRequest(json);

        Assert.True(result.IsOk(out var request));
        Assert.Equal(ActionType.Ping, request.ActionType);
        Assert.True(request.Parameter.HasValue);
        Assert.Equal(JsonValueKind.Object, request.Parameter.Value.ValueKind);
        Assert.Equal(FixedId, request.Id);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void ParseRequest_ValidUtf8ActionEnvelope_ParsesWithCachedTypeInfo()
    {
        var executor = new FakeExecutor();
        var json =
            """
            {
              "action": 2,
              "params": {},
              "id": "33333333-3333-3333-3333-333333333333"
            }
            """;

        var result = executor.ParseRequest(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsOk(out var request));
        Assert.Equal(ActionType.Ping, request.ActionType);
        Assert.True(request.Parameter.HasValue);
        Assert.Equal(JsonValueKind.Object, request.Parameter.Value.ValueKind);
        Assert.Equal(FixedId, request.Id);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundErrors")]
    public void ParseRequest_MalformedEnvelope_ReturnsBadRequestCouldNotParseActionJson()
    {
        var executor = new FakeExecutor();
        var result = executor.ParseRequest("{\"action\":\"Ping\"");

        Assert.True(result.IsErr(out var response));
        Assert.NotNull(response);
        Assert.Equal(ActionRequestStatus.Error, response.RequestStatus);
        Assert.Equal(ActionRetcode.BadRequest.Code, response.Retcode);
        Assert.Equal("Bad Request: Could not parse action json", response.Message);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundErrors")]
    public void ParseParameter_MissingParams_ReturnsParamErrorMissingParameters()
    {
        var handler = new TestParameterHandler();

        var parsed = ((IActionHandlerBase<EmptyActionParameter, EmptyActionResult>)handler).ParseParameter(null);

        Assert.True(parsed.IsErr(out var err));
        Assert.NotNull(err);
        Assert.Equal(ActionRetcode.ParamError.Code, err.Retcode.Code);
        Assert.Equal("Param Error: Missing parameters", err.Retcode.Message);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundErrors")]
    public void ParseParameter_InvalidParamType_ReturnsParamErrorCouldNotDeserializeParam()
    {
        var handler = new TestParameterHandler();
        var param = ParseElement("{\"path\":123}");

        var parsed = ((IActionHandlerBase<GetFileInfoParameter, EmptyActionResult>)handler).ParseParameter(param);

        Assert.True(parsed.IsErr(out var err));
        Assert.NotNull(err);
        Assert.Equal(ActionRetcode.ParamError.Code, err.Retcode.Code);
        Assert.Contains("Could not deserialize param", err.Retcode.Message);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundErrors")]
    public void CheckHandler_UnknownAction_ReturnsActionNotImplemented()
    {
        var executor = new FakeExecutor();
        var ctx = new WsContext("client", Guid.Empty, "*", DateTime.UtcNow.AddHours(1));
        var request = new ActionRequest
        {
            ActionType = ActionType.GetSystemInfo,
            Parameter = ParseElement("{}"),
            Id = FixedId
        };

        var result = executor.CheckHandler(request, ctx);

        Assert.True(result.IsErr(out var response));
        Assert.NotNull(response);
        Assert.Equal(ActionRetcode.ActionUnavailable.Code, response.Retcode);
        Assert.Equal("Action Unavailable: Action not implemented", response.Message);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundErrors")]
    public void CheckHandler_PermissionDenied_ReturnsPermissionDenied()
    {
        var executor = new FakeExecutor(
            new Dictionary<ActionType, ActionHandlerMeta>
            {
                [ActionType.Ping] = new ActionHandlerMeta(Permission.Of("mcsl.daemon.secret"), EActionHandlerType.Sync)
            });
        var ctx = new WsContext("client", Guid.Empty, "mcsl.daemon.other", DateTime.UtcNow.AddHours(1));
        var request = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = ParseElement("{}"),
            Id = FixedId
        };

        var result = executor.CheckHandler(request, ctx);

        Assert.True(result.IsErr(out var response));
        Assert.NotNull(response);
        Assert.Equal(ActionRetcode.PermissionDenied.Code, response.Retcode);
        Assert.Equal("Permission Denied: Permission denied", response.Message);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void HandleEventMetaAdapter_InstanceLogMeta_ParsesToTypedMeta()
    {
        var meta = ParseElement("{\"instance_id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}");

        var parsed = HandleEventMetaAdapter.GetEventMeta(EventType.InstanceLog, meta);

        var typed = Assert.IsType<InstanceLogEventMeta>(parsed);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), typed.InstanceId);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void HandleEventMetaAdapter_NullLiteralMeta_ThrowsJsonException()
    {
        var meta = ParseElement("null");
        var parsed = HandleEventMetaAdapter.GetEventMeta(EventType.InstanceLog, meta);
        Assert.Null(parsed);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void ResponseUtils_FromResult_None_ProjectsEmptyObjectWithoutJObjectBridge()
    {
        var response = ResponseUtils.FromResult(
            RResult.Ok<Option<IActionResult>, ActionError>(Option.None<IActionResult>()),
            FixedId);

        Assert.Equal(ActionRequestStatus.Ok, response.RequestStatus);
        Assert.True(response.Data.HasValue);
        Assert.Equal(JsonValueKind.Object, response.Data.Value.ValueKind);
        Assert.Empty(response.Data.Value.EnumerateObject());
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void CommonAssembly_InternalsVisibleTo_AllowsDaemonAndProtocolTestsToConsumeInternalPerformanceHelpers()
    {
        var friendAssemblies = typeof(StjResolver).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToArray();

        Assert.Contains("MCServerLauncher.Daemon", friendAssemblies);
        Assert.Contains("MCServerLauncher.ProtocolTests", friendAssemblies);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void ActionHandlerFile_UsesCachedDaemonRpcTypeInfo_ForTypedParamAndResultPaths()
    {
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "JsonElementHotPathAdapters.Deserialize(");
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "DaemonRpcTypeInfoCache<TParam>.TypeInfo");
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "JsonElementHotPathAdapters.SerializeToElement(");
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "DaemonRpcTypeInfoCache<TResult>.TypeInfo");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "token.Value.Deserialize<TParam>(DaemonRpcJsonBoundary.StjOptions)");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "SerializeToElement(value, DaemonRpcJsonBoundary.StjOptions)");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void ActionExecutorFile_UsesCachedDaemonRpcTypeInfo_ForRequestParsing()
    {
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "JsonElementHotPathAdapters.Deserialize(");
        AssertFileContains("MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "DaemonRpcTypeInfoCache<ActionRequest>.TypeInfo");
        AssertFileContains("MCServerLauncher.Daemon/Remote/WsActionPlugin.cs",
            "e.DataFrame.PayloadData");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/WsActionPlugin.cs",
            "e.DataFrame.ToText()");
        AssertFileDoesNotContain("MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "Deserialize<ActionRequest>(text, DaemonRpcJsonBoundary.StjOptions)");
    }

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static void AssertFileContains(string relativePath, string expectedText)
    {
        var repoRoot = ResolveRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, relativePath));
        Assert.Contains(expectedText, source, StringComparison.Ordinal);
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

    private sealed class FakeExecutor : IActionExecutor
    {
        public FakeExecutor(IReadOnlyDictionary<ActionType, ActionHandlerMeta>? handlerMetas = null)
        {
            HandlerMetas = handlerMetas ?? new Dictionary<ActionType, ActionHandlerMeta>();
        }

        public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

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

    private sealed class TestParameterHandler : IActionHandler<GetFileInfoParameter, EmptyActionResult>,
        IActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        Result<EmptyActionResult, ActionError> IActionHandler<GetFileInfoParameter, EmptyActionResult>.Handle(
            GetFileInfoParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return RResult.Ok<EmptyActionResult, ActionError>(new EmptyActionResult());
        }

        Result<EmptyActionResult, ActionError> IActionHandler<EmptyActionParameter, EmptyActionResult>.Handle(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return RResult.Ok<EmptyActionResult, ActionError>(new EmptyActionResult());
        }
    }
}
