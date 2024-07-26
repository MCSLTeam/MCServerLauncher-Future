namespace MCServerLauncher.Daemon.Utils;

public interface ILogHelper
{
    void Debug(string message);
    void Info(string message);
    void Error(string message);
    void Warn(string message);
}