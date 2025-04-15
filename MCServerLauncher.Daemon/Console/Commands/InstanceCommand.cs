using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Daemon.Minecraft.Server;

namespace MCServerLauncher.Daemon.Console.Commands;

public static class InstanceCommand
{
    public static LiteralCommandNode<TSource> Register<TSource>(this CommandDispatcher<TSource> dispatcher)
        where TSource : ConsoleCommandSource
    {
        return dispatcher.Register(ctx => ctx.Literal("inst")
            .Then(ctx.Literal("list").Executes(cmd =>
            {
                var source = cmd.Source;
                var manager = source.GetRequiredService<IInstanceManager>();
                foreach (var (_, instance) in manager.Instances)
                {
                    ShowInstanceInformation(source, instance);
                }

                source.SendFeedback("共 {0} 个实例", manager.Instances.Count);
                return 0;
            })));
    }

    public static void ShowInstanceInformation<TSource>(TSource source, Instance instance)
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback(" - {0} ({1})", instance.Config.Name, instance.Config.Uuid);
        source.SendFeedback("   - 状态: {0}", instance.Status.ToString());
    }
}