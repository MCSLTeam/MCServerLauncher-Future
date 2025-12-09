using System.Linq.Expressions;
using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

public enum EActionHandlerType
{
    Sync,
    Async
}

public class ActionHandlerMeta
{
    internal ActionHandlerMeta(IMatchable permission, EActionHandlerType type)
    {
        Permission = permission;
        Type = type;
    }

    public IMatchable Permission { get; }
    public EActionHandlerType Type { get; }
}

internal static class AnotherActionHandlerRegistry
{
    private static readonly Dictionary<ActionType, ActionHandlerMeta> HandlerMeta = new();

    private static readonly Dictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        Handlers = new();

    private static readonly Dictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers = new();

    public static IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetaMap => HandlerMeta;

    public static IReadOnlyDictionary<ActionType, Func<JToken?, Guid, WsContext, IResolver, CancellationToken,
        Task<ActionResponse>>> AsyncHandlerMap => AsyncHandlers;

    public static IReadOnlyDictionary<ActionType, Func<JToken?, Guid, WsContext, IResolver, CancellationToken,
        ActionResponse>> SyncHandlerMap => Handlers;

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

        if (actionHandlerInterface != null)
        {
            // 获取泛型参数
            var genericArgs = actionHandlerInterface.GetGenericArguments();
            var tParam = genericArgs[0];
            var tResult = genericArgs[1];

            // 调用泛型的BuildHandler方法
            var buildHandlerMethod = typeof(AnotherActionHandlerRegistry)
                .GetMethod(nameof(BuildHandler), BindingFlags.Static | BindingFlags.NonPublic)
                ?.MakeGenericMethod(tParam, tResult);

            if (buildHandlerMethod != null)
            {
                var handlerDelegate = buildHandlerMethod.Invoke(null, new[] { handlerInstance })!;
                Handlers[attr.ActionType] =
                    (Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>)handlerDelegate;
                HandlerMeta[attr.ActionType] = new ActionHandlerMeta(attr.Permission, EActionHandlerType.Sync);
                Log.Verbose(template, type.Name, attr.ActionType, "Sync", attr.Permission.ToString());
            }

            return;
        }

        // 检查是否实现了IAsyncActionHandler<TParam, TResult>
        var asyncActionHandlerInterface = interfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IAsyncActionHandler<,>));

        if (asyncActionHandlerInterface != null)
        {
            // 获取泛型参数
            var genericArgs = asyncActionHandlerInterface.GetGenericArguments();
            var tParam = genericArgs[0];
            var tResult = genericArgs[1];

            // 调用泛型的BuildAsyncHandler方法
            var buildAsyncHandlerMethod = typeof(AnotherActionHandlerRegistry)
                .GetMethod(nameof(BuildAsyncHandler), BindingFlags.Static | BindingFlags.NonPublic)
                ?.MakeGenericMethod(tParam, tResult);

            if (buildAsyncHandlerMethod != null)
            {
                var handlerDelegate = buildAsyncHandlerMethod.Invoke(null, new[] { handlerInstance })!;
                AsyncHandlers[attr.ActionType] =
                    (Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>)handlerDelegate;
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
    private static Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> BuildHandler<TParam,
        TResult>(
        IActionHandler<TParam, TResult> handler)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        // 构建表达式树
        var paramTokenExpr = Expression.Parameter(typeof(JToken), "paramToken");
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
        return Expression.Lambda<Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>(
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
    private static Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> BuildAsyncHandler<
        TParam,
        TResult>(
        IAsyncActionHandler<TParam, TResult> handler)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        // 构建表达式树
        var paramTokenExpr = Expression.Parameter(typeof(JToken), "paramToken");
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
        return Expression.Lambda<Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>(
            callExpr,
            paramTokenExpr,
            idExpr,
            ctxExpr,
            resolverExpr,
            ctExpr
        ).Compile();
    }
}