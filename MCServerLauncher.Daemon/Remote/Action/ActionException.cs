using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionException : Exception
{
    // 添加包含内部异常的构造函数
    public ActionException(ActionReturnCode code, string message, Exception innerException)
        : base(message, innerException) // 将message和innerException传递给基类
    {
        Code = code;
    }

    // 可选：其他构造函数
    public ActionException(ActionReturnCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ActionException(ActionReturnCode code = ActionReturnCode.InternalError)
    {
        Code = code;
    }

    public ActionReturnCode Code { get; }
}

public static class ActionExceptionHelper
{
    public static void ThrowIf(bool condition, ActionReturnCode code, string message)
    {
        if (condition) throw new ActionException(code, message);
    }

    public static void Throw(ActionReturnCode code, string message, Exception? innerException = null)
    {
        if (innerException is null) throw new ActionException(code, message);

        throw new ActionException(code, message, innerException);
    }

    public static ActionException Context(this Exception e, ActionReturnCode code, string message)
    {
        return new ActionException(code, message, e);
    }

    public static ActionException Context(this Exception e, string message)
    {
        return new ActionException(ActionReturnCode.InternalError, message, e);
    }

    public static ActionException Context(this Exception e, ActionReturnCode code)
    {
        return new ActionException(code, e.Message, e);
    }
}