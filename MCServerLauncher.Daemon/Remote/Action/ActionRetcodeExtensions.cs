using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.Daemon.Remote.Action;

public static class ActionRetcodeExtensions
{
    public static ActionError ToError(this ActionRetcode code)
    {
        return new ActionError(code);
    }
}