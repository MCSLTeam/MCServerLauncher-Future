using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Tree;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.Console;
using ApplicationInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

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
                var application = source.GetRequiredService<IDaemonApplication>();
                var shutdown = source.GetRequiredService<GracefulShutdown>();
                if (!TryGetReports(source, application, shutdown.CancellationToken, out var reports))
                    return 1;

                source.SendFeedback("正在加载 ...");
                foreach (var (id, report) in reports.Reports.OrderBy(static report => report.Key))
                    ShowInstanceInformation(source, id, report);

                source.SendFeedback(
                    "共 {0} 个实例 内存占用 {1}, CPU利用率 {2} %",
                    reports.Reports.Count,
                    GetMemoryString(reports.Reports.Values.Sum(static report => report.PerformanceCounter.MemoryBytes)),
                    reports.Reports.Values.Sum(static report => report.PerformanceCounter.Cpu));
                return 0;
            }))
            .Then(ctx.Literal("start")
                .Then(ctx.Argument("target", Arguments.String()).Executes(cmd =>
                {
                    var source = cmd.Source;
                    var application = source.GetRequiredService<IDaemonApplication>();
                    var shutdown = source.GetRequiredService<GracefulShutdown>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, application, target, shutdown.CancellationToken, out var instanceId)) return 1;

                    var result = application.Instances.StartInstanceAsync(
                            new InstanceReference(instanceId),
                            shutdown.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    if (result.IsErr(out _))
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
                    var application = source.GetRequiredService<IDaemonApplication>();
                    var shutdown = source.GetRequiredService<GracefulShutdown>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, application, target, shutdown.CancellationToken, out var instanceId)) return 1;

                    var result = application.Instances.StopInstanceAsync(
                            new InstanceReference(instanceId),
                            shutdown.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    if (result.IsErr(out _))
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
                    var application = source.GetRequiredService<IDaemonApplication>();
                    var shutdown = source.GetRequiredService<GracefulShutdown>();
                    var target = Arguments.GetString(cmd, "target") ?? string.Empty;
                    if (!TryResolveTarget(source, application, target, shutdown.CancellationToken, out var instanceId)) return 1;

                    var result = application.Instances.HaltInstanceAsync(
                            new InstanceReference(instanceId),
                            shutdown.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    if (result.IsErr(out _))
                    {
                        source.SendError("无法发送强制停止信号给实例 '{Instance}'.", target);
                        return 1;
                    }

                    source.SendFeedback("已向实例 '{Instance}' 发送强制停止信号", target);
                    return 0;
                }))));
    }

    internal static TargetResolution ResolveTarget(
        IReadOnlyCollection<KeyValuePair<Guid, ApplicationInstanceReport>> snapshot,
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
        IDaemonApplication application,
        string target,
        CancellationToken cancellationToken,
        out Guid instanceId)
        where TSource : ConsoleCommandSource
    {
        if (!TryGetReports(source, application, cancellationToken, out var reports))
        {
            instanceId = default;
            return false;
        }

        var resolution = ResolveTarget(reports.Reports, target);
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

    private static bool TryGetReports<TSource>(
        TSource source,
        IDaemonApplication application,
        CancellationToken cancellationToken,
        out InstanceReportList reports)
        where TSource : ConsoleCommandSource
    {
        var result = application.Instances.ListInstanceReportsAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (result.IsOk(out reports!))
            return true;

        source.SendError("无法读取实例报告.");
        reports = null!;
        return false;
    }

    internal sealed record TargetResolution(Guid? InstanceId, Guid[] AmbiguousInstanceIds);

    private static void ShowInstanceInformation<TSource>(
        TSource source,
        Guid instanceId,
        ApplicationInstanceReport report)
        where TSource : ConsoleCommandSource
    {
        source.SendFeedback("");
        source.SendFeedback(" - {0} ({1})", report.Config.Name, instanceId);
        source.SendFeedback("   - 状态: {0}", report.Status.ToString());

        if (report.Status == InstanceStatus.Running)
        {
            if (report.ProcessId is int processId)
                source.SendFeedback("   - PID: {0}", processId);
            source.SendFeedback("   - 内存: {0}", GetMemoryString(report.PerformanceCounter.MemoryBytes));
            source.SendFeedback("   - CPU: {0} %", report.PerformanceCounter.Cpu);
        }
    }

    private static string GetMemoryString(double bytes)
    {
        if (bytes < 1024) return $"{bytes} B";

        bytes /= 1024;
        if (bytes < 10240) return $"{bytes:F2} KB";

        bytes /= 1024;
        if (bytes < 10240) return $"{bytes:F2} MB";

        bytes /= 1024;
        return $"{bytes:F2} GB";
    }
}
