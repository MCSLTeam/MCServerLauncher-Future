using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class HelpCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("help")
                .Executes(cmd =>
                {
                    var source = cmd.Source;
                    source.SendFeedback("======================== Commands ========================");

                    dispatcher.GetRoot().Children.ForEach(node =>
                    {
                        if (node is LiteralCommandNode<TSource> literal)
                        {
                            var usage = string.Join("|", dispatcher.GetAllUsage(node, cmd.Source, false));
                            source.SendFeedback(
                                "- Command: {0} {1}",
                                literal.Literal,
                                usage
                            );

                            
                            source.SendFeedback("  - Description: {0}", CommandManager.CommandDescriptionDictionary[literal.Literal]);
                        }
                    });
                    source.SendFeedback("==========================================================");
                    return 0;
                })
        );
        return node;
    }
}