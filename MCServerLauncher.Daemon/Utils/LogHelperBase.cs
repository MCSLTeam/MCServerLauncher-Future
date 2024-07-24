using Serilog;

namespace MCServerLauncher.Daemon.Utils;

public abstract class LogHelperBase : ILogHelper
{
    public void Debug(string message)
    {
        Log.Debug(Format(message));
    }

    public void Info(string message)
    {
        Log.Information(Format(message));
    }

    public void Error(string message)
    {
        Log.Error(Format(message));
    }

    public void Warn(string message)
    {
        Log.Warning(Format(message));
    }

    protected abstract string Format(string message);
}