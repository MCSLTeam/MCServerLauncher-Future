using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.ViewModels;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.WPF.Tests;

public sealed class InstanceReportPresentationTests
{
    private static readonly InstanceStatus[] Preview1StatusValues =
    [
        InstanceStatus.Running,
        InstanceStatus.Stopped,
        InstanceStatus.Crashed,
        InstanceStatus.Starting,
        InstanceStatus.Stopping
    ];

    public static TheoryData<InstanceStatus> Preview1Statuses =>
    [
        .. Preview1StatusValues
    ];

    [Theory]
    [MemberData(nameof(Preview1Statuses))]
    public void PresentationMappingPreservesReadyTimeoutAndActiveLifecycleStatus(InstanceStatus status)
    {
        if (status == InstanceStatus.Stopped)
            return;

        using var eventRules = JsonDocument.Parse("{}");
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "ready-timeout",
            "server.jar",
            InstanceType.MCJava,
            TargetType.Jar,
            "1.21.8",
            "utf-8",
            "utf-8",
            "java",
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            eventRules.RootElement,
            ConsoleMode.Pty);
        var report = new ContractInstanceReport(
            status,
            configuration,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<InstancePlayer>.Empty,
            new InstancePerformance(0, 0),
            ProcessId: 42,
            ReadyTimedOut: true);

        var presentation = InstanceDataManager.ToPresentationReport(report);

        Assert.Equal(status, presentation.Status);
        Assert.True(presentation.ReadyTimedOut);
        Assert.Equal(ConsoleMode.Pty, presentation.Config.ConsoleMode);
    }

    [Fact]
    public void PresentationMappingTreatsStoppedReportWithProcessIdAsStarting()
    {
        using var eventRules = JsonDocument.Parse("{}");
        var configuration = new InstanceConfiguration(
            Guid.NewGuid(),
            "booting",
            "server.jar",
            InstanceType.MCJava,
            TargetType.Jar,
            "1.21.8",
            "utf-8",
            "utf-8",
            "java",
            ImmutableArray<string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            eventRules.RootElement,
            ConsoleMode.Pty);
        var report = new ContractInstanceReport(
            InstanceStatus.Stopped,
            configuration,
            ImmutableDictionary<string, string>.Empty,
            ImmutableArray<InstancePlayer>.Empty,
            new InstancePerformance(0, 0),
            ProcessId: 42,
            ReadyTimedOut: false);

        var presentation = InstanceDataManager.ToPresentationReport(report);

        Assert.Equal(InstanceStatus.Starting, presentation.Status);
    }

    [Theory]
    [MemberData(nameof(Preview1Statuses))]
    public void InstanceManagerFilterCanSelectEachPreview1LifecycleStatus(InstanceStatus status)
    {
        foreach (var candidate in Preview1StatusValues)
        {
            Assert.Equal(
                candidate == status,
                InstanceManagerViewModel.MatchesStatusFilter(candidate, status.ToString()));
        }
    }
}
