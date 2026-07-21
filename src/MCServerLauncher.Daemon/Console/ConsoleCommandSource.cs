using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Console;

public class ConsoleCommandSource
{
    public ConsoleCommandSource(IHttpService httpService)
    {
        HttpService = httpService;
    }

    private IHttpService HttpService { get; }

    public TService GetRequiredService<TService>()
        where TService : notnull
    {
        return HttpService.Resolver.GetRequiredService<TService>();
    }

    public TService? GetService<TService>()
        where TService : notnull
    {
        return HttpService.Resolver.GetService<TService>();
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void SendFeedback(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Information("[Console] " + messageTemplate, propertyValues);
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    public void SendError(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Error("[Console] " + messageTemplate, propertyValues);
    }

    /// <summary>
    /// Writes sensitive output directly to the interactive console without routing it through
    /// Serilog sinks, so file/network sinks never persist secrets such as the main token.
    /// Only meaningful for an interactive console session.
    /// </summary>
    public void SendSecret(string value)
    {
        System.Console.WriteLine(value);
    }
}