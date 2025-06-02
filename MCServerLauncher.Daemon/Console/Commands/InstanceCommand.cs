using System.Collections.Concurrent;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Extensions;
using MCServerLauncher.Daemon.Management.Minecraft;
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

                source.SendFeedback("正在加载 ...");
                var infos = new ConcurrentDictionary<Guid, (long, double)>();
                Parallel.ForEachAsync(
                    manager.Instances.Keys,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 4
                    },
                    async (id, ct) =>
                    {
                        if (manager.Instances.TryGetValue(id, out var inst))
                            if (inst.Status is InstanceStatus.Running or InstanceStatus.Starting)
                                infos.TryAdd(id, await ProcessInfo.GetProcessUsageAsync(inst.ServerProcessId));
                    }).Wait();

                foreach (var (id, inst) in manager.Instances)
                    if (infos.TryGetValue(id, out var rv))
                        ShowInstanceInformation(source, inst, true, rv.Item1, rv.Item2);
                    else ShowInstanceInformation(source, inst, false, 0, 0);

                source.SendFeedback(
                    "共 {0} 个实例, 内存占用 {1}, CPU利用率 {2} %",
                    manager.Instances.Count,
                    GetMemoryString(infos.Sum(x => x.Value.Item1)),
                    infos.Sum(x => x.Value.Item2)
                );
                return 0;
            })));
    }

    private static void ShowInstanceInformation<TSource>(
        TSource source,
        IInstance instance,
        bool showInfo,
        long mem,
        double cpu
    )
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback("");
        source.SendFeedback(" - {0} ({1})", instance.Config.Name, instance.Config.Uuid);
        source.SendFeedback("   - 状态: {0}", instance.Status.ToString());

        if (showInfo && instance.Status is InstanceStatus.Running or InstanceStatus.Starting)
        {
            source.SendFeedback("   - PID: {0}", instance.ServerProcessId);
            if (instance.TryCastTo<MinecraftInstance>(out var mcInstance))
                source.SendFeedback("   - 端口: {0}", mcInstance!.Port);
            source.SendFeedback("   - 内存: {0}", GetMemoryString(mem));
            source.SendFeedback("   - CPU: {0} %", cpu);
        }
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