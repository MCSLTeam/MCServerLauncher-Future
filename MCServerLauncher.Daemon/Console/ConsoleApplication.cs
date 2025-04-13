using Brigadier.NET;
using Brigadier.NET.Exceptions;
using Serilog;
using TouchSocket.Http;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Console;

public class ConsoleApplication
{
    public ConsoleApplication(IHttpService httpService)
    {
        HttpService = httpService;
    }

    private IHttpService HttpService { get; }

    public Task Serve(CancellationTokenSource cts)
    {
        return Task.Factory.StartNew(() =>
        {
            var commandSource = new ConsoleCommandSource(HttpService, cts);
            var dispatcher = new CommandDispatcher<ConsoleCommandSource>().RegisterCommands();
            Log.Information("[Console] Console Application is running, type 'help' for help.");

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
        }, TaskCreationOptions.LongRunning);
    }
}