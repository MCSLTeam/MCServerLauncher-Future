using Brigadier.NET;
using Brigadier.NET.Tree;
using MCServerLauncher.Daemon.Console.Commands;

namespace MCServerLauncher.Daemon.Console;

public static class CommandManager
{
    public static readonly Dictionary<string, string> CommandDescriptionDictionary = new();

    public static CommandDispatcher<TCommandSource> RegisterCommands<TCommandSource>
    (
        this CommandDispatcher<TCommandSource> dispatcher
    )
        where TCommandSource : ConsoleCommandSource
    {
        dispatcher.RegisterCommand(ExitCommand.Register, "退出");
        dispatcher.RegisterCommand(HelpCommand.Register, "打印帮助");
        dispatcher.RegisterCommand(TokenCommand.Register, "打印daemon配置文件细节");
        dispatcher.RegisterCommand(ConnectionsCommand.Register, "打印当前所有的Websocket客户端连接信息");

        return dispatcher;
    }

    private static void RegisterCommand<TSource>(
        this CommandDispatcher<TSource> dispatcher,
        Func<CommandDispatcher<TSource>, LiteralCommandNode<TSource>> registerFactory,
        string description
    )
        where TSource : ConsoleCommandSource
    {
        var command = registerFactory(dispatcher);
        CommandDescriptionDictionary[command.Literal] = description;
    }
}