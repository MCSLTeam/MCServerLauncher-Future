using Brigadier.NET;
using Brigadier.NET.Exceptions;
using TouchSocket.Http;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Console;

public class ConsoleApplication
{
    public ConsoleApplication(IHttpService httpService)
    {
        HttpService = httpService;
    }

    public IHttpService HttpService { get; }

    public async Task Serve(CancellationTokenSource cts)
    {
        var wait = Task.Delay(-1, cts.Token);
        var loop = Task.Run(async () =>
        {
            var commandSource = new ConsoleCommandSource(HttpService, cts);
            var dispatcher = new CommandDispatcher<ConsoleCommandSource>().RegisterCommands();

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = System.Console.ReadLine();
                    cts.Token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var execute = dispatcher.Execute(line, commandSource);
                    }
                    catch (CommandSyntaxException e)
                    {
                        var keyword = e.Input.Split(' ').FirstOrDefault("");
                        if (dispatcher.GetRoot().GetChild(keyword) is null)
                        {
                            commandSource.SendError("Unknown command: '{Command}'.", line);
                            dispatcher.Execute("help", commandSource);
                        }
                        else
                        {
                            commandSource.SendError(e.Message);
                        }
                    }
                    catch (Exception e)
                    {
                        commandSource.SendError(
                            "An error occurred while executing the command: '{Command}', {Message}.", line, e.Message);
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
            }
        }, cts.Token);
        await Task.WhenAny(wait, loop);
    }
}