using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Console;

public interface IConsoleCommand
{
    ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts);
    string HelpString { get; }
}