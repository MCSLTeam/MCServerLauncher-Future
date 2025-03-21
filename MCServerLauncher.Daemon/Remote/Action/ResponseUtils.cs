using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ResponseUtils
{
    public static JObject Err(string? message, int retcode = 1400, string? echo = null)
    {
        return WithEcho(new JObject
        {
            ["status"] = "error",
            ["retcode"] = retcode,
            ["data"] = new JObject(),
            ["message"] = message
        }, echo);
    }

    public static JObject Err(string action, string message, int retcode = 1400, bool log = true, string? echo = null)
    {
        if (log)
            Log.Error("[Remote] Error while handling Action {0}: {1}", action, message);
        return Err(message, retcode, echo);
    }

    public static JObject Err(string action, ActionExecutionException exception, bool log = true, string? echo = null)
    {
        if (log)
            Log.Error("[Remote] Error while handling Action {0}: {1}", action, exception.ErrorMessage);
        return Err(exception.ErrorMessage, exception.Retcode, echo);
    }

    public static JObject Ok(JObject? data = null, string? echo = null)
    {
        return WithEcho(new JObject
        {
            ["status"] = "ok",
            ["retcode"] = 0,
            ["data"] = data ?? new JObject(),
            ["message"] = ""
        }, echo);
    }

    private static JObject WithEcho(JObject result, string? echo)
    {
        if (echo != null) result["echo"] = echo;

        return result;
    }
}

public class ActionExecutionException : Exception
{
    public readonly string? ErrorMessage;
    public readonly int Retcode;

    public ActionExecutionException(int retcode = 1400, string? errorMessage = null)
    {
        Retcode = retcode;
        ErrorMessage = errorMessage;
    }
}