using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Action.Handlers;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        //   2. UseWebSocket (with exact /api/v2)
        //   3. UseWebSocket (with branch-migration /api/v1)
        //   4. TouchSocketV2TransportPlugin
        //   5. WsBasePlugin
        //   6. WsActionPlugin
        //   7. WsEventPlugin
        //   8. WsExpirationPlugin
        //   9. UseDefaultHttpServicePlugin
        var configPluginsStart = source.IndexOf("internal static void ConfigurePlugins", StringComparison.Ordinal);
        Assert.True(configPluginsStart >= 0, "ConfigurePlugins method not found");

        var configPluginsBody = source[configPluginsStart..];
        Assert.DoesNotContain("Add<FileSystemWatcherPlugin>", configPluginsBody, StringComparison.Ordinal);
        var order = new[]
        {
            "Add<HttpPlugin>",
            "SetUrl(TouchSocketV2TransportPlugin.Endpoint)",
            "SetUrl(\"/api/v1\")",
            "Add<TouchSocketV2TransportPlugin>",
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
        Assert.Contains(".SetUrl(\"/api/v1\")", source, StringComparison.Ordinal);
        Assert.Contains("v1Options.VerifyConnection = WsVerifyHandler.VerifyHandler", source, StringComparison.Ordinal);
        Assert.Contains(".SetAutoPong(true)", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonServiceComposition_ConfiguresExactApiV2WithoutFallback()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs");
        Assert.Contains(".SetUrl(TouchSocketV2TransportPlugin.Endpoint)", source, StringComparison.Ordinal);
        Assert.Contains("v2Options.VerifyConnection = WsVerifyHandler.VerifyV2Handler", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compat", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void WsVerifyHandler_V2UsesExactPathAndNonLeakingStatuses()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Remote/WsVerifyHandler.cs");
        Assert.Contains("StringComparer.Ordinal.Equals(context.Request.RelativeURL, \"/api/v2\")", source,
            StringComparison.Ordinal);
        Assert.Contains("SetStatus(401, \"Unauthorized\")", source, StringComparison.Ordinal);
        Assert.Contains("SetStatus(403, \"Daemon rejected\")", source, StringComparison.Ordinal);
        Assert.Contains("SetStatus(500, \"Internal Server Error\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus(500, e.Message)", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public void ApplicationStartupLog_PointsToApifoxDocsConnectUrlsOnly()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("var endpoints = GetRemoteEndpoints(host.HttpService);", source, StringComparison.Ordinal);
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
    public void EmbeddedApifoxProject_MatchesFrozenV2Catalog()
    {
        var repoRoot = ResolveRepoRoot();
        var apifoxPath = Path.Combine(repoRoot, "src/MCServerLauncher.Daemon/.Resources/Docs/apifox.json");
        var json = File.ReadAllText(apifoxPath);
        using var apifox = JsonDocument.Parse(json);
        var root = apifox.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("apifoxProject").GetString());
        Assert.Contains("{{wsUrl}}?token={{token}}", root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol/topics/", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionType", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MFP", json, StringComparison.Ordinal);
        AssertApifoxProjectMatchesFrozenCatalog(root);
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    [Trait("Category", "Documentation")]
    public async Task GeneratedApifoxProject_PassesDeterministicCheckGate()
    {
        var repoRoot = ResolveRepoRoot();
        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = testOutputDirectory.Parent?.Name ?? "Release";
        var protocolDocsDll = Path.Combine(
            repoRoot,
            "tools",
            "MCServerLauncher.ProtocolDocs",
            "bin",
            configuration,
            "net10.0",
            "MCServerLauncher.ProtocolDocs.dll");
        Assert.True(
            File.Exists(protocolDocsDll),
            $"Expected the ProtocolDocs project output at '{protocolDocsDll}'. Build the test project before running with --no-build.");

        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(dotnetHost) ? "dotnet" : dotnetHost,
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(protocolDocsDll);
        startInfo.ArgumentList.Add("--check");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start the protocol documentation generator.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var (output, error, failures) = await RunDocumentationGateProcessAsync(
            cancellationToken => process.WaitForExitAsync(cancellationToken),
            () => process.HasExited,
            () => process.Kill(entireProcessTree: true),
            cancellationToken => process.WaitForExitAsync(cancellationToken),
            standardOutput,
            standardError,
            timeout.Token);

        if (failures.Count > 0)
            throw new AggregateException("Protocol documentation --check or its cleanup failed.", failures);

        Assert.True(
            process.ExitCode == 0,
            $"Protocol documentation --check failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    [Fact]
    [Trait("Category", "Inbound")]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "Documentation")]
    public async Task DocumentationGateCleanup_KillFailureStillWaitsAndObservesStreams()
    {
        var cleanupWaited = false;
        var killFailure = new InvalidOperationException("kill failed");
        var outputFailure = new IOException("stdout failed");

        var (_, error, failures) = await RunDocumentationGateProcessAsync(
            _ => Task.FromException(new OperationCanceledException()),
            () => false,
            () => throw killFailure,
            _ =>
            {
                cleanupWaited = true;
                return Task.CompletedTask;
            },
            Task.FromException<string>(outputFailure),
            Task.FromResult("stderr observed"),
            CancellationToken.None);

        Assert.True(cleanupWaited);
        Assert.Equal("stderr observed", error);
        Assert.Contains(failures, exception => exception is TimeoutException);
        Assert.Contains(killFailure, failures);
        Assert.Contains(outputFailure, failures);
    }

    private static async Task<(string? Output, string? Error, List<Exception> Failures)>
        RunDocumentationGateProcessAsync(
            Func<CancellationToken, Task> waitForExitAsync,
            Func<bool> hasExited,
            Action killProcessTree,
            Func<CancellationToken, Task> waitForKilledProcessAsync,
            Task<string> standardOutput,
            Task<string> standardError,
            CancellationToken timeoutToken)
    {
        var failures = new List<Exception>();
        string? output = null;
        string? error = null;
        try
        {
            await waitForExitAsync(timeoutToken);
        }
        catch (OperationCanceledException exception)
        {
            failures.Add(new TimeoutException("Protocol documentation --check exceeded two minutes.", exception));
            try
            {
                if (!hasExited())
                    killProcessTree();
            }
            catch (Exception) when (hasExited())
            {
            }
            catch (Exception killException)
            {
                failures.Add(killException);
            }

            try
            {
                using var killTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await waitForKilledProcessAsync(killTimeout.Token);
            }
            catch (Exception cleanupException)
            {
                failures.Add(cleanupException);
            }
        }
        finally
        {
            try
            {
                output = await standardOutput.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception outputException)
            {
                failures.Add(outputException);
            }

            try
            {
                error = await standardError.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception errorException)
            {
                failures.Add(errorException);
            }
        }

        return (output, error, failures);
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

    private static void AssertApifoxProjectMatchesFrozenCatalog(JsonElement root)
    {
        var expectedMethods = BuiltInProtocolDefinitions.Rpcs
            .Select(descriptor => descriptor.Method.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var descriptorsByMethod = BuiltInProtocolDefinitions.Rpcs.ToDictionary(
            descriptor => descriptor.Method.Value,
            StringComparer.Ordinal);
        var expectedCategories = BuiltInProtocolDefinitions.Rpcs
            .Select(descriptor => descriptor.Documentation!.Category)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var roots = root.GetProperty("webSocketCollection").EnumerateArray().ToArray();
        Assert.Single(roots);
        Assert.Equal("root", roots[0].GetProperty("name").GetString());
        Assert.False(roots[0].TryGetProperty("api", out _));

        var folders = roots[0].GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedCategories, folders.Select(folder => folder.GetProperty("name").GetString()).ToArray());
        Assert.All(folders, folder =>
        {
            Assert.False(folder.TryGetProperty("api", out _));
            Assert.Equal(JsonValueKind.Array, folder.GetProperty("items").ValueKind);
        });

        var apiItems = folders
            .SelectMany(folder => folder.GetProperty("items").EnumerateArray())
            .ToArray();
        Assert.All(apiItems, item =>
        {
            Assert.True(item.TryGetProperty("api", out _));
            Assert.False(item.TryGetProperty("items", out _));
        });
        Assert.Equal(
            expectedMethods,
            apiItems.Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal).ToArray());

        foreach (var item in apiItems)
        {
            var method = item.GetProperty("name").GetString()!;
            var descriptor = descriptorsByMethod[method];
            var api = item.GetProperty("api");
            Assert.Equal("{{wsUrl}}?token={{token}}", api.GetProperty("path").GetString());
            Assert.False(api.TryGetProperty("type", out _), "Native Apifox WebSocket APIs omit api.type");
            Assert.False(api.TryGetProperty("method", out _), "Apifox WebSocket APIs must not masquerade as HTTP requests");

            var parameters = api.GetProperty("parameters");
            var query = parameters.GetProperty("query").EnumerateArray().ToArray();
            Assert.Single(query);
            Assert.Equal("token", query[0].GetProperty("name").GetString());
            Assert.Equal("{{token}}", query[0].GetProperty("defaultValue").GetString());
            Assert.Equal(JsonValueKind.Array, parameters.GetProperty("path").ValueKind);
            Assert.Equal(JsonValueKind.Array, parameters.GetProperty("cookie").ValueKind);
            Assert.Equal(JsonValueKind.Array, parameters.GetProperty("header").ValueKind);

            var requestBody = api.GetProperty("requestBody");
            Assert.Equal(JsonValueKind.Array, requestBody.GetProperty("parameters").ValueKind);
            using var message = JsonDocument.Parse(requestBody.GetProperty("message").GetString()!);
            Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(method, message.RootElement.GetProperty("method").GetString());
            Assert.Equal(JsonValueKind.Object, message.RootElement.GetProperty("params").ValueKind);
            Assert.Equal(JsonValueKind.String, message.RootElement.GetProperty("id").ValueKind);

            var description = api.GetProperty("description").GetString()!;
            Assert.Contains(descriptor.Documentation!.RequestSchemaId, description, StringComparison.Ordinal);
            Assert.Contains(descriptor.Documentation.ResultSchemaId, description, StringComparison.Ordinal);
        }

        var expectedEvents = BuiltInProtocolDefinitions.Events
            .Select(descriptor => descriptor.Name.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var eventDocs = root.GetProperty("docCollection")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .ToArray();
        Assert.Equal(
            expectedEvents,
            eventDocs.Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal).ToArray());
        var eventDescriptors = BuiltInProtocolDefinitions.Events.ToDictionary(
            descriptor => descriptor.Name.Value,
            StringComparer.Ordinal);
        foreach (var eventDoc in eventDocs)
        {
            var descriptor = eventDescriptors[eventDoc.GetProperty("name").GetString()!];
            var content = eventDoc.GetProperty("content").GetString()!;
            Assert.Contains(descriptor.Documentation!.DataSchemaId, content, StringComparison.Ordinal);
            if (descriptor.Documentation.MetaSchemaId is not null)
            {
                Assert.Contains(descriptor.Documentation.MetaSchemaId, content, StringComparison.Ordinal);
            }
        }

        var expectedSchemas = BuiltInProtocolDefinitions.Rpcs
            .SelectMany(descriptor => new[]
            {
                descriptor.Documentation!.RequestSchemaId,
                descriptor.Documentation.ResultSchemaId
            })
            .Concat(BuiltInProtocolDefinitions.Events.Select(descriptor => descriptor.Documentation!.DataSchemaId))
            .Concat(BuiltInProtocolDefinitions.Events
                .Select(descriptor => descriptor.Documentation!.MetaSchemaId)
                .Where(schemaId => schemaId is not null)
                .Select(schemaId => schemaId!))
            .Append("mcsl.schema.json-rpc.error.data")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var schemaItems = root.GetProperty("schemaCollection")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .ToArray();
        Assert.Equal(
            expectedSchemas,
            schemaItems.Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal).ToArray());
        foreach (var schemaItem in schemaItems)
        {
            var name = schemaItem.GetProperty("name").GetString()!;
            Assert.Equal($"#/definitions/{name}", schemaItem.GetProperty("id").GetString());
            Assert.Equal(
                name,
                schemaItem.GetProperty("schema").GetProperty("jsonSchema").GetProperty("$id").GetString());
        }

        var errorDataSchema = schemaItems.Single(
                item => item.GetProperty("name").GetString() == "mcsl.schema.json-rpc.error.data")
            .GetProperty("schema")
            .GetProperty("jsonSchema");
        var runtimeErrorDataSchema = BuiltInProtocolDefinitions.CreateDocument("v2")
            .Components
            .GetProperty("schemas")
            .GetProperty("mcsl.schema.json-rpc.error.data");
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(runtimeErrorDataSchema.GetRawText()),
            JsonNode.Parse(errorDataSchema.GetRawText())));
        Assert.Equal(
            ["daemon_error_kind", "correlation_id"],
            errorDataSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(errorDataSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["correlation_id", "daemon_error_code", "daemon_error_kind", "details", "execution_owner", "origin_plugin"],
            errorDataSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        var errorDataProperties = errorDataSchema.GetProperty("properties");
        Assert.Equal("string", errorDataProperties.GetProperty("daemon_error_code").GetProperty("type").GetString());
        Assert.Equal(
            ["validation", "not_found", "conflict", "permission", "storage", "transport", "internal"],
            errorDataProperties.GetProperty("daemon_error_kind").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("object", errorDataProperties.GetProperty("origin_plugin").GetProperty("type").GetString());
        Assert.Equal("object", errorDataProperties.GetProperty("execution_owner").GetProperty("type").GetString());
        Assert.Equal(
            "null",
            errorDataProperties.GetProperty("details").GetProperty("not").GetProperty("type").GetString());

        var catalogChangeDescriptor = BuiltInProtocolDefinitions.Events.Single(
            descriptor => descriptor.Name.Value == "mcsl.event.instance.catalog.changed");
        var runtimeCatalogChangeSchema = BuiltInProtocolDefinitions.CreateDocument("2.0.0")
            .Events.Single(@event => @event.Name == catalogChangeDescriptor.Name.Value)
            .Data.Schema!.Value;
        var apifoxCatalogChangeSchema = schemaItems.Single(
                item => item.GetProperty("name").GetString() == catalogChangeDescriptor.Documentation!.DataSchemaId)
            .GetProperty("schema")
            .GetProperty("jsonSchema");
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(runtimeCatalogChangeSchema.GetRawText()),
            JsonNode.Parse(apifoxCatalogChangeSchema.GetRawText())));
        Assert.DoesNotContain(
            "snapshot",
            apifoxCatalogChangeSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, apifoxCatalogChangeSchema.GetProperty("allOf").GetArrayLength());

        var environment = Assert.Single(root.GetProperty("environments").EnumerateArray());
        Assert.Equal(
            "ws://127.0.0.1:11452/api/v2",
            environment.GetProperty("websocketBaseUrls").GetProperty("mcsl-daemon-protocol").GetString());
        var variables = environment.GetProperty("variables")
            .EnumerateArray()
            .ToDictionary(variable => variable.GetProperty("name").GetString()!, StringComparer.Ordinal);
        Assert.Equal("ws://127.0.0.1:11452/api/v2", variables["wsUrl"].GetProperty("value").GetString());
        Assert.Equal(string.Empty, variables["token"].GetProperty("value").GetString());
        Assert.Equal(string.Empty, variables["token"].GetProperty("defaultValue").GetString());

        var ids = EnumerateApifoxEntityIds(root).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<string> EnumerateApifoxEntityIds(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                yield return id.GetString()!;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var nestedId in EnumerateApifoxEntityIds(property.Value))
                {
                    yield return nestedId;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nestedId in EnumerateApifoxEntityIds(item))
                {
                    yield return nestedId;
                }
            }
        }
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
