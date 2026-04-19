using System.Reflection;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.DaemonClient.Serialization;
using MCServerLauncher.DaemonClient.WebSocketPlugin;
using MCServerLauncher.ProtocolTests.Helpers;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using Xunit;

namespace MCServerLauncher.ProtocolTests;

[Collection("RuntimeSwitchIsolation")]
public class ActionRegistrySelectionTests
{
    private static readonly Guid PingRequestId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly Type AppConfigType = typeof(AppConfig);
    private static readonly Type? ActionHandlerRegistryRuntimeType =
        Type.GetType("MCServerLauncher.Daemon.Remote.Action.ActionHandlerRegistryRuntime, MCServerLauncher.Daemon");

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void ConfigSeam_MissingUseGeneratedActionRegistry_DeserializesNullAndDoesNotSerializeTheFlag()
    {
        var json =
            """
            {
              "port": 11452,
              "secret": "0123456789abcdef0123456789abcdef",
              "main_token": "fedcba9876543210fedcba9876543210",
              "file_download_sessions": 5,
              "verbose": true
            }
            """;

        var config = JsonSerializer.Deserialize(json, AppConfigType, DaemonPersistenceJsonBoundary.StjOptions);
        Assert.NotNull(config);

        var property = AppConfigType.GetProperty(
            "UseGeneratedActionRegistry",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        Assert.Null(property!.GetValue(config));

        using var serialized = JsonDocument.Parse(
            JsonSerializer.Serialize(config, AppConfigType, DaemonPersistenceJsonBoundary.StjWriteIndentedOptions));

        Assert.False(serialized.RootElement.TryGetProperty("use_generated_action_registry", out _));
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void SelectionRule_ExplicitTrue_SelectsGeneratedRegistry()
    {
        var registry = CreateSelectedRegistry(useGeneratedActionRegistry: true);
        var legacySnapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();

        Assert.Equal("Generated", GetRegistryMode(registry));
        Assert.Equal(Order(legacySnapshot.HandlerMetas.Keys), GetRegisteredActions(registry));
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void SelectionRule_ExplicitFalse_SelectsLegacyRegistry()
    {
        var registry = CreateSelectedRegistry(useGeneratedActionRegistry: false);
        var legacySnapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();

        Assert.Equal("Legacy", GetRegistryMode(registry));
        Assert.Equal(Order(legacySnapshot.HandlerMetas.Keys), GetRegisteredActions(registry));
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void SelectionRule_Null_SelectsGeneratedRegistryByDefaultForCurrentPhase()
    {
        var registry = CreateSelectedRegistry(useGeneratedActionRegistry: null);
        var legacySnapshot = LegacyActionRegistryHarness.BuildProductionSnapshot();

        Assert.Equal("Generated", GetRegistryMode(registry));
        Assert.Equal(Order(legacySnapshot.HandlerMetas.Keys), GetRegisteredActions(registry));
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void Initialize_Null_StoresGeneratedSnapshotAsSelected()
    {
        ResetRuntimeRegistry();

        try
        {
            var initialized = InitializeRuntimeRegistry(useGeneratedActionRegistry: null);
            var selected = GetSelectedRuntimeRegistry();

            Assert.Same(initialized, selected);
            Assert.Equal("Generated", GetRegistryMode(initialized));
            Assert.Equal("Generated", GetRegistryMode(selected));
        }
        finally
        {
            ResetRuntimeRegistry();
        }
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public void Reset_ClearsSelectedRegistry()
    {
        ResetRuntimeRegistry();

        try
        {
            _ = InitializeRuntimeRegistry(useGeneratedActionRegistry: true);
            Assert.Equal("Generated", GetRegistryMode(GetSelectedRuntimeRegistry()));

            ResetRuntimeRegistry();

            Assert.Throws<TargetInvocationException>(() => _ = GetSelectedRuntimeRegistry());
        }
        finally
        {
            ResetRuntimeRegistry();
        }
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public async Task ExecutorConstruction_ProvidedRegistrySnapshot_TakesPrecedenceOverRuntimeSelection()
    {
        ActionHandlerRegistryRuntime.Reset();

        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var selectedGeneratedRegistry = ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        var explicitLegacyRegistry = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry: false);

        Assert.NotSame(selectedGeneratedRegistry.HandlerMetas, explicitLegacyRegistry.HandlerMetas);
        Assert.NotSame(selectedGeneratedRegistry.SyncHandlers, explicitLegacyRegistry.SyncHandlers);
        Assert.NotSame(selectedGeneratedRegistry.AsyncHandlers, explicitLegacyRegistry.AsyncHandlers);

        var executor = new AnotherActionExecutor(resolver, explicitLegacyRegistry);

        try
        {
            Assert.Same(explicitLegacyRegistry.HandlerMetas, executor.HandlerMetas);
            Assert.Same(explicitLegacyRegistry.SyncHandlers, executor.SyncHandlers);
            Assert.Same(explicitLegacyRegistry.AsyncHandlers, executor.AsyncHandlers);
            Assert.NotSame(ActionHandlerRegistryRuntime.Selected.HandlerMetas, executor.HandlerMetas);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
            ActionHandlerRegistryRuntime.Reset();
        }
    }

    [Fact]
    [Trait("Category", "ActionRegistrySelection")]
    public async Task GeneratedSelection_PingAction_RoundTripsThroughRealExecutorAndClientParser()
    {
        ActionHandlerRegistryRuntime.Reset();

        var selectedRegistry = ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        var resolver = LegacyActionRegistryHarness.CreateResolver();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var executor = new AnotherActionExecutor(resolver, selectedRegistry);

        try
        {
            var wireJson = await ExecutePingThroughWsActionPluginAsync(executor);
            var parsedResponse = WsReceivedPlugin.ParseActionResponse(wireJson);

            Assert.Equal(ActionRequestStatus.Ok, parsedResponse.RequestStatus);
            Assert.Equal(ActionRetcode.Ok.Code, parsedResponse.Retcode);
            Assert.Equal(ActionRetcode.Ok.Message, parsedResponse.Message);
            Assert.Equal(PingRequestId, parsedResponse.Id);
            Assert.True(parsedResponse.Data.HasValue);

            var payload = JsonSerializer.Deserialize<PingResult>(
                parsedResponse.Data.Value.GetRawText(),
                DaemonClientRpcJsonBoundary.StjOptions);

            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.NotNull(payload);
            Assert.InRange(payload!.Time, before, after);
        }
        finally
        {
            await LegacyActionRegistryHarness.ShutdownExecutorAsync(executor);
            ActionHandlerRegistryRuntime.Reset();
        }
    }

    private static object CreateSelectedRegistry(bool? useGeneratedActionRegistry)
    {
        Assert.NotNull(ActionHandlerRegistryRuntimeType);

        var createSelectedMethod = ActionHandlerRegistryRuntimeType!.GetMethod(
            "CreateSelected",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool?)],
            modifiers: null);

        Assert.NotNull(createSelectedMethod);

        var registry = createSelectedMethod!.Invoke(null, [useGeneratedActionRegistry]);
        Assert.NotNull(registry);
        return registry!;
    }

    private static object InitializeRuntimeRegistry(bool? useGeneratedActionRegistry)
    {
        Assert.NotNull(ActionHandlerRegistryRuntimeType);

        var initializeMethod = ActionHandlerRegistryRuntimeType!.GetMethod(
            "Initialize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool?)],
            modifiers: null);

        Assert.NotNull(initializeMethod);

        var registry = initializeMethod!.Invoke(null, [useGeneratedActionRegistry]);
        Assert.NotNull(registry);
        return registry!;
    }

    private static object GetSelectedRuntimeRegistry()
    {
        Assert.NotNull(ActionHandlerRegistryRuntimeType);

        var selectedProperty = ActionHandlerRegistryRuntimeType!.GetProperty(
            "Selected",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(selectedProperty);

        return selectedProperty!.GetValue(null) ?? throw new InvalidOperationException("Selected registry was null.");
    }

    private static void ResetRuntimeRegistry()
    {
        Assert.NotNull(ActionHandlerRegistryRuntimeType);

        var resetMethod = ActionHandlerRegistryRuntimeType!.GetMethod(
            "Reset",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(resetMethod);
        resetMethod!.Invoke(null, null);
    }

    private static string GetRegistryMode(object registry)
    {
        var modeProperty = registry.GetType().GetProperty(
            "Mode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(modeProperty);
        var mode = modeProperty!.GetValue(registry);
        Assert.NotNull(mode);
        return mode!.ToString()!;
    }

    private static ActionType[] GetRegisteredActions(object registry)
    {
        var handlerMetasProperty = registry.GetType().GetProperty(
            "HandlerMetas",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(handlerMetasProperty);

        var handlerMetas = Assert.IsAssignableFrom<IReadOnlyDictionary<ActionType, ActionHandlerMeta>>(
            handlerMetasProperty!.GetValue(registry));

        return Order(handlerMetas.Keys);
    }

    private static ActionType[] Order(IEnumerable<ActionType> actions)
    {
        return actions.OrderBy(action => (int)action).ToArray();
    }

    private static async Task<string> ExecutePingThroughWsActionPluginAsync(IActionExecutor executor)
    {
        const string clientId = "action-registry-selection-client";

        var container = new WsContextContainer();
        container.CreateContext(clientId, Guid.Empty, "*", DateTime.UtcNow.AddHours(1));

        var plugin = new WsActionPlugin(executor, CreateProxy<IHttpService>((_, _) => null), container);
        string? sent = null;

        var sessionClient = CreateProxy<IHttpSessionClient>((method, _) =>
            method.Name switch
            {
                "get_Id" => clientId,
                _ => GetDefaultReturnValue(method.ReturnType)
            });

        var webSocket = CreateProxy<IWebSocket>((method, args) =>
            method.Name switch
            {
                "get_Client" => sessionClient,
                "SendAsync" when method.GetParameters().Length > 0
                                  && method.GetParameters()[0].ParameterType == typeof(WSDataFrame)
                    => CaptureSentFrame((WSDataFrame)args![0]!, value => sent = value),
                _ => GetDefaultReturnValue(method.ReturnType)
            });

        var requestJson = JsonSerializer.Serialize(
            new ActionRequest
            {
                ActionType = ActionType.Ping,
                Parameter = LegacyActionRegistryHarness.ParseElement("{}"),
                Id = PingRequestId
            },
            DaemonRpcJsonBoundary.StjOptions);

        var frame = new WSDataFrame(Encoding.UTF8.GetBytes(requestJson))
        {
            Opcode = WSDataType.Text,
            FIN = true
        };

        await plugin.OnWebSocketReceived(webSocket, new WSDataFrameEventArgs(frame));

        Assert.NotNull(sent);
        return sent!;
    }

    private static Task CaptureSentFrame(WSDataFrame frame, Action<string> setter)
    {
        setter(Encoding.UTF8.GetString(frame.PayloadData.Span));
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

    private class InterfaceDispatchProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return Handler(targetMethod ?? throw new MissingMethodException("DispatchProxy target method was null."), args);
        }
    }
}
