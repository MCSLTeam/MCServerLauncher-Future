using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using MCServerLauncher.Daemon.Utils.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RustyOptions;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action;

#region Handler支持

/// <summary>
/// Action Handler的基础接口
/// </summary>
/// <typeparam name="TParam">Action参数类型</typeparam>
/// <typeparam name="TResult">Action返回值类型</typeparam>
interface IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    // TODO 之后换成System.Text.Json的JsonElement
    Result<TParam, ActionError> ParseParameter(JToken? token)
    {
        if (token is null)
        {
            return Result.Err<TParam, ActionError>(ActionRetcode.BadRequest.WithMessage("Missing parameters"));
        }

        try
        {
            // paramToken一定不为null
            var result = token.ToObject<TParam>(JsonSerializer.Create(DaemonJsonSettings.Settings))!;
            return Result.Ok<TParam, ActionError>(result);
        }
        catch (JsonSerializationException e)
        {
            var @params = e.Path?.Split(new[] { '.' });
            var errorMessage = "Could not deserialize param";

            if (@params is not null)
            {
                errorMessage += $"'{@params[1]}' at '{e.Path}'";
            }

            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage(errorMessage));
        }
        catch (NullReferenceException e)
        {
            return Result.Err<TParam, ActionError>(ActionRetcode.ParamError.WithMessage("Could not deserialize param"));
        }
        catch (Exception e)
        {
            return Result.Err<TParam, ActionError>(
                ActionRetcode.ParamError.WithMessage("Could not deserialize param: " + e.Message));
        }
    }

    ActionResponse ToResponse(Result<TResult, ActionError> result, Guid id)
    {
        return result.Match(
            r => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Ok,
                Retcode = ActionRetcode.Ok.Code,
                Message = ActionRetcode.Ok.Message,
                Data = JObject.FromObject(r, JsonSerializer.Create(DaemonJsonSettings.Settings)),
                Id = id
            },
            err => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Error,
                Retcode = err.Retcode.Code,
                Message = err.ToString(),
                Data = null,
                Id = id
            });
    }
}

/// <summary>
/// 同步ActionHandler接口（适用于耗时短，没有IO阻塞/计算量的任务）， 直接在当前线程处理
/// </summary>
/// <typeparam name="TParam"></typeparam>
/// <typeparam name="TResult"></typeparam>
interface IActionHandler<TParam, TResult> : IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    Result<TResult, ActionError> Handle(TParam param, WsContext ctx, IResolver resolver, CancellationToken ct);
}

/// <summary>
/// 异步ActionHandler接口（适用于耗时长，有IO阻塞/计算量的任务），提交到线程池处理
/// </summary>
/// <typeparam name="TParam"></typeparam>
/// <typeparam name="TResult"></typeparam>
interface IAsyncActionHandler<TParam, TResult> : IActionHandlerBase<TParam, TResult>
    where TParam : class, IActionParameter
    where TResult : class, IActionResult
{
    Task<Result<TResult, ActionError>> HandleAsync(
        TParam param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    );
}

static class ActionHandlerExtensions
{
    public static readonly EmptyActionParameter EmptyActionParameter = new();
    public static readonly EmptyActionResult EmptyActionResult = new();


    #region ResultHelper

    public static Result<TResult, ActionError> Ok<TParam, TResult>(this IActionHandlerBase<TParam, TResult> self,
        TResult result)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        return Result.Ok<TResult, ActionError>(result);
    }

    public static Result<TResult, ActionError> Err<TParam, TResult>(this IActionHandlerBase<TParam, TResult> self,
        ActionError error)
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        return Result.Err<TResult, ActionError>(error);
    }

    #endregion

    #region ProcessHelper

    public static ActionResponse Process<TParam, TResult>(
        this IActionHandler<TParam, TResult> self,
        JToken? param,
        Guid id,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        var parsed = self.ParseParameter(param);

        var result = parsed.IsErr(out var err)
            ? Result.Err<TResult, ActionError>(err!)
            : self.Handle(parsed.Ok().Unwrap(), ctx, resolver, ct);

        return self.ToResponse(result, id);
    }

    public static async Task<ActionResponse> ProcessAsync<TParam, TResult>(
        this IAsyncActionHandler<TParam, TResult> self,
        JToken? param,
        Guid id,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct
    )
        where TParam : class, IActionParameter
        where TResult : class, IActionResult
    {
        var parsed = self.ParseParameter(param);

        var task = parsed.IsErr(out var err)
            ? Task.FromResult(Result.Err<TResult, ActionError>(err!))
            : self.HandleAsync(parsed.Ok().Unwrap(), ctx, resolver, ct);

        var result = await task;
        return self.ToResponse(result, id);
    }

    #endregion
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
class ActionHandlerAttribute : Attribute
{
    public ActionType ActionType { get; }
    public IMatchable Permission { get; }

    public ActionHandlerAttribute(ActionType actionType, string permission)
    {
        this.ActionType = actionType;
        this.Permission = Authentication.Permission.Of(permission);
    }
}

#endregion

#region ActionHandler注册示例

[ActionHandler(ActionType.Ping, "*")]
class ActionHandler : IActionHandler<EmptyActionParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(EmptyActionParameter param, WsContext ctx, IResolver resolver,
        CancellationToken ct)
    {
        return this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.GetSystemInfo, "*")]
class ActionHandler1 : IActionHandler<EmptyActionParameter, GetSystemInfoResult>
{
    public Result<GetSystemInfoResult, ActionError> Handle(EmptyActionParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return this.Ok(new GetSystemInfoResult { Info = SystemInfoHelper.GetSystemInfo().GetAwaiter().GetResult() });
    }
}

[ActionHandler(ActionType.GetJavaList, "*")]
class AsyncActionHandler : IAsyncActionHandler<EmptyActionParameter, GetJavaListResult>
{
    public async Task<Result<GetJavaListResult, ActionError>> HandleAsync(EmptyActionParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return this.Ok(new GetJavaListResult { JavaList = await JavaScanner.ScanJavaAsync() });
    }
}

#endregion

#region ActionHandler注册表

enum EActionHandlerType
{
    Sync,
    Async
}

class ActionHandlerMeta
{
    public IMatchable Permission { get; }
    public EActionHandlerType Type { get; }

    internal ActionHandlerMeta(IMatchable permission, EActionHandlerType type)
    {
        this.Permission = permission;
        this.Type = type;
    }
}

static class AnotherActionHandlerRegistry
{
    private static Dictionary<ActionType, ActionHandlerMeta> HandlerMeta = new();

    private static Dictionary<ActionType, Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        Handlers = new();

    private static Dictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers = new();

    public static IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetaMap => HandlerMeta;

    public static IReadOnlyDictionary<ActionType, Func<JToken?, Guid, WsContext, IResolver, CancellationToken,
        Task<ActionResponse>>> AsyncHandlerMap => AsyncHandlers;

    public static IReadOnlyDictionary<ActionType, Func<JToken?, Guid, WsContext, IResolver, CancellationToken,
        ActionResponse>> SyncHandlerMap => Handlers;

    /// <summary>
    /// 注册handler，如果handler类同时实现了同步和异步接口，那么只会注册同步接口
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
    ///  表达树构造同步handler
    /// </summary>
    /// <param name="handler"></param>
    /// <typeparam name="TParam"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    static Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> BuildHandler<TParam, TResult>(
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
    /// 表达树构造异步handler
    /// </summary>
    /// <param name="handler"></param>
    /// <typeparam name="TParam"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    /// <returns></returns>
    static Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> BuildAsyncHandler<TParam,
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

#endregion

/// <summary>
/// Action执行器,解析request调用对应handler返回响应, 异步的handler直接发送响应
/// </summary>
class AnotherActionExecutor
{
    class ActionTask
    {
        public JToken? Param;
        public Guid Id;
        public WsContext Context;
        public IResolver Resolver;
        public CancellationToken CancellationToken;
        public Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> AsyncHandler;
        public ActionResponse Result;
    }

    private readonly IReadOnlyDictionary<ActionType, ActionHandlerMeta> _handlerMetas;

    private readonly IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        _syncHandlers;

    private readonly IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        _asyncHandlers;

    private static readonly ObjectPool<ActionTask> ActionTaskPool = new DefaultObjectPool<ActionTask>(
        new DefaultPooledObjectPolicy<ActionTask>());

    private TransformBlock<ActionTask, ActionTask> ActionHandleBlock { get; }
    private ActionBlock<ActionTask> ActionSendBlock { get; }
    private IResolver Resolver { get; }
    private CancellationTokenSource Cts { get; }

    public AnotherActionExecutor(IResolver resolver)
    {
        _handlerMetas = AnotherActionHandlerRegistry.HandlerMetaMap;
        _syncHandlers = AnotherActionHandlerRegistry.SyncHandlerMap;
        _asyncHandlers = AnotherActionHandlerRegistry.AsyncHandlerMap;
        Resolver = resolver;
        Cts = new CancellationTokenSource();

        ActionHandleBlock = new TransformBlock<ActionTask, ActionTask>(async task =>
        {
            task.Result =
                await task.AsyncHandler.Invoke(task.Param, task.Id, task.Context, task.Resolver,
                    task.CancellationToken);
            return task;
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 16, CancellationToken = Cts.Token, MaxDegreeOfParallelism = 16,
            EnsureOrdered = false
        });

        ActionSendBlock = new ActionBlock<ActionTask>(async task =>
        {
            var o = JsonConvert.SerializeObject(task.Result, DaemonJsonSettings.Settings);
            Log.Verbose("[Remote] Sending message: \n{0}", o);
            var ws = task.Context.GetWebsocket();

            if (ws != null) await ws.SendAsync(o);
            else
                Log.Warning("[Remote] Failed to send text, because websocket connection is closed or lost.");
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 16, CancellationToken = Cts.Token, MaxDegreeOfParallelism = 16,
            EnsureOrdered = false
        });

        ActionHandleBlock.LinkTo(ActionSendBlock, new DataflowLinkOptions { PropagateCompletion = true });
    }

    bool PostAsyncAction(ActionType actionType, JToken? param, WsContext ctx, Guid id)
    {
        var task = ActionTaskPool.Get();

        task.Param = param;
        task.Id = id;
        task.Context = ctx;
        task.Resolver = Resolver;
        task.CancellationToken = Cts.Token;
        task.AsyncHandler = _asyncHandlers[actionType];
        return ActionHandleBlock.Post(task);
    }

    // TODO 后续换成输入Span<byte>（System.Text.Json的反序列化）
    Result<ActionRequest, ActionResponse> ParseRequest(string text)
    {
        try
        {
            // TODO 反序列化使用System.Text.Json
            var request = JsonConvert.DeserializeObject<ActionRequest>(text, DaemonJsonSettings.Settings)!;
            Log.Verbose("[Remote] Received message:{0}", request);
            return Result.Ok<ActionRequest, ActionResponse>(request);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            return Result.Err<ActionRequest, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.BadRequest.WithMessage("Could not parse action json"), null));
        }
    }

    Result<ActionHandlerMeta, ActionResponse> CheckHandler(ActionRequest request, WsContext ctx)
    {
        if (!_handlerMetas.TryGetValue(request.ActionType, out var meta))
        {
            return Result.Err<ActionHandlerMeta, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.ActionUnavailable.WithMessage("Action not implemented"), request.Id));
        }

        if (!ctx.Permissions.Matches(meta.Permission))
        {
            return Result.Err<ActionHandlerMeta, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.PermissionDenied.WithMessage("Permission denied"), request.Id));
        }

        return Result.Ok<ActionHandlerMeta, ActionResponse>(meta);
    }


    /// <summary>
    /// 处理请求，返回响应（如果是异步handler, 不立即返回响应）
    /// </summary>
    /// <param name="text"></param>
    /// <param name="ctx"></param>
    /// <returns></returns>
    public ActionResponse? ProcessAction(string text, WsContext ctx)
    {
        var parsed = ParseRequest(text);

        if (parsed.IsErr(out var response))
        {
            return response;
        }

        var request = parsed.Unwrap();

        var @checked = CheckHandler(request, ctx);
        if (@checked.IsErr(out response))
        {
            return response;
        }

        var meta = @checked.Unwrap();

        return meta.Type switch
        {
            EActionHandlerType.Sync => _syncHandlers[request.ActionType]
                .Invoke(request.Parameter, request.Id, ctx, Resolver, Cts.Token),

            EActionHandlerType.Async => PostAsyncAction(request.ActionType, request.Parameter, ctx, request.Id)
                ? null
                : ResponseUtils.Err(ActionRetcode.RateLimitExceeded, request.Id),

            _ => ResponseUtils.Err(
                ActionRetcode.UnexpectedError.WithMessage($"Unknown action handler type: {meta.Type.ToString()}"),
                request.Id)
        };
    }
}