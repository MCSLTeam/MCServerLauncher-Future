using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Console;

public class StopCommand : IConsoleCommand
{
    public ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts)
    {
        cts.Cancel();
        return ValueTask.CompletedTask;
    }

    public string HelpString => "关闭daemon";
}