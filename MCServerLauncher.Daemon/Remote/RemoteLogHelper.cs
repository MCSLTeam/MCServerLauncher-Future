using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Remote;

using Serilog;

public class RemoteLogHelper : LogHelperBase
{
    protected override string Format(string message)
    {
        return " [Remote] " + message;
    }
}