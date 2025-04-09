using Serilog;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Console;

public class HelpCommand : IConsoleCommand
{
    public ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts)
    {
        Log.Information("[Console] ==================== Commands ====================");
        foreach (var (kw, c) in app.CommandMap)
        {
            Log.Information($"[Console] {kw} - {c.HelpString}");
        }
        Log.Information("[Console] ==================================================");
        return ValueTask.CompletedTask;
    }

    public string HelpString => "获取命令帮助列表";
}