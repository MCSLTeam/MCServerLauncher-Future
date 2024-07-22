namespace MCServerLauncher.Daemon.Remote;
using Serilog;

public static class LogHelper
{
    public static void Debug(string message)
    {
        Log.Debug($"[Remote] {message}");
    }
    
    public static void Info(string message)
    {
        Log.Information($"[Remote] {message}");
    }
    
    public static void Error(string message)
    {
        Log.Error($"[Remote] {message}");
    }
    
    public static void Warn(string message)
    {
        Log.Warning($"[Remote] {message}");
    }
}