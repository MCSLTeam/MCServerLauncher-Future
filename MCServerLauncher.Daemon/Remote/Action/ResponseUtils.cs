using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;
using JsonElement = System.Text.Json.JsonElement;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class ResponseUtils
{
    public static ActionResponse Err(ActionRetcode code, Guid? id)
    {
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            Retcode = code.Code,
            Message = code.Message,
            Data = null,
            Id = id ?? Guid.Empty
        };
    }

    public static ActionResponse Err(ActionRequest request, Exception exception, ActionRetcode code, bool verbose)
    {
        Log.Error("[Remote] Error while handling Action {0}: \n{1}", request.ActionType, exception.ToString());
        return Err(code.WithMessage(verbose ? exception.ToString() : exception.Message), request.Id);
    }

    public static ActionResponse Ok(JsonElement? data, Guid id)
    {
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            Retcode = ActionRetcode.Ok.Code,
            Message = ActionRetcode.Ok.Message,
            Data = data,
            Id = id
        };
    }

    public static ActionResponse FromResult(Result<Option<IActionResult>, ActionError> result, Guid id)
    {
        return result.Match(
            option => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Ok,
                Retcode = ActionRetcode.Ok.Code,
                Message = ActionRetcode.Ok.Message,
                Data = option.MapOr(
                    ToJsonElement,
                    EmptyObject),
                Id = id
            },
            err => new ActionResponse
            {
                RequestStatus = ActionRequestStatus.Error,
                Retcode = err.Retcode.Code,
                Message = err.ToString(),
                Data = null,
                Id = id
            }
        );
    }

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is null)
            return default;

        return StjJsonSerializer.SerializeToElement(value, DaemonRpcJsonBoundary.StjOptions);
    }

    private static readonly JsonElement EmptyObject = StjJsonSerializer.SerializeToElement(new EmptyActionResult(),
        DaemonRpcJsonBoundary.StjOptions);
}
