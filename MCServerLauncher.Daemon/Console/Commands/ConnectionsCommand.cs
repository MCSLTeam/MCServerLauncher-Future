using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Daemon.Remote;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class ConnectionsCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("conn")
                .Then(ctx.Literal("list")
                    .Executes(cmd =>
                    {
                        var source = cmd.Source;
                        var container = source.GetRequiredService<WsContextContainer>();
                        var service = source.GetRequiredService<IHttpService>();

                        var clientIds = container.GetClientIds().ToArray();
                        source.SendFeedback("当前Websocket客户端连接数: {Count}", clientIds.Length);
                        foreach (var clientId in clientIds)
                            if (service.TryGetClient(clientId, out var client))
                            {
                                var context = container.GetContext(clientId);
                                if (context is null) continue;
                                ShowClientInformation(source, client!, context);
                            }

                        return 0;
                    })
                ).Then(ctx.Literal("expire_all")
                    .Executes(cmd =>
                    {
                        var source = cmd.Source;

                        WsVerifyHandler.RejectWithNoReason = true;
                        AppConfig.Get().ResetSecret();
                        WsVerifyHandler.RejectWithNoReason = false;

                        var container = source.GetRequiredService<WsContextContainer>();
                        var service = source.GetRequiredService<IHttpService>();
                        var clientIds = container.GetClientIds().ToArray();

                        foreach (var clientId in clientIds)
                            if (service.TryGetClient(clientId, out var client))
                                client.WebSocket.SafeClose("Daemon administrator manual expired this connection");

                        source.SendFeedback("已过期并关闭所有Websocket客户端连接");
                        return 0;
                    }))
                .Then(ctx.Argument("cid", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var container = source.GetRequiredService<WsContextContainer>();
                    var service = source.GetRequiredService<IHttpService>();
                    var clientId = Arguments.GetString(cmd, "cid") ?? "";

                    if (service.TryGetClient(clientId, out var client))
                    {
                        var context = container.GetContext(clientId);
                        if (context is not null)
                        {
                            ShowClientInformation(source, client!, context);
                            return 0;
                        }
                    }

                    source.SendError("未找到该客户端");
                    return 1;
                }))
        );
        return node;
    }

    private static void ShowClientInformation<TSource>(TSource source, IHttpSessionClient client, WsContext context)
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback("- {CID}", context.ClientId);
        source.SendFeedback("  - IP: {IP}", client.GetIPPort());
        source.SendFeedback("  - JTI: {Jti}",
            context.JTI == Guid.Empty ? context.JTI + " (MainToken登录, JTI无意义)" : context.JTI);
        source.SendFeedback("  - 权限: {Permissions}", context.Permissions.ToString());
        source.SendFeedback("  - 到期时间: {Time}", context.ExpiredTo);
    }
}