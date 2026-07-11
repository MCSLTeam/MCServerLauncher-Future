using System.Collections.Concurrent;
using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon;
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
                            if (inst.Status == InstanceStatus.Running)
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
            }))
            .Then(ctx.Literal("start")
                .Then(ctx.Argument("target", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var manager = source.GetRequiredService<IInstanceManager>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, manager, target, out var instanceId)) return 1;

                    var shutdown = source.GetRequiredService<GracefulShutdown>();
                    var instance = manager.TryStartInstance(instanceId, shutdown.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    if (instance is null)
                    {
                        source.SendError("无法启动实例 '{Instance}'.", target);
                        return 1;
                    }

                    source.SendFeedback("已启动实例 '{Instance}'.", target);
                    return 0;
                })))
            .Then(ctx.Literal("stop")
                .Then(ctx.Argument("target", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var manager = source.GetRequiredService<IInstanceManager>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, manager, target, out var instanceId)) return 1;

                    if (!manager.TryStopInstance(instanceId))
                    {
                        source.SendError("无法请求停止实例 '{Instance}'.", target);
                        return 1;
                    }

                    source.SendFeedback("已请求停止实例 '{Instance}'.", target);
                    return 0;
                })))
            .Then(ctx.Literal("halt")
                .Then(ctx.Argument("target", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var manager = source.GetRequiredService<IInstanceManager>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, manager, target, out var instanceId)) return 1;

                    manager.KillInstance(instanceId);
                    source.SendFeedback("已向实例 '{Instance}' 发送强制停止信号.", target);
                    return 0;
                }))));
    }

    internal static TargetResolution ResolveTarget(
        IReadOnlyCollection<KeyValuePair<Guid, IInstance>> snapshot,
        string target)
    {
        if (Guid.TryParse(target, out var targetId))
        {
            foreach (var (instanceId, _) in snapshot)
                if (instanceId == targetId)
                    return new TargetResolution(instanceId, []);
        }

        var matchingInstanceIds = snapshot
            .Where(pair => string.Equals(pair.Value.Config.Name, target, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .OrderBy(static instanceId => instanceId)
            .ToArray();

        return matchingInstanceIds.Length switch
        {
            0 => new TargetResolution(null, []),
            1 => new TargetResolution(matchingInstanceIds[0], []),
            _ => new TargetResolution(null, matchingInstanceIds)
        };
    }

    private static bool TryResolveTarget<TSource>(
        TSource source,
        IInstanceManager manager,
        string target,
        out Guid instanceId)
        where TSource : ConsoleCommandSource
    {
        var resolution = ResolveTarget(manager.Instances.ToArray(), target);
        if (resolution.InstanceId is Guid resolvedInstanceId)
        {
            instanceId = resolvedInstanceId;
            return true;
        }

        instanceId = default;
        if (resolution.AmbiguousInstanceIds.Length > 0)
        {
            source.SendError(
                "实例目标 '{Instance}' 不明确，匹配的 UUID: {Candidates}.",
                target,
                string.Join(", ", resolution.AmbiguousInstanceIds));
        }
        else
        {
            source.SendError("未找到实例 '{Instance}'.", target);
        }

        return false;
    }

    internal sealed record TargetResolution(Guid? InstanceId, Guid[] AmbiguousInstanceIds);

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

        if (showInfo && instance.Status == InstanceStatus.Running)
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
