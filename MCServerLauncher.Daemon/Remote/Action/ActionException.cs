using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.Daemon.Remote.Action;

public class ActionException : Exception
{
    // 添加包含内部异常的构造函数
    public ActionException(ActionRetcode? code = null, Exception? innerException = null)
        : base(code?.Message ?? ActionRetcode.UnexpectedError.Message, innerException) // 将message和innerException传递给基类
    {
        Retcode = code ?? ActionRetcode.UnexpectedError;
    }

    // 可选：其他构造函数
    public ActionException(ActionRetcode? code = null)
        : this(code, null)
    {
    }

    public ActionException(Exception? innerException = null)
        : this(null, innerException)
    {
    }

    public ActionException()
        : this(null, null)
    {
    }

    public ActionRetcode Retcode { get; }
}

public static class ActionExceptionHelper
{
    public static void ThrowIf(bool condition, ActionRetcode? code = null, Exception? innerException = null)
    {
        if (condition) throw new ActionException(code?.WithException(innerException), innerException);
    }

    public static void Throw(ActionRetcode code, Exception? innerException = null)
    {
        throw new ActionException(code.WithException(innerException), innerException);
    }

    public static ActionException Context(this Exception e)
    {
        return new ActionException(ActionRetcode.UnexpectedError.WithException(e), e);
    }

    public static ActionException Context(this Exception e, ActionRetcode code)
    {
        return new ActionException(code.WithException(e), e);
    }
}