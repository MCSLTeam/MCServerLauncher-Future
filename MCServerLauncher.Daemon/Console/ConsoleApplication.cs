using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Console;

public class ConsoleApplication
{
    public IHttpService HttpService { get; }
    public Dictionary<string, IConsoleCommand> CommandMap { get; } = new();

    public ConsoleApplication(IHttpService httpService)
    {
        HttpService = httpService;
        AddCommand<HelpCommand>("help");
    }

    public async Task Serve(CancellationTokenSource cts)
    {
        var wait = Task.Delay(-1,cts.Token);
        var loop = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    System.Console.Write(">> ");
                    var line = System.Console.ReadLine();
                    cts.Token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var commands = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (CommandMap.TryGetValue(commands[0], out var command))
                        await command.Run(commands, this, cts);
                    else
                    {
                        Log.Information("[Console] Unknown command: {Command}", commands[0]);
                        await CommandMap["help"].Run(Array.Empty<string>(), this, cts);
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
            }
        }, cts.Token);
        await Task.WhenAny(wait, loop);
    }

    public ConsoleApplication AddCommand<TConsoleCommand>(string keyword) where TConsoleCommand : IConsoleCommand
    {
        CommandMap.Add(keyword, Activator.CreateInstance<TConsoleCommand>());
        return this;
    }
}