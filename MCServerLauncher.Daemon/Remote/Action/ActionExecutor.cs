using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Serialization;
using Microsoft.Extensions.ObjectPool;
using Serilog;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     Action执行器,解析request调用对应handler返回响应, 异步的handler直接发送响应
/// </summary>
internal class AnotherActionExecutor : IActionExecutor
{
    internal const int MaxDegreeOfParallelism = 16;

    private static readonly ObjectPool<ActionTask> ActionTaskPool = new DefaultObjectPool<ActionTask>(
        new DefaultPooledObjectPolicy<ActionTask>());

    public AnotherActionExecutor(
        IResolver resolver,
        ActionHandlerRegistrySnapshot registry,
        IActionExecutorInstrumentation? instrumentation = null,
        Func<WsContext, string, CancellationToken, Task>? sendAsync = null)
    {
        HandlerMetas = registry.HandlerMetas;
        SyncHandlers = registry.SyncHandlers;
        AsyncHandlers = registry.AsyncHandlers;
        Resolver = resolver;
        Cts = new CancellationTokenSource();
        Instrumentation = instrumentation ?? NoopActionExecutorInstrumentation.Instance;
        SendAsync = sendAsync ?? ((context, payload, cancellationToken) =>
            context.GetWebsocket().SendAsync(payload, cancellationToken: cancellationToken));

        ActionHandleBlock = new TransformBlock<ActionTask, ActionTask>(async task =>
        {
            var handlerStart = Stopwatch.GetTimestamp();
            var success = false;
            var canceled = false;

            try
            {
                Instrumentation.OnQueueWaitObserved(Stopwatch.GetElapsedTime(task.EnqueueTimestamp));

                task.Result =
                    await task.AsyncHandler.Invoke(task.Param, task.Id, task.Context, task.Resolver,
                        task.CancellationToken);

                success = true;
            }
            catch (OperationCanceledException e)
            {
                canceled = true;
                task.Result = ResponseUtils.Err(
                    ActionRetcode.UnexpectedError.WithMessage(e.Message),
                    task.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "[Remote] Unhandled exception in async action handler");
                task.Result = ResponseUtils.Err(
                    ActionRetcode.UnexpectedError.WithMessage(e.Message),
                    task.Id);
            }
            finally
            {
                Instrumentation.OnHandlerCompleted(
                    Stopwatch.GetElapsedTime(handlerStart),
                    success,
                    canceled);
            }

            return task;
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = -1, CancellationToken = Cts.Token, MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            EnsureOrdered = false
        });

        ActionSendBlock = new ActionBlock<ActionTask>(async task =>
        {
            var sendStart = Stopwatch.GetTimestamp();
            var success = false;
            var canceled = false;

            try
            {
                var o = StjJsonSerializer.Serialize(task.Result, DaemonRpcJsonBoundary.StjOptions);
                Log.Verbose("[Remote] Sending message: \n{0}", o);
                await SendAsync(task.Context, o, task.CancellationToken);

                success = true;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                throw;
            }
            finally
            {
                Instrumentation.OnSendCompleted(
                    Stopwatch.GetElapsedTime(sendStart),
                    success,
                    canceled);
                ActionTaskPool.Return(task);
            }
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = -1, CancellationToken = Cts.Token, MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            EnsureOrdered = false
        });

        ActionHandleBlock.LinkTo(ActionSendBlock, new DataflowLinkOptions { PropagateCompletion = true });
    }

    private TransformBlock<ActionTask, ActionTask> ActionHandleBlock { get; }
    private ActionBlock<ActionTask> ActionSendBlock { get; }
    internal IResolver Resolver { get; }
    internal CancellationTokenSource Cts { get; }
    private IActionExecutorInstrumentation Instrumentation { get; }
    private Func<WsContext, string, CancellationToken, Task> SendAsync { get; }

    public IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

    public IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; }

    public IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers { get; }


    /// <summary>
    ///     处理请求，返回响应（如果是异步handler, 不立即返回响应）
    /// </summary>
    /// <param name="text"></param>
    /// <param name="ctx"></param>
    /// <returns></returns>
    public ActionResponse? ProcessAction(string text, WsContext ctx)
    {
        return this.ProcessParsedRequest(this.ParseRequest(text), ctx);
    }

    public async Task ShutdownAsync()
    {
        ActionHandleBlock.Complete();
        Cts.Cancel();
        await ActionHandleBlock.Completion;
        await ActionSendBlock.Completion;
    }

    internal bool PostAsyncAction(ActionType actionType, JsonElement? param, WsContext ctx, Guid id)
    {
        var task = ActionTaskPool.Get();
        Instrumentation.OnQueueSubmitted();

        task.Param = param;
        task.Id = id;
        task.Context = ctx;
        task.Resolver = Resolver;
        task.CancellationToken = Cts.Token;
        task.AsyncHandler = AsyncHandlers[actionType];
        task.EnqueueTimestamp = Stopwatch.GetTimestamp();

        var accepted = ActionHandleBlock.Post(task);
        if (!accepted)
        {
            Instrumentation.OnQueueRejected();
            ActionTaskPool.Return(task);
        }

        return accepted;
    }

    private class ActionTask
    {
        public Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> AsyncHandler;
        public CancellationToken CancellationToken;
        public WsContext Context;
        public Guid Id;
        public JsonElement? Param;
        public IResolver Resolver;
        public ActionResponse Result;
        public long EnqueueTimestamp;
    }
}
