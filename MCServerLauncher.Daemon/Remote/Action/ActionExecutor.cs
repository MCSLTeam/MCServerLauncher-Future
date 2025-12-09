using System.Threading.Tasks.Dataflow;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action执行器,解析request调用对应handler返回响应, 异步的handler直接发送响应
/// </summary>
internal class AnotherActionExecutor : IActionExecutor
{
    private static readonly ObjectPool<ActionTask> ActionTaskPool = new DefaultObjectPool<ActionTask>(
        new DefaultPooledObjectPolicy<ActionTask>());

    public AnotherActionExecutor(IResolver resolver)
    {
        HandlerMetas = AnotherActionHandlerRegistry.HandlerMetaMap;
        SyncHandlers = AnotherActionHandlerRegistry.SyncHandlerMap;
        AsyncHandlers = AnotherActionHandlerRegistry.AsyncHandlerMap;
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
            await task.Context.GetWebsocket().SendAsync(o, cancellationToken: task.CancellationToken);
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 16, CancellationToken = Cts.Token, MaxDegreeOfParallelism = 16,
            EnsureOrdered = false
        });

        ActionHandleBlock.LinkTo(ActionSendBlock, new DataflowLinkOptions { PropagateCompletion = true });
    }

    private TransformBlock<ActionTask, ActionTask> ActionHandleBlock { get; }
    private ActionBlock<ActionTask> ActionSendBlock { get; }
    private IResolver Resolver { get; }
    private CancellationTokenSource Cts { get; }

    public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

    public IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; }

    public IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers { get; }


    /// <summary>
    ///     处理请求，返回响应（如果是异步handler, 不立即返回响应）
    /// </summary>
    /// <param name="text"></param>
    /// <param name="ctx"></param>
    /// <returns></returns>
    public ActionResponse? ProcessAction(string text, WsContext ctx)
    {
        var parsed = this.ParseRequest(text);

        if (parsed.IsErr(out var response)) return response;

        var request = parsed.Unwrap();

        var @checked = this.CheckHandler(request, ctx);
        if (@checked.IsErr(out response)) return response;

        var meta = @checked.Unwrap();

        return meta.Type switch
        {
            EActionHandlerType.Sync => SyncHandlers[request.ActionType]
                .Invoke(request.Parameter, request.Id, ctx, Resolver, Cts.Token),

            EActionHandlerType.Async => PostAsyncAction(request.ActionType, request.Parameter, ctx, request.Id)
                ? null
                : ResponseUtils.Err(ActionRetcode.RateLimitExceeded, request.Id),

            _ => ResponseUtils.Err(
                ActionRetcode.UnexpectedError.WithMessage($"Unknown action handler type: {meta.Type.ToString()}"),
                request.Id)
        };
    }

    public async Task ShutdownAsync()
    {
        ActionHandleBlock.Complete();
        Cts.Cancel();
        await ActionHandleBlock.Completion;
        await ActionSendBlock.Completion;
    }

    private bool PostAsyncAction(ActionType actionType, JToken? param, WsContext ctx, Guid id)
    {
        var task = ActionTaskPool.Get();

        task.Param = param;
        task.Id = id;
        task.Context = ctx;
        task.Resolver = Resolver;
        task.CancellationToken = Cts.Token;
        task.AsyncHandler = AsyncHandlers[actionType];

        return ActionHandleBlock.Post(task);
    }

    private class ActionTask
    {
        public Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> AsyncHandler;
        public CancellationToken CancellationToken;
        public WsContext Context;
        public Guid Id;
        public JToken? Param;
        public IResolver Resolver;
        public ActionResponse Result;
    }
}