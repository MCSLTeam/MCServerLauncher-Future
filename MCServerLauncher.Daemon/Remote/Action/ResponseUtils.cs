using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RustyOptions;
using Serilog;

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

    public static ActionResponse Ok(JObject? data, Guid id)
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
                    data => JObject.FromObject(data, JsonSerializer.Create(DaemonJsonSettings.Settings)),
                    new JObject()),
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
}