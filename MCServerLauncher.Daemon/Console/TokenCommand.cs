using Serilog;

namespace MCServerLauncher.Daemon.Console;

public class TokenCommand : IConsoleCommand
{
    public ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts)
    {
        Log.Information("[Console] MainToken: {MainToken}", AppConfig.Get().MainToken);
        return ValueTask.CompletedTask;
    }

    public string HelpString => "打印MainToken";
}