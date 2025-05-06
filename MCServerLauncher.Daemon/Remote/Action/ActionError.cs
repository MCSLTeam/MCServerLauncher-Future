using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     ActionError is a wrapper of ActionRetcode.
/// </summary>
public class ActionError : Error
{
    public ActionError(ActionRetcode? retcode = null)
    {
        Retcode = retcode ?? ActionRetcode.UnexpectedError;
    }

    public ActionRetcode Retcode { get; }

    public override string Cause => Retcode.Message;

    public static implicit operator ActionError(ActionRetcode retcode)
    {
        return new ActionError(retcode);
    }

    public override string ToString()
    {
        var writer = new StringWriter();
        writer.WriteLine(Cause);

        if (InnerError is not null)
        {
            writer.WriteLine("=> Error backtrace:");
            writer.Write(InnerError.ToString());
        }

        return writer.ToString();
    }
}