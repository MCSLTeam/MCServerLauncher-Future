using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     ActionError is a wrapper of ActionRetcode.
/// </summary>
public sealed class ActionError(ActionRetcode? retcode = null) : Error
{
    public ActionRetcode Retcode { get; } = retcode ?? ActionRetcode.UnexpectedError;

    public override string Cause => Retcode.Message;

    public static implicit operator ActionError(ActionRetcode retcode)
    {
        return new ActionError(retcode);
    }
}