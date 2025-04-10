namespace MCServerLauncher.Daemon.Console;

public interface IConsoleCommand
{
    string HelpString { get; }
    ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts);
}