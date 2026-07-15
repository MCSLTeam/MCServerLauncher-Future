using MCServerLauncher.Common.ProtoType.Action;
using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Remote.Action;

/// <summary>
///     ActionError is a wrapper of ActionRetcode.
/// </summary>
public sealed class ActionError(ActionRetcode? retcode = null)
{
    public ActionRetcode Retcode { get; } = retcode ?? ActionRetcode.UnexpectedError;

    [JsonIgnore]
    public Exception? CausedException { get; private set; }

    public string Cause => Retcode.Message;

    public ActionError CauseBy(Exception exception)
    {
        CausedException = exception;
        return this;
    }

    public override string ToString()
    {
        return Cause + Environment.NewLine;
    }

    public static implicit operator ActionError(ActionRetcode retcode)
    {
        return new ActionError(retcode);
    }
}
