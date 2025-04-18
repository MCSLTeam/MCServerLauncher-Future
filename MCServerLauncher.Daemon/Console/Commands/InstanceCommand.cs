using System.Collections.Concurrent;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Utils.Status;

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


                var data = new ConcurrentDictionary<Guid, (long, double)>();
                Parallel.ForEachAsync(
                    manager.Instances.Keys,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 4
                    },
                    async (id, ct) =>
                    {
                        if (manager.Instances.TryGetValue(id, out var inst))
                            try
                            {
                                data.TryAdd(id, await inst.GetMonitorData());
                            }
                            catch
                            {
                                data.TryAdd(id, (-1L, 0.0));
                            }
                    }).Wait();

                foreach (var (id, inst) in manager.Instances)
                    if (data.TryGetValue(id, out var rv))
                        ShowInstanceInformation(source, inst, true, rv.Item1, rv.Item2);
                    else ShowInstanceInformation(source, inst, false, 0, 0);

                source.SendFeedback("共 {0} 个实例", manager.Instances.Count);
                return 0;
            })));
    }

    public static async Task ShowInstanceInformation<TSource>(
        TSource source,
        Instance instance
    )
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback(" - {0} ({1})", instance.Config.Name, instance.Config.Uuid);
        source.SendFeedback("   - 状态: {0}", instance.Status.ToString());

        if (instance.Status is InstanceStatus.Running or InstanceStatus.Starting)
        {
            var (mem, cpu) = await ProcessInfo.GetProcessUsageAsync(instance.ServerProcessId);
            source.SendFeedback("   - PID: {0}", instance.ServerProcessId);
            source.SendFeedback("   - 端口: {0}", instance.Port);
            source.SendFeedback("   - 内存: {0}", GetMemoryString(mem));
            source.SendFeedback("   - CPU: {0} %", cpu);
        }

        source.SendFeedback("");
    }

    public static void ShowInstanceInformation<TSource>(
        TSource source,
        Instance instance,
        bool showInfo,
        long mem,
        double cpu
    )
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback(" - {0} ({1})", instance.Config.Name, instance.Config.Uuid);
        source.SendFeedback("   - 状态: {0}", instance.Status.ToString());

        if (showInfo && instance.Status is InstanceStatus.Running or InstanceStatus.Starting)
        {
            source.SendFeedback("   - PID: {0}", instance.ServerProcessId);
            source.SendFeedback("   - 端口: {0}", instance.Port);
            source.SendFeedback("   - 内存: {0}", GetMemoryString(mem));
            source.SendFeedback("   - CPU: {0} %", cpu);
        }

        source.SendFeedback("");
    }

    private static string GetMemoryString(double bytes)
    {
        if (bytes < 1024) return $"{bytes} B";

        bytes = bytes / 1024;
        if (bytes < 10240) return $"{bytes:F2} KB";

        bytes = bytes / 1024;
        if (bytes < 10240) return $"{bytes:F2} MB";

        bytes = bytes / 1024;
        return $"{bytes:F2} GB";
    }
}