using System;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient;

public class DaemonRequestException : Exception
{
    public readonly ActionRetcode Retcode;

    public DaemonRequestException(ActionRetcode retcode, string message) : base(message)
    {
        Retcode = retcode;
    }
}

public class DaemonRequestLimitException : Exception
{
}