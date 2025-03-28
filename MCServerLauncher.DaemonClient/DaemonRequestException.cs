using System;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient;

public class DaemonRequestException : Exception
{
    public readonly ActionReturnCode ReturnCode;

    public DaemonRequestException(ActionReturnCode returnCode, string message) : base(message)
    {
        ReturnCode = returnCode;
    }
}

public class DaemonRequestLimitException : Exception
{
}