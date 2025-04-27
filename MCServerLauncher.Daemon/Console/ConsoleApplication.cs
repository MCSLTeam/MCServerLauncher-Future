using Brigadier.NET;
using Brigadier.NET.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Http;
using Exception = System.Exception;

namespace MCServerLauncher.Daemon.Console;

public class ConsoleApplication
{
    public const int Exit = -255;

    public ConsoleApplication(IHttpService httpService)
    {
        HttpService = httpService;
    }

    private IHttpService HttpService { get; }

    public void Serve()
    {
        var consoleTask = Task.Factory.StartNew(() =>
        {
            var commandSource = new ConsoleCommandSource(HttpService);
            var dispatcher = new CommandDispatcher<ConsoleCommandSource>().RegisterCommands();
            Log.Information("[Console] Console Application is running, type 'help' for help.");

            var gs = commandSource.GetRequiredService<GracefulShutdown>();
            try
            {
                while (!gs.CancellationToken.IsCancellationRequested)
                {
                    var line = System.Console.In.ReadLine();
                    gs.CancellationToken.ThrowIfCancellationRequested();
                    if (line is null) return;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var execute = dispatcher.Execute(line, commandSource);
                        if (execute == Exit) return;
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
                    var parsed = dispatcher.Parse(line,commandSource);
                    var suggestions = dispatcher.GetCompletionSuggestions(parsed).Result;
                    Log.Debug("Suggestions: Range={0}, List={1}",suggestions.Range,suggestions.List.Select(x=>x.ToString()));
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
            }
        }, TaskCreationOptions.LongRunning);

        var gs = HttpService.Resolver.GetRequiredService<GracefulShutdown>();
        gs.OnShutdown += () => consoleTask.Wait();
    }
}