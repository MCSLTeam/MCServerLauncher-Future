using System.Linq.Expressions;
using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;

namespace MCServerLauncher.Daemon.Remote.Action;

public enum EActionHandlerType
{
    Sync,
    Async
}

public class ActionHandlerMeta(IMatchable permission, EActionHandlerType type)
{
    public IMatchable Permission { get; } = permission;
    public EActionHandlerType Type { get; } = type;
}

internal enum ActionHandlerRegistryMode
{
    Legacy,
    Generated
}

internal interface IActionHandlerRegistry
{
    ActionHandlerRegistryMode Mode { get; }

    IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

    IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; }

    IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers { get; }
}

internal sealed class ActionHandlerRegistrySnapshot(
    ActionHandlerRegistryMode mode,
    IReadOnlyDictionary<ActionType, ActionHandlerMeta> handlerMetas,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        syncHandlers,
    IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        asyncHandlers)
    : IActionHandlerRegistry
{
    public ActionHandlerRegistryMode Mode { get; } = mode;

    public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; } = handlerMetas;

    public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; } = syncHandlers;

    public IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers { get; } = asyncHandlers;
}

internal static class ActionHandlerRegistryRuntime
{
    private static ActionHandlerRegistrySnapshot? _selected;

    public static ActionHandlerRegistrySnapshot Selected =>
        _selected ?? throw new InvalidOperationException("Action handler registry has not been initialized.");

    public static ActionHandlerRegistrySnapshot CreateSelected(bool? useGeneratedActionRegistry)
    {
        return useGeneratedActionRegistry switch
        {
            true => CreateGenerated(),
            false => CreateLegacy(),
            null => CreateGenerated()
        };
    }

    public static ActionHandlerRegistrySnapshot Initialize(bool? useGeneratedActionRegistry)
    {
        _selected = CreateSelected(useGeneratedActionRegistry);
        return _selected;
    }

    public static void Reset()
    {
        _selected = null;
    }

    private static ActionHandlerRegistrySnapshot CreateLegacy()
    {
        AnotherActionHandlerRegistry.Reset();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            AnotherActionHandlerRegistry.LoadHandlerFromType(type);
        }

        return new ActionHandlerRegistrySnapshot(
            ActionHandlerRegistryMode.Legacy,
            new Dictionary<ActionType, ActionHandlerMeta>(AnotherActionHandlerRegistry.HandlerMetaMap),
            new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>(
                AnotherActionHandlerRegistry.SyncHandlerMap),
            new Dictionary<ActionType,
                Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>(
                AnotherActionHandlerRegistry.AsyncHandlerMap));
    }

    private static ActionHandlerRegistrySnapshot CreateGenerated()
    {
        return new ActionHandlerRegistrySnapshot(
            ActionHandlerRegistryMode.Generated,
            GeneratedActionHandlerRegistryArtifacts.CreateHandlerMetaMap(),
            GeneratedActionHandlerRegistryArtifacts.CreateSyncHandlerMap(),
            GeneratedActionHandlerRegistryArtifacts.CreateAsyncHandlerMap());
    }
}

internal static class AnotherActionHandlerRegistry
{
    private static readonly Dictionary<ActionType, ActionHandlerMeta> HandlerMeta = new();

    private static readonly Dictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        Handlers = new();

    private static readonly Dictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers = new();

    public static IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetaMap => HandlerMeta;

    public static IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken,
        Task<ActionResponse>>> AsyncHandlerMap => AsyncHandlers;

    public static IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken,
        ActionResponse>> SyncHandlerMap => Handlers;

    public static void Reset()
    {
        HandlerMeta.Clear();
        Handlers.Clear();
        AsyncHandlers.Clear();
    }

    /// <summary>
    ///     注册handler，如果handler类同时实现了同步和异步接口，那么只会注册同步接口
    /// </summary>
    /// <param name="type"></param>
    public static void LoadHandlerFromType(Type type)
    {
        const string template =
            "[ActionHandlerRegistry] Loaded Handler: \"{0}\" for \"{1}\", ExecuteType = {2}, Permission = {3}";
        var attr = type.GetCustomAttribute<ActionHandlerAttribute>();
        if (attr is null) return;

        var handlerInstance = Activator.CreateInstance(type)!;
        var interfaces = type.GetInterfaces();

        // 检查是否实现了IActionHandler<TParam, TResult>
        var actionHandlerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IActionHandler<,>));

        if (actionHandlerInterface is not null)
        {
            // 获取泛型参数
            var genericArgs = actionHandlerInterface.GetGenericArguments();
            var tParam = genericArgs[0];
            var tResult = genericArgs[1];

            // 调用泛型的BuildHandler方法
            var buildHandlerMethod = typeof(AnotherActionHandlerRegistry)
                .GetMethod(nameof(BuildHandler), BindingFlags.Static | BindingFlags.NonPublic)
                ?.MakeGenericMethod(tParam, tResult);

            if (buildHandlerMethod is not null)
            {
                var handlerDelegate = buildHandlerMethod.Invoke(null, new[] { handlerInstance })!;
                Handlers[attr.ActionType] =
                    (Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>)handlerDelegate;
                HandlerMeta[attr.ActionType] = new ActionHandlerMeta(attr.Permission, EActionHandlerType.Sync);
                Log.Verbose(template, type.Name, attr.ActionType, "Sync", attr.Permission.ToString());
            }

            return;
        }

        // 检查是否实现了IAsyncActionHandler<TParam, TResult>
        var asyncActionHandlerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IAsyncActionHandler<,>));

        if (asyncActionHandlerInterface is not null)
        {
            // 获取泛型参数
            var genericArgs = asyncActionHandlerInterface.GetGenericArguments();
            var tParam = genericArgs[0];
            var tResult = genericArgs[1];

            // 调用泛型的BuildAsyncHandler方法
            var buildAsyncHandlerMethod = typeof(AnotherActionHandlerRegistry)
                .GetMethod(nameof(BuildAsyncHandler), BindingFlags.Static | BindingFlags.NonPublic)
                ?.MakeGenericMethod(tParam, tResult);

            if (buildAsyncHandlerMethod is not null)
            {
                var handlerDelegate = buildAsyncHandlerMethod.Invoke(null, new[] { handlerInstance })!;
                AsyncHandlers[attr.ActionType] =
                    (Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>)handlerDelegate;
                HandlerMeta[attr.ActionType] = new ActionHandlerMeta(attr.Permission, EActionHandlerType.Async);
                Log.Verbose(template, type.Name, attr.ActionType, "Async", attr.Permission.ToString());
            }
        }
    }


    // static void Main(string[] argv)
    // {
    //     var token = JToken.Parse("{}");
    //     var guid = Guid.Empty;
    //     WsContext ctx = new WsContext("", Guid.Empty, "*", DateTime.Now);
    //     IResolver resolver = null;
    //     var ct = CancellationToken.None;
    //
    //     var resp1 = Handlers[ActionType.Ping].Invoke(token, guid, ctx, resolver, ct);
    //     var resp2 = Handlers[ActionType.GetSystemInfo].Invoke(token, guid, ctx, resolver, ct);
    //     var resp3 = AsyncHandlers[ActionType.GetJavaList].Invoke(token, guid, ctx, resolver, ct).GetAwaiter()
    //         .GetResult();
    //
    //     System.Console.WriteLine(JsonConvert.SerializeObject(resp1, Formatting.Indented));
    //     System.Console.WriteLine(JsonConvert.SerializeObject(resp2, Formatting.Indented));
    //     System.Console.WriteLine(JsonConvert.SerializeObject(resp3, Formatting.Indented));
    // }

    /// <summary>
    ///     表达树构造同步handler
    /// </summary>
    /// <param name="handler"></param>
    /// <typeparam name="TParam"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    private static Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> BuildHandler<TParam,
        TResult>(
        IActionHandler<TParam, TResult> handler)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        // 构建表达式树
        var paramTokenExpr = Expression.Parameter(typeof(JsonElement?), "paramToken");
        var idExpr = Expression.Parameter(typeof(Guid), "id");
        var ctxExpr = Expression.Parameter(typeof(WsContext), "ctx");
        var resolverExpr = Expression.Parameter(typeof(IResolver), "resolver");
        var ctExpr = Expression.Parameter(typeof(CancellationToken), "ct");

        // 获取Process方法的引用
        var processMethod = typeof(ActionHandlerExtensions)
            .GetMethods()
            .First(m => m.Name == "Process" && m.GetParameters().Length == 6)
            .MakeGenericMethod(typeof(TParam), typeof(TResult));

        // 构建调用表达式
        var handlerExpr = Expression.Constant(handler);
        var callExpr = Expression.Call(
            processMethod,
            handlerExpr,
            paramTokenExpr,
            idExpr,
            ctxExpr,
            resolverExpr,
            ctExpr
        );

        // 编译为委托
        return Expression.Lambda<Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>(
            callExpr,
            paramTokenExpr,
            idExpr,
            ctxExpr,
            resolverExpr,
            ctExpr
        ).Compile();
    }

    /// <summary>
    ///     表达树构造异步handler
    /// </summary>
    /// <param name="handler"></param>
    /// <typeparam name="TParam"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    private static Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> BuildAsyncHandler<
        TParam,
        TResult>(
        IAsyncActionHandler<TParam, TResult> handler)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        // 构建表达式树
        var paramTokenExpr = Expression.Parameter(typeof(JsonElement?), "paramToken");
        var idExpr = Expression.Parameter(typeof(Guid), "id");
        var ctxExpr = Expression.Parameter(typeof(WsContext), "ctx");
        var resolverExpr = Expression.Parameter(typeof(IResolver), "resolver");
        var ctExpr = Expression.Parameter(typeof(CancellationToken), "ct");

        // 获取ProcessAsync方法的引用
        var processAsyncMethod = typeof(ActionHandlerExtensions)
            .GetMethods()
            .First(m => m.Name == "ProcessAsync" && m.GetParameters().Length == 6)
            .MakeGenericMethod(typeof(TParam), typeof(TResult));

        // 构建调用表达式
        var handlerExpr = Expression.Constant(handler);
        var callExpr = Expression.Call(
            processAsyncMethod,
            handlerExpr,
            paramTokenExpr,
            idExpr,
            ctxExpr,
            resolverExpr,
            ctExpr
        );

        // 编译为委托
        return Expression.Lambda<Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>(
            callExpr,
            paramTokenExpr,
            idExpr,
            ctxExpr,
            resolverExpr,
            ctExpr
        ).Compile();
    }
}
