using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     ActionError is a wrapper of ActionRetcode.
/// </summary>
public sealed class ActionError : Error
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
}