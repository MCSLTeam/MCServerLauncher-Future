using System.Collections;
using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;

namespace MCServerLauncher.Benchmarks.Infrastructure;

internal sealed record LegacyActionRegistrySnapshot(
    IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> SyncHandlers,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> AsyncHandlers)
{
    public int HandlerCount => HandlerMetas.Count;
}

internal static class DaemonActionReflectionBridge
{
    private static readonly Assembly DaemonAssembly = typeof(IActionExecutor).Assembly;
    private static readonly Type RegistryType =
        DaemonAssembly.GetType("MCServerLauncher.Daemon.Remote.Action.AnotherActionHandlerRegistry", throwOnError: true)!;

    private static readonly Type ActionHandlerRegistryRuntimeType =
        DaemonAssembly.GetType("MCServerLauncher.Daemon.Remote.Action.ActionHandlerRegistryRuntime", throwOnError: true)!;

    private static readonly Type ActionHandlerAttributeType =
        DaemonAssembly.GetType("MCServerLauncher.Daemon.Remote.Action.ActionHandlerAttribute", throwOnError: true)!;

    private static readonly MethodInfo LoadHandlerFromTypeMethod =
        RegistryType.GetMethod("LoadHandlerFromType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(RegistryType.FullName, "LoadHandlerFromType");

    private static readonly FieldInfo HandlerMetaField =
        RegistryType.GetField("HandlerMeta", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(RegistryType.FullName, "HandlerMeta");

    private static readonly FieldInfo SyncHandlersField =
        RegistryType.GetField("Handlers", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(RegistryType.FullName, "Handlers");

    private static readonly FieldInfo AsyncHandlersField =
        RegistryType.GetField("AsyncHandlers", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(RegistryType.FullName, "AsyncHandlers");

    private static readonly PropertyInfo HandlerMetaMapProperty =
        RegistryType.GetProperty("HandlerMetaMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMemberException(RegistryType.FullName, "HandlerMetaMap");

    private static readonly PropertyInfo SyncHandlerMapProperty =
        RegistryType.GetProperty("SyncHandlerMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMemberException(RegistryType.FullName, "SyncHandlerMap");

    private static readonly PropertyInfo AsyncHandlerMapProperty =
        RegistryType.GetProperty("AsyncHandlerMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMemberException(RegistryType.FullName, "AsyncHandlerMap");

    private static readonly MethodInfo CreateSelectedMethod =
        ActionHandlerRegistryRuntimeType.GetMethod(
            "CreateSelected",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(bool?)],
            modifiers: null)
        ?? throw new MissingMethodException(ActionHandlerRegistryRuntimeType.FullName, "CreateSelected");

    public static LegacyActionRegistrySnapshot BuildLegacyRegistry()
    {
        ClearRegistry();

        foreach (var handlerType in DiscoverActionHandlerTypes())
        {
            LoadHandlerFromTypeMethod.Invoke(null, [handlerType]);
        }

        var handlerMetas =
            new Dictionary<ActionType, ActionHandlerMeta>(
                (IReadOnlyDictionary<ActionType, ActionHandlerMeta>)HandlerMetaMapProperty.GetValue(null)!);

        var syncHandlers =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>(
                (IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>)
                SyncHandlerMapProperty.GetValue(null)!);

        var asyncHandlers =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>(
                (IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>)
                AsyncHandlerMapProperty.GetValue(null)!);

        return new LegacyActionRegistrySnapshot(handlerMetas, syncHandlers, asyncHandlers);
    }

    public static int BuildGeneratedRegistry()
    {
        var snapshot = CreateSelectedMethod.Invoke(null, [(bool?)true]);
        if (snapshot is null)
            throw new InvalidOperationException("Generated registry snapshot creation unexpectedly returned null.");

        var handlerMetasProperty = snapshot.GetType().GetProperty(
            "HandlerMetas",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (handlerMetasProperty?.GetValue(snapshot) is not IReadOnlyDictionary<ActionType, ActionHandlerMeta> handlerMetas)
            throw new InvalidOperationException("Generated registry snapshot did not expose the expected handler metadata map.");

        return handlerMetas.Count;
    }

    private static IEnumerable<Type> DiscoverActionHandlerTypes()
    {
        return DaemonAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.GetCustomAttributes(ActionHandlerAttributeType, inherit: false).Length > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    private static void ClearRegistry()
    {
        ClearDictionary(HandlerMetaField.GetValue(null));
        ClearDictionary(SyncHandlersField.GetValue(null));
        ClearDictionary(AsyncHandlersField.GetValue(null));
    }

    private static void ClearDictionary(object? value)
    {
        switch (value)
        {
            case IDictionary dictionary:
                dictionary.Clear();
                return;
            case not null:
                value.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance)?.Invoke(value, null);
                return;
            default:
                throw new InvalidOperationException("Action registry dictionary was unexpectedly null.");
        }
    }
}

internal sealed class DaemonDispatchBenchmarkContext
{
    private static readonly Guid PingRequestId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GetSystemInfoRequestId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private DaemonDispatchBenchmarkContext(
        IActionExecutor executor,
        IResolver resolver,
        WsContext wsContext,
        LegacyActionRegistrySnapshot registry,
        JsonElement emptyParameters)
    {
        Executor = executor;
        Resolver = resolver;
        WsContext = wsContext;
        Registry = registry;
        EmptyParameters = emptyParameters;
        PingRequest = new ActionRequest
        {
            ActionType = ActionType.Ping,
            Parameter = emptyParameters,
            Id = PingRequestId
        };
        GetSystemInfoRequest = new ActionRequest
        {
            ActionType = ActionType.GetSystemInfo,
            Parameter = emptyParameters,
            Id = GetSystemInfoRequestId
        };
    }

    public IActionExecutor Executor { get; }
    public IResolver Resolver { get; }
    public WsContext WsContext { get; }
    public LegacyActionRegistrySnapshot Registry { get; }
    public JsonElement EmptyParameters { get; }
    public ActionRequest PingRequest { get; }
    public ActionRequest GetSystemInfoRequest { get; }

    public static DaemonDispatchBenchmarkContext Create()
    {
        var registry = DaemonActionReflectionBridge.BuildLegacyRegistry();
        var services = new ServiceCollection();

        var systemInfoCell = new AsyncTimedLazyCell<SystemInfo>(
            () => Task.FromResult(CreateSystemInfo()),
            TimeSpan.FromHours(1));

        _ = systemInfoCell.Value.AsTask().GetAwaiter().GetResult();

        services.AddSingleton<IAsyncTimedLazyCell<SystemInfo>>(systemInfoCell);

        var container = new AspNetCoreContainer(services);
        var resolver = container.BuildResolver();
        var executor = new SnapshotActionExecutor(registry.HandlerMetas);
        var context = new WsContext("benchmark-client", Guid.Empty, "*", DateTime.UtcNow.AddHours(1));
        var emptyParameters = BenchmarkFixtureLoader.ParseElement("{}");

        return new DaemonDispatchBenchmarkContext(executor, resolver, context, registry, emptyParameters);
    }

    private static SystemInfo CreateSystemInfo()
    {
        return new SystemInfo(
            new OsInfo("Benchmark OS", "x64"),
            new CpuInfo("Benchmark Vendor", "Benchmark CPU", 8, 0.12),
            new MemInfo(33_554_432UL, 16_777_216UL),
            new DriveInformation("BenchmarkFS", 1_000_000_000_000UL, 500_000_000_000UL));
    }

    private sealed class SnapshotActionExecutor(IReadOnlyDictionary<ActionType, ActionHandlerMeta> handlerMetas) : IActionExecutor
    {
        public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; } =
            new Dictionary<ActionType, ActionHandlerMeta>(handlerMetas);

        public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
            SyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>();

        public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
            AsyncHandlers { get; } =
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>();

        public ActionResponse? ProcessAction(string text, WsContext ctx)
        {
            throw new NotSupportedException("Benchmark executor only supports CheckHandler metadata access.");
        }

        public Task ShutdownAsync()
        {
            return Task.CompletedTask;
        }
    }
}
