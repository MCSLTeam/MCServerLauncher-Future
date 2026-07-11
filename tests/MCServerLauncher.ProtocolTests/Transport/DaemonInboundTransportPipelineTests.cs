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

public class DaemonInboundTransportPipelineTests
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
    public void ParseRequest_NullLiteralEnvelope_ReturnsBadRequest()
    {
        var executor = new FakeExecutor();
        var result = executor.ParseRequest("null");

        Assert.True(result.IsErr(out var response));
        Assert.NotNull(response);
        Assert.Equal(ActionRequestStatus.Error, response.RequestStatus);
        Assert.Equal(ActionRetcode.BadRequest.Code, response.Retcode);
        Assert.Equal("Bad Request: Received null action request envelope", response.Message);
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
    public void HandleEventMetaAdapter_NullLiteralMeta_ReturnsNull()
    {
        var meta = ParseElement("null");
        var result = HandleEventMetaAdapter.GetEventMeta(EventType.InstanceLog, meta);
        Assert.Null(result);
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
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "JsonElementHotPathAdapters.Deserialize(");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "DaemonRpcTypeInfoCache<TParam>.TypeInfo");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "JsonElementHotPathAdapters.SerializeToElement(");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "DaemonRpcTypeInfoCache<TResult>.TypeInfo");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "token.Value.Deserialize<TParam>(DaemonRpcJsonBoundary.StjOptions)");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/Action/ActionHandler.cs",
            "SerializeToElement(value, DaemonRpcJsonBoundary.StjOptions)");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void ActionExecutorFile_UsesCachedDaemonRpcTypeInfo_ForRequestParsing()
    {
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "ActionResponse? ProcessAction(ReadOnlyMemory<byte> utf8Json, WsContext ctx)");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "DaemonRpcTypeInfoCache<ActionRequest>.TypeInfo");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "JsonSerializer.Deserialize(");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/WsActionPlugin.cs",
            "e.DataFrame.PayloadData");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/WsActionPlugin.cs",
            "e.DataFrame.ToText()");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "Deserialize<ActionRequest>(text, DaemonRpcJsonBoundary.StjOptions)");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/Action/IActionExecutor.cs",
            "ActionResponse? ProcessAction(string text, WsContext ctx)");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void DaemonServiceComposition_ConfigurePlugins_LocksPluginRegistrationOrder()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs");

        // The plugin registration order in ConfigurePlugins must remain exactly:
        //   1. HttpPlugin
        //   2. UseWebSocket (with /api/v1)
        //   3. WsBasePlugin
        //   4. WsActionPlugin
        //   5. WsEventPlugin
        //   6. WsExpirationPlugin
        //   7. UseDefaultHttpServicePlugin
        var configPluginsStart = source.IndexOf("internal static void ConfigurePlugins", StringComparison.Ordinal);
        Assert.True(configPluginsStart >= 0, "ConfigurePlugins method not found");

        var configPluginsBody = source[configPluginsStart..];
        Assert.DoesNotContain("Add<FileSystemWatcherPlugin>", configPluginsBody, StringComparison.Ordinal);
        var order = new[]
        {
            "Add<HttpPlugin>",
            "UseWebSocket",
            "Add<WsBasePlugin>",
            "Add<WsActionPlugin>",
            "Add<WsEventPlugin>",
            "Add<WsExpirationPlugin>",
            "UseDefaultHttpServicePlugin",
        };

        var positions = order
            .Select(marker => configPluginsBody.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        // All markers must be found
        for (var i = 0; i < order.Length; i++)
        {
            Assert.True(positions[i] >= 0, $"Plugin marker '{order[i]}' not found in ConfigurePlugins");
        }

        // Each marker must appear after the previous one (strict order)
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(positions[i] > positions[i - 1],
                $"Plugin '{order[i]}' must appear after '{order[i - 1]}' in ConfigurePlugins");
        }
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void DaemonServiceComposition_ConfigurePlugins_LocksApiV1WebsocketPathAndVerifyHandler()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs");

        // The /api/v1 path and verify handler must remain exactly as-is
        Assert.Contains("options.SetUrl(\"/api/v1\")", source, StringComparison.Ordinal);
        Assert.Contains("options.SetVerifyConnection(WsVerifyHandler.VerifyHandler)", source, StringComparison.Ordinal);
        Assert.Contains("options.SetAutoPong(true)", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void ApplicationStartupLog_PointsToApifoxDocsConnectUrlsOnly()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("var endpoints = GetRemoteEndpoints();", source, StringComparison.Ordinal);
        Assert.Contains("[Remote] Apifox docs connect URLs: {ConnectUrls}", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[Remote] Apifox docs available at http://0.0.0.0:{0}/apifox.json", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[Remote] Apifox docs available at http://{RemoteAddress}/apifox.json", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("postman_collection.json", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void DaemonAssembly_EmbedsApifoxProjectAndProtocolDocsOnly()
    {
        var resourceNames = typeof(HttpPlugin).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.openapi.json", resourceNames);
        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.postman_collection.json", resourceNames);
        Assert.Contains("MCServerLauncher.Daemon.Resources.Docs.apifox.json", resourceNames);
        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.swagger.index.html", resourceNames);
        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.swagger.swagger-ui.css", resourceNames);
        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.swagger.swagger-ui-bundle.js", resourceNames);
        Assert.DoesNotContain("MCServerLauncher.Daemon.Resources.Docs.swagger.swagger-ui-standalone-preset.js", resourceNames);
        Assert.Contains("MCServerLauncher.Daemon.Resources.Docs.protocol.topics.connection.md", resourceNames);
        Assert.Contains("MCServerLauncher.Daemon.Resources.Docs.protocol.topics.actions.md", resourceNames);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void HttpPluginFile_ExposesEmbeddedApifoxProjectAndProtocolDocs()
    {
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/HttpPlugin.cs",
            "EmbeddedDocumentation.TryGetResource(request.URL, out var document)");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "\"/openapi.json\"");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "\"/postman_collection.json\"");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "\"/apifox.json\"");
        AssertFileContains("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "\"/docs/protocol/\"");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "\"/swagger\"");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void HttpPluginFile_DoesNotExposeActionHttpBridge()
    {
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/HttpPlugin.cs",
            "HttpActionBridge.TryGetActionName(request.URL, out var actionName)");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/HttpPlugin.cs",
            "HttpActionBridge.HandleAsync");
        Assert.False(File.Exists(Path.Combine(ResolveRepoRoot(), "src/MCServerLauncher.Daemon/Remote/HttpActionBridge.cs")));
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void OpenApiArtifacts_AreNotGeneratedOrEmbedded()
    {
        var repoRoot = ResolveRepoRoot();
        Assert.False(File.Exists(Path.Combine(repoRoot, "src/MCServerLauncher.Daemon/.Resources/Docs/openapi.json")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "../mcsl-future-protocol/openapi.json")));
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj",
            ".Resources\\Docs\\openapi.json");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void PostmanArtifacts_AreNotGeneratedOrEmbedded()
    {
        var repoRoot = ResolveRepoRoot();

        Assert.False(File.Exists(Path.Combine(repoRoot, "src/MCServerLauncher.Daemon/.Resources/Docs/postman_collection.json")));
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj",
            ".Resources\\Docs\\postman_collection.json");
        AssertFileDoesNotContain("src/MCServerLauncher.Daemon/Remote/EmbeddedDocumentation.cs",
            "postman_collection.json");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void EmbeddedApifoxProject_DocumentsWebSocketActionsAsWebSocketApis()
    {
        var repoRoot = ResolveRepoRoot();
        var apifoxPath = Path.Combine(repoRoot, "src/MCServerLauncher.Daemon/.Resources/Docs/apifox.json");
        using var apifox = JsonDocument.Parse(File.ReadAllText(apifoxPath));
        var root = apifox.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("apifoxProject").GetString());
        Assert.Contains("{{wsUrl}}?token={{token}}", root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/actions/", root.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("protocol/topics/actions.md", root.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("protocol/topics/models.md", root.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("ActionResponse", root.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("SystemInfo", root.GetRawText(), StringComparison.Ordinal);
        AssertApifoxWebSocketActionsMatchActionType(root);
        AssertApifoxProjectIncludesMfpSchemasAndDocs(root);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    public void WsEventPlugin_OnWebSocketClosing_CallsUnsubscribeAllEvents()
    {
        var container = new WsContextContainer();
        var context = container.CreateContext(
            "close-cleanup-client",
            Guid.Empty,
            "*",
            DateTime.UtcNow.AddHours(1));

        context.SubscribeEvent(EventType.DaemonReport, null);
        context.SubscribeEvent(EventType.InstanceLog, new InstanceLogEventMeta
        {
            InstanceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        });

        // Confirm the context is subscribed before close
        Assert.True(context.IsSubscribedEvent(EventType.DaemonReport, null));
        Assert.True(context.IsSubscribedEvent(EventType.InstanceLog, null));

        // Simulate what WsEventPlugin.OnWebSocketClosing does
        context.UnsubscribeAllEvents();

        // After close cleanup, all subscriptions are gone
        Assert.False(context.IsSubscribedEvent(EventType.DaemonReport, null));
        Assert.False(context.IsSubscribedEvent(EventType.InstanceLog, null));
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "CleanupValidation")]
    public void WsEventPluginFile_OnWebSocketClosing_DelegatesToUnsubscribeAllEvents()
    {
        // Source-inspection: confirm OnWebSocketClosing calls UnsubscribeAllEvents
        AssertFileContains(
            "src/MCServerLauncher.Daemon/Remote/WsEventPlugin.cs",
            "this.GetWsContext(webSocket).UnsubscribeAllEvents()");
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

    private static void AssertSchemaReferenceExists(JsonElement schemaReference, JsonElement schemas, string context)
    {
        var reference = schemaReference.GetProperty("$ref").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reference), $"{context} schema reference is empty");
        const string prefix = "#/components/schemas/";
        Assert.StartsWith(prefix, reference, StringComparison.Ordinal);
        var schemaName = reference[prefix.Length..];
        Assert.True(schemas.TryGetProperty(schemaName, out _), $"{context} schema '{schemaName}' is missing");
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

    private static string ReadSourceFile(string relativePath)
    {
        var repoRoot = ResolveRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, relativePath));
    }

    private static void AssertApifoxWebSocketActionsMatchActionType(JsonElement root)
    {
        var expected = Enum.GetNames<ActionType>()
            .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("根目录", root.GetProperty("webSocketCollection")[0].GetProperty("name").GetString());
        Assert.Equal("WebSocket actions", root.GetProperty("webSocketCollection")[0]
            .GetProperty("items")[0]
            .GetProperty("name")
            .GetString());

        var webSocketItems = root.GetProperty("webSocketCollection")
            .EnumerateArray()
            .SelectMany(EnumerateApifoxApiItems)
            .ToArray();

        var actions = webSocketItems
            .Select(item => item.GetProperty("name").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actions);

        foreach (var api in webSocketItems.Select(item => item.GetProperty("api")))
        {
            Assert.Equal("{{wsUrl}}?token={{token}}", api.GetProperty("path").GetString());
            Assert.False(api.TryGetProperty("type", out _), "Native Apifox WebSocket APIs omit api.type");
            Assert.False(api.TryGetProperty("method", out _), "Apifox WebSocket APIs must not masquerade as HTTP requests");
            Assert.True(api.GetProperty("requestBody").TryGetProperty("message", out var message));
            Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
            Assert.Equal(JsonValueKind.Array, api.GetProperty("requestBody").GetProperty("parameters").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("parameters").GetProperty("query").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("parameters").GetProperty("path").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("parameters").GetProperty("cookie").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("parameters").GetProperty("header").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("commonParameters").GetProperty("query").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("commonParameters").GetProperty("body").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("commonParameters").GetProperty("cookie").ValueKind);
            Assert.Equal(JsonValueKind.Array, api.GetProperty("commonParameters").GetProperty("header").ValueKind);
        }
    }

    private static IEnumerable<JsonElement> EnumerateApifoxApiItems(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (item.TryGetProperty("api", out _))
        {
            yield return item;
        }

        if (!item.TryGetProperty("items", out var children))
        {
            yield break;
        }

        foreach (var child in children.EnumerateArray())
        {
            foreach (var apiItem in EnumerateApifoxApiItems(child))
            {
                yield return apiItem;
            }
        }
    }

    private static void AssertApifoxProjectIncludesMfpSchemasAndDocs(JsonElement root)
    {
        var schemaNames = root.GetProperty("schemaCollection")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ActionRequest", schemaNames);
        Assert.Contains("ActionResponse", schemaNames);
        Assert.Contains("EventPacket", schemaNames);
        Assert.Contains("SystemInfo", schemaNames);
        Assert.Contains("DaemonReport", schemaNames);
        Assert.Contains("InstanceConfig", schemaNames);

        var docNames = root.GetProperty("docCollection")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MFP actions", docNames);
        Assert.Contains("MFP models", docNames);
        Assert.Contains("MFP connection", docNames);
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

        public ActionResponse? ProcessAction(ReadOnlyMemory<byte> utf8Json, WsContext ctx)
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
