using System.Collections;
using System.Reflection;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;

namespace MCServerLauncher.ProtocolTests;

internal sealed record LegacyActionRegistrySnapshot(
    IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> SyncHandlers,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> AsyncHandlers)
{
    public int HandlerCount => HandlerMetas.Count;
}

internal static class LegacyActionRegistryHarness
{
    private static readonly Assembly DaemonAssembly = typeof(IActionExecutor).Assembly;
    private static readonly Type RegistryType =
        DaemonAssembly.GetType("MCServerLauncher.Daemon.Remote.Action.AnotherActionHandlerRegistry", throwOnError: true)!;

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

    public static LegacyActionRegistrySnapshot BuildProductionSnapshot()
    {
        return BuildSnapshot(DiscoverProductionHandlerTypes());
    }

    public static LegacyActionRegistrySnapshot BuildSnapshot(params Type[] handlerTypes)
    {
        return BuildSnapshot((IEnumerable<Type>)handlerTypes);
    }

    public static AnotherActionExecutor CreateProductionExecutor(IResolver resolver)
    {
        var snapshot = BuildProductionSnapshot();
        LegacyActionRegistryRuntimeBridge.Install(snapshot);
        return new AnotherActionExecutor(resolver, ActionHandlerRegistryRuntime.Selected);
    }

    public static AnotherActionExecutor CreateExecutor(IResolver resolver, params Type[] handlerTypes)
    {
        var snapshot = BuildSnapshot(handlerTypes);
        LegacyActionRegistryRuntimeBridge.Install(snapshot);
        return new AnotherActionExecutor(resolver, ActionHandlerRegistryRuntime.Selected);
    }

    public static IResolver CreateResolver(SystemInfo? systemInfo = null)
    {
        return systemInfo is null
            ? CreateResolverCore(systemInfoCell: null)
            : CreateResolver(CreatePreloadedSystemInfoCell(systemInfo));
    }

    public static IResolver CreateResolver(IAsyncTimedLazyCell<SystemInfo> systemInfoCell)
    {
        return CreateResolverCore(systemInfoCell);
    }

    private static IResolver CreateResolverCore(IAsyncTimedLazyCell<SystemInfo>? systemInfoCell)
    {
        var services = new ServiceCollection();

        if (systemInfoCell is not null)
        {
            services.AddSingleton<IAsyncTimedLazyCell<SystemInfo>>(systemInfoCell);
        }

        var container = new AspNetCoreContainer(services);
        return container.BuildResolver();
    }

    private static IAsyncTimedLazyCell<SystemInfo> CreatePreloadedSystemInfoCell(SystemInfo systemInfo)
    {
        var cell = new AsyncTimedLazyCell<SystemInfo>(
            () => Task.FromResult(systemInfo),
            TimeSpan.FromHours(1));

        _ = cell.Value.AsTask().GetAwaiter().GetResult();
        return cell;
    }

    public static WsContext CreateContext(string permissions = "*")
    {
        return new WsContext("legacy-action-test-client", Guid.Empty, permissions, DateTime.UtcNow.AddHours(1));
    }

    public static SystemInfo CreateSystemInfo()
    {
        return new SystemInfo(
            new OsInfo("Characterization OS", "x64"),
            new CpuInfo("Characterization Vendor", "Characterization CPU", 8, 0.125d),
            new MemInfo(67_108_864UL, 33_554_432UL),
            new DriveInformation("CharacterizationFS", 1_000_000_000UL, 500_000_000UL));
    }

    public static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static T DeserializeData<T>(ActionResponse response)
    {
        Assert.True(response.Data.HasValue);
        return JsonSerializer.Deserialize<T>(response.Data.Value.GetRawText(), DaemonRpcJsonBoundary.StjOptions)!;
    }

    public static string FormatActionErrorMessage(string cause)
    {
        return cause + Environment.NewLine;
    }

    public static async Task ShutdownExecutorAsync(IActionExecutor executor)
    {
        try
        {
            await executor.ShutdownAsync();
        }
        catch (OperationCanceledException)
        {
            // Legacy executor teardown cancels its internal dataflow blocks.
        }
    }

    private static LegacyActionRegistrySnapshot BuildSnapshot(IEnumerable<Type> handlerTypes)
    {
        ClearRegistry();

        foreach (var handlerType in handlerTypes.OrderBy(type => type.FullName, StringComparer.Ordinal))
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

    private static IEnumerable<Type> DiscoverProductionHandlerTypes()
    {
        return DaemonAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.Namespace == "MCServerLauncher.Daemon.Remote.Action.Handlers"
                && type.GetCustomAttributes(ActionHandlerAttributeType, inherit: false).Length > 0);
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

[CollectionDefinition("LegacyActionRegistryIsolation", DisableParallelization = true)]
public sealed class LegacyActionRegistryIsolationCollection;

file static class LegacyActionRegistryRuntimeBridge
{
    public static void Install(LegacyActionRegistrySnapshot snapshot)
    {
        var runtimeType = typeof(IActionExecutor).Assembly.GetType(
            "MCServerLauncher.Daemon.Remote.Action.ActionHandlerRegistryRuntime",
            throwOnError: true)!;

        var modeType = typeof(IActionExecutor).Assembly.GetType(
            "MCServerLauncher.Daemon.Remote.Action.ActionHandlerRegistryMode",
            throwOnError: true)!;

        var snapshotType = typeof(IActionExecutor).Assembly.GetType(
            "MCServerLauncher.Daemon.Remote.Action.ActionHandlerRegistrySnapshot",
            throwOnError: true)!;

        var legacyMode = Enum.Parse(modeType, "Legacy");
        var registrySnapshot = Activator.CreateInstance(
            snapshotType,
            legacyMode,
            snapshot.HandlerMetas,
            snapshot.SyncHandlers,
            snapshot.AsyncHandlers);

        var selectedProperty = runtimeType.GetProperty(
            "Selected",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(selectedProperty);

        var backingField = runtimeType.GetField(
            "_selected",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(backingField);
        backingField!.SetValue(null, registrySnapshot);
    }
}
