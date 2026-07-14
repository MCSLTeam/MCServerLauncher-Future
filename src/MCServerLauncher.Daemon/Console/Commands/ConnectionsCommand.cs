using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using Serilog;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class ConnectionsCommand
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("conn")
                .Then(ctx.Literal("list")
                    .Executes(cmd => ListConnections(cmd.Source)))
                .Then(ctx.Literal("expire_all")
                    .Executes(cmd => ExpireAllConnections(cmd.Source)))
                .Then(ctx.Argument("cid", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var connections = source.GetRequiredService<IV2ConnectionAdministration>();
                    var clientId = Arguments.GetString(cmd, "cid") ?? "";

                    if (connections.TryGet(clientId, out var connection))
                    {
                        ShowClientInformation(source, connection);
                        return 0;
                    }

                    source.SendError("未找到 WebSocket 客户端 '{ClientId}'。", clientId);
                    return 1;
                }))
        );
        return node;
    }

    private static int ListConnections<TSource>(TSource source)
        where TSource : ConsoleCommandSource
    {
        var connections = source.GetRequiredService<IV2ConnectionAdministration>();
        var snapshot = connections.Snapshot();
        source.SendFeedback("当前 V2 WebSocket 客户端连接数: {Count}", snapshot.Length);
        foreach (var connection in snapshot)
            ShowClientInformation(source, connection);
        return 0;
    }

    private static int ExpireAllConnections<TSource>(TSource source)
        where TSource : ConsoleCommandSource
    {
        AppConfig.Get().ResetSecret();
        var connections = source.GetRequiredService<IV2ConnectionAdministration>();
        Task<int>? closeTask = null;
        try
        {
            closeTask = connections.CloseAllAsync();
            var closed = closeTask.WaitAsync(CloseTimeout).GetAwaiter().GetResult();
            source.SendFeedback("已过期凭据并关闭 {Count} 个 V2 WebSocket 客户端连接。", closed);
        }
        catch (TimeoutException)
        {
            Observe(closeTask!);
            source.SendFeedback("已过期凭据；部分 V2 WebSocket 客户端连接仍在关闭中。");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[ConnectionsCommand] Failed to close V2 WebSocket client connections after expiring credentials.");
            source.SendError("已过期凭据；关闭 V2 WebSocket 客户端连接时发生错误。");
            return 1;
        }

        return 0;
    }

    private static void ShowClientInformation<TSource>(TSource source, V2ConnectionSnapshot connection)
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback("- {CID}", connection.ConnectionId);
        source.SendFeedback("  - IP: {IP}", connection.RemoteEndpoint);
        source.SendFeedback("  - JTI: {Jti}",
            connection.TokenId == Guid.Empty ? "主令牌" : connection.TokenId.ToString());
        source.SendFeedback("  - 权限: {Permissions}", string.Join(", ", connection.Permissions));
        source.SendFeedback("  - 到期时间: {Time}", connection.ExpiresAt.ToLocalTime());
    }

    private static void Observe(Task task) => _ = task.ContinueWith(static completed => _ = completed.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
