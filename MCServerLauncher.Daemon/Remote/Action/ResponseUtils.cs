using MCServerLauncher.Common.ProtoType.Action;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class ResponseUtils
{
    public static ActionResponse Err(string? message, ActionReturnCode code)
    {
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            ReturnCode = code,
            Data = null,
            Message = message ?? string.Empty
        };
    }

    public static ActionResponse Err(ActionRequest request, string? message, ActionReturnCode code)
    {
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Error,
            ReturnCode = code,
            Data = null,
            Message = message ?? string.Empty,
            Id = request.Id
        };
    }

    public static ActionResponse Err(ActionRequest request, Exception exception, ActionReturnCode code, bool verbose)
    {
        Log.Error("[Remote] Error while handling Action {0}: \n{1}", request.ActionType, exception.ToString());
        var message = verbose ? exception.ToString() : exception.Message;
        return Err(request, message, code);
    }

    public static ActionResponse Err(ActionRequest request, ActionException exception, bool verbose)
    {
        return Err(request, exception, exception.Code, verbose);
    }

    public static ActionResponse Ok(JObject? data, Guid id)
    {
        return new ActionResponse
        {
            RequestStatus = ActionRequestStatus.Ok,
            ReturnCode = ActionReturnCode.Ok,
            Data = data,
            Id = id
        };
    }
}