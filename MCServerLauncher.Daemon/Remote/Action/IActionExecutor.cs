using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;
using System.Text.Json;
using TouchSocket.Core;
using JsonElement = System.Text.Json.JsonElement;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
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
            var request = StjJsonSerializer.Deserialize<ActionRequest>(text, DaemonRpcJsonBoundary.StjOptions)!;
            Log.Verbose("[Remote] Received message:{0}", request);
            return Result.Ok<ActionRequest, ActionResponse>(request);
        }
        catch (Exception exception) when (exception is JsonException or NullReferenceException)
        {
            return Result.Err<ActionRequest, ActionResponse>(
                ResponseUtils.Err(ActionRetcode.BadRequest.WithMessage("Could not parse action json"), null));
        }
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
