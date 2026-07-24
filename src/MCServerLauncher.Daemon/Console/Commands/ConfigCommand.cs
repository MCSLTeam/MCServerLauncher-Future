using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using System.Text.Json;
using MCServerLauncher.Daemon.Serialization;

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
                        // Never serialize secrets through Serilog sinks. Render the real config
                        // shape (so operators see security/plugins sections) but redact secret
                        // material before it leaves the console-only channel.
                        var config = AppConfig.Get();
                        var json = JsonSerializer.Serialize(config, AppConfig.PersistenceWriteIndentedTypeInfo);
                        var redacted = json
                            .Replace($"\"secret\": \"{config.Secret}\"", "\"secret\": \"<redacted>\"", StringComparison.Ordinal)
                            .Replace($"\"main_token\": \"{config.MainToken}\"", "\"main_token\": \"<redacted>\"", StringComparison.Ordinal);
                        cmd.Source.SendSecret(redacted);
                        return 0;
                    }))
                .Then(ctx.Literal("token")
                    .Executes(cmd =>
                    {
                        // Reveal the main token to the interactive console only; it must not be
                        // persisted to file or network Serilog sinks. Use `cfg reset token` to rotate.
                        var config = AppConfig.Get();
                        cmd.Source.SendSecret($"MainToken: {config.MainToken}");
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
