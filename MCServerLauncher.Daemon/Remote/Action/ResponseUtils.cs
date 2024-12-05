using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ResponseUtils
{
    public static JObject Err(string? message, int retcode = 1400)
    {
        return new JObject
        {
            ["status"] = "error",
            ["retcode"] = retcode,
            ["data"] = new JObject
            {
                ["error_message"] = message
            }
        };
    }

    public static JObject Err(String action, string message, int retcode = 1400, bool log = true)
    {
        if (log)
            Log.Error("Error while handling Action {0}: {1}", action, message);
        return Err(message, retcode);
    }

    public static JObject Err(String action, ActionExecutionException exception, bool log = true)
    {
        if (log)
            Log.Error("Error while handling Action {0}: {1}", action, exception.ErrorMessage);
        return Err(exception.ErrorMessage, exception.Retcode);
    }

    public static JObject Ok(JObject? data = null)
    {
        return new JObject
        {
            ["status"] = "ok",
            ["retcode"] = 0,
            ["data"] = data ?? new JObject()
        };
    }
}

public class ActionExecutionException : Exception
{
    public readonly int Retcode;
    public readonly string? ErrorMessage;

    public ActionExecutionException(int retcode = 1400, string? errorMessage = null)
    {
        Retcode = retcode;
        ErrorMessage = errorMessage;
    }
}