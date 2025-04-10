using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class ExitCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("exit")
                .Executes(cmd =>
                {
                    cmd.Source.Cts.Cancel();
                    return 0;
                })
        );
        return node;
    }
}