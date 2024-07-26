using MCServerLauncher.Daemon.Utils;

namespace MCServerLauncher.Daemon.Remote;

public class RemoteLogHelper : LogHelperBase
{
    protected override string Format(string message)
    {
        return "[Remote] " + message;
    }
}