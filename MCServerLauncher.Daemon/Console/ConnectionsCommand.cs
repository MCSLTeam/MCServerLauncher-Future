using MCServerLauncher.Daemon.Remote;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Console;

public class ConnectionsCommand : IConsoleCommand
{
    public ValueTask Run(string[] commandArgs, ConsoleApplication app, CancellationTokenSource cts)
    {
        var container = app.HttpService.Resolver.GetRequiredService<WsContextContainer>();
        var clientIds = container.GetClientIds().ToArray();
        Log.Information("[Console] 当前Websocket客户端连接数: {Count}", clientIds.Length);
        foreach (var clientId in clientIds)
        {
            if (app.HttpService.TryGetClient(clientId, out var client))
            {
                var context = container.GetContext(clientId);
                Log.Information("[Console] - {CID}", clientId);
                Log.Information("[Console]   - IP: {IP}", client.GetIPPort());
                Log.Information("[Console]   - 权限: {Permissions}", context?.Permissions.ToString());
                Log.Information("[Console]   - 到期时间: {Time}", context?.ExpiredTo);
            }
        }

        return default;
    }

    public string HelpString => "打印当前所有的Websocket客户端连接信息";
}