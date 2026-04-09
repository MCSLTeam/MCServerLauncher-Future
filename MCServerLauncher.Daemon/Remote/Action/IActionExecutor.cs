using MCServerLauncher.Common.Internal.Performance;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;
using System.Text.Json;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action;

public interface IActionExecutor
{
    IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

    IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; }

    IReadOnlyDictionary<ActionType,
            Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
        AsyncHandlers { get; }

    ActionResponse? ProcessAction(string text, WsContext ctx);
    Task ShutdownAsync();
}

public static class ActionExecutorExtensions
{
    // TODO 后续换成输入Span<byte>（System.Text.Json的反序列化）
    public static Result<ActionRequest, ActionResponse> ParseRequest(this IActionExecutor _, string text)
    {
        try
        {
            var request = JsonElementHotPathAdapters.Deserialize(
                text,
                DaemonRpcTypeInfoCache<ActionRequest>.TypeInfo);
            Log.Verbose("[Remote] Received message:{0}", request);
            return Result.Ok<ActionRequest, ActionResponse>(request);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            return Result.Err<ActionRequest, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.BadRequest.WithMessage("Could not parse action json"), null));
        }
    }

    public static Result<ActionRequest, ActionResponse> ParseRequest(this IActionExecutor _, ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var request = JsonSerializer.Deserialize(
                utf8Json,
                DaemonRpcTypeInfoCache<ActionRequest>.TypeInfo);
            Log.Verbose("[Remote] Received message:{0}", request);
            return Result.Ok<ActionRequest, ActionResponse>(request!);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            return Result.Err<ActionRequest, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.BadRequest.WithMessage("Could not parse action json"), null));
        }
    }

    public static Result<ActionRequest, ActionResponse> ParseRequest(this IActionExecutor executor, ReadOnlyMemory<byte> utf8Json)
    {
        return executor.ParseRequest(utf8Json.Span);
    }

    public static ActionResponse? ProcessAction(this IActionExecutor executor, ReadOnlySpan<byte> utf8Json, WsContext ctx)
    {
        return executor.ProcessParsedRequest(executor.ParseRequest(utf8Json), ctx);
    }

    public static ActionResponse? ProcessAction(this IActionExecutor executor, ReadOnlyMemory<byte> utf8Json, WsContext ctx)
    {
        return executor.ProcessParsedRequest(executor.ParseRequest(utf8Json), ctx);
    }

    public static ActionResponse? ProcessParsedRequest(this IActionExecutor executor,
        Result<ActionRequest, ActionResponse> parsed,
        WsContext ctx)
    {
        if (parsed.IsErr(out var response)) return response;

        var request = parsed.Unwrap();

        var @checked = executor.CheckHandler(request, ctx);
        if (@checked.IsErr(out response)) return response;

        var meta = @checked.Unwrap();

        return meta.Type switch
        {
            EActionHandlerType.Sync => executor.SyncHandlers[request.ActionType]
                .Invoke(request.Parameter, request.Id, ctx, executor.GetResolver(), executor.GetCancellationToken()),

            EActionHandlerType.Async => executor.PostAsyncAction(request.ActionType, request.Parameter, ctx, request.Id)
                ? null
                : ResponseUtils.Err(ActionRetcode.RateLimitExceeded, request.Id),

            _ => ResponseUtils.Err(
                ActionRetcode.UnexpectedError.WithMessage($"Unknown action handler type: {meta.Type}"),
                request.Id)
        };
    }

    internal static IResolver GetResolver(this IActionExecutor executor)
    {
        return executor is AnotherActionExecutor another
            ? another.Resolver
            : throw new NotSupportedException($"Resolver access is not supported for executor type {executor.GetType().FullName}");
    }

    internal static CancellationToken GetCancellationToken(this IActionExecutor executor)
    {
        return executor is AnotherActionExecutor another
            ? another.Cts.Token
            : CancellationToken.None;
    }

    internal static bool PostAsyncAction(this IActionExecutor executor, ActionType actionType, JsonElement? param, WsContext ctx, Guid id)
    {
        return executor is AnotherActionExecutor another
            ? another.PostAsyncAction(actionType, param, ctx, id)
            : throw new NotSupportedException($"Async dispatch is not supported for executor type {executor.GetType().FullName}");
    }

    public static Result<ActionHandlerMeta, ActionResponse> CheckHandler(this IActionExecutor executor,
        ActionRequest request,
        WsContext ctx)
    {
        if (!executor.HandlerMetas.TryGetValue(request.ActionType, out var meta))
            return Result.Err<ActionHandlerMeta, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.ActionUnavailable.WithMessage("Action not implemented"), request.Id));

        if (!ctx.Permissions.Matches(meta.Permission))
            return Result.Err<ActionHandlerMeta, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.PermissionDenied.WithMessage("Permission denied"), request.Id));

        return Result.Ok<ActionHandlerMeta, ActionResponse>(meta);
    }
}
