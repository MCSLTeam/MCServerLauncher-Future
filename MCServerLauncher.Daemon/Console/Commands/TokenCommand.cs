using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class TokenCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("config")
                .Then(
                    ctx.Literal("token")
                        .Executes(cmd =>
                        {
                            var config = AppConfig.Get();
                            cmd.Source.SendFeedback("MainToken: {0}", config.MainToken);
                            return 0;
                        })
                )
        );
        return node;
    }
}