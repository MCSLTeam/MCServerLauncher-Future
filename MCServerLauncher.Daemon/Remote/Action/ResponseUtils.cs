using MCServerLauncher.Common.ProtoType.Action;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class ResponseUtils
{
    public static JObject Err(string? message, int code, string? echo = null)
    {
        return WithEcho(new JObject
        {
            ["status"] = "error",
            ["retcode"] = code,
            ["data"] = null,
            ["message"] = message
        }, echo);
    }

    public static JObject Err(ActionType action, Exception exception, int code, bool fullMessage, string? echo = null)
    {
        Log.Error("[Remote] Error while handling Action {0}: \n{1}", action, exception.ToString());
        var message = fullMessage ? exception.ToString() : exception.Message;
        return Err(message, code, echo);
    }

    public static JObject Err(ActionType action, ActionExecutionException exception, bool fullMessage,
        string? echo = null)
    {
        Log.Error("[Remote] Error while handling Action {0}: {1}", action, exception.Message);
        return Err(action, exception, exception.Code, fullMessage, echo);
    }

    public static JObject Ok(JObject? data = null, string? echo = null)
    {
        return WithEcho(new JObject
        {
            ["status"] = "ok",
            ["retcode"] = 0,
            ["data"] = data,
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
    public int Code { get; }

    // 添加包含内部异常的构造函数
    public ActionExecutionException(int code, string message, Exception innerException)
        : base(message, innerException) // 将message和innerException传递给基类
    {
        Code = code;
    }

    // 可选：其他构造函数
    public ActionExecutionException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public ActionExecutionException(int code = 1400)
        : base()
    {
        Code = code;
    }
}