using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RustyOptions;
using Serilog;
using TouchSocket.Core;
using Result = RustyOptions.Result;

namespace MCServerLauncher.Daemon.Remote.Action;

public interface IActionExecutor
{
    IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetas { get; }

    IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>
        SyncHandlers { get; }

    IReadOnlyDictionary<ActionType,
            Func<JToken?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>
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