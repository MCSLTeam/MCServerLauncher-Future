using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Daemon.Utils;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class ConfigCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        var node = dispatcher.Register(ctx =>
            ctx.Literal("cfg")
                .Then(ctx.Literal("json")
                    .Executes(cmd =>
                    {
                        var config = AppConfig.Get();
                        cmd.Source.SendFeedback(
                            "config.json: {0}",
                            JsonConvert.SerializeObject(config, Formatting.Indented, DaemonJsonSettings.Settings)
                        );
                        return 0;
                    }))
                .Then(ctx.Literal("token")
                    .Executes(cmd =>
                    {
                        var config = AppConfig.Get();
                        cmd.Source.SendFeedback("MainToken: {0}", config.MainToken);
                        return 0;
                    })
                ).Then(ctx.Literal("port")
                    .Executes(cmd =>
                    {
                        var config = AppConfig.Get();
                        cmd.Source.SendFeedback("Port: {0}", config.Port);
                        return 0;
                    })
                ).Then(ctx.Literal("reset").Then(
                    ctx.Literal("token")
                        .Executes(cmd =>
                        {
                            if (AppConfig.Get().ResetMainToken())
                            {
                                cmd.Source.SendFeedback("Reset MainToken Successfully");
                                return 0;
                            }

                            {
                                cmd.Source.SendError("Reset MainToken Failed");
                                return 1;
                            }
                        })
                ))
        );
        return node;
    }
}