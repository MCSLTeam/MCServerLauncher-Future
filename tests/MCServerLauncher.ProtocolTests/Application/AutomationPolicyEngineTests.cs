using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.ApplicationCore.Automation;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.ApplicationCore.Monitoring;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;
using ContractInstanceConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceConfiguration;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;
using ProtoInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;
using InstanceFactoryConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceFactoryConfiguration;
using InstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult;
using UpdateInstanceSettingsRequest = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest;
using UpdateInstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult;

namespace MCServerLauncher.ProtocolTests;

public sealed class AutomationPolicyEngineTests
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PolicyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Store_AppliesWithVersionCasAndRejectsInvalidDocuments()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-automation-store-").FullName;
        var store = new AutomationPolicyStore(root);

        Assert.Empty(store.Get().Policies);
        Assert.Equal(0, store.Get().Version);

        var applied = store.Apply(Document(CrashGuardPolicy(), version: 0));
        Assert.Equal(1L, applied.Unwrap());

        var stale = store.Apply(Document(CrashGuardPolicy(), version: 0));
        Assert.True(stale.IsErr(out var staleError));
        Assert.Equal("automation.version_conflict", staleError!.Code);

        var invalid = Document(new AutomationPolicy { Id = PolicyId, Name = "", Trigger = null }, version: 1);
        var rejected = store.Apply(invalid);
        Assert.True(rejected.IsErr(out var rejectedError));
        Assert.Equal("automation.policy_invalid", rejectedError!.Code);

        var toggled = store.Enable(PolicyId, enabled: false, expectedVersion: 1);
        Assert.Equal(2L, toggled.Unwrap());
        Assert.False(store.Get().Policies.Single().Enabled);
        Assert.True(store.Enable(Guid.NewGuid(), true, 2).IsErr(out var missing));
        Assert.Equal("automation.policy_not_found", missing!.Code);

        // The document survives a reload; a corrupted document starts empty instead of half-applied.
        var reloaded = new AutomationPolicyStore(root);
        Assert.Equal(2, reloaded.Get().Version);
        Assert.Single(reloaded.Get().Policies);
        File.WriteAllText(Path.Combine(root, "policies.json"), "{broken");
        var corrupt = new AutomationPolicyStore(root);
        Assert.Empty(corrupt.Get().Policies);
        Assert.Equal(0, corrupt.Get().Version);
    }

    [Fact]
    public void Validator_RejectsNestedConfirmationPlansAndUnknownShapes()
    {
        var nested = Document(new AutomationPolicy
        {
            Id = PolicyId,
            Name = "nested",
            Trigger = new UnexpectedExitTrigger(),
            Actions =
            [
                new ConfirmationPlanAction
                {
                    Summary = "approve",
                    Deferred = new ConfirmationPlanAction { Summary = "inner", Deferred = new StopInstanceAction() }
                }
            ]
        }, version: 0);
        Assert.Contains(
            AutomationPolicyValidator.Validate(nested),
            diagnostic => diagnostic.Message.Contains("cannot defer another", StringComparison.Ordinal));

        var badMetric = Document(new AutomationPolicy
        {
            Id = PolicyId,
            Name = "bad-metric",
            Trigger = new SustainedMetricTrigger { Metric = "bogus", Threshold = 1, SustainedSeconds = 30 },
            Actions = [new StopInstanceAction { InstanceId = InstanceId }]
        }, version: 0);
        Assert.Contains(
            AutomationPolicyValidator.Validate(badMetric),
            diagnostic => diagnostic.Code == "automation.trigger_invalid");
    }

    [Fact]
    public async Task Evaluator_RestartsACrashedInstanceOnceAndThenHonoursCooldownAndBackoff()
    {
        using var harness = new Harness();
        harness.Store.Apply(Document(CrashGuardPolicy(cooldownSeconds: 3600), version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Instances.Started);

        instance.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Started);

        // The restart left the instance running; one tick observes that, then a fresh crash within
        // the cooldown is suppressed, and the suppression itself is audited.
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry => entry.ErrorCode == "cooldown" && !entry.Succeeded);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Principal == AutomationEvaluator.ServicePrincipalSubject &&
            entry.Permission == "instance.restart" &&
            entry.Succeeded);
    }

    [Fact]
    public async Task Evaluator_WildcardCrashTriggerRepairsEveryInstanceThatCrashedInTheSameTick()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "fleet-guard",
            Trigger = new UnexpectedExitTrigger(),
            Actions = [new RestartInstanceAction { BackoffBaseSeconds = 30, BackoffMaxSeconds = 1800 }],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var second = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var third = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var a = harness.AddInstance(InstanceId, InstanceStatus.Running);
        var b = harness.AddInstance(second, InstanceStatus.Running);
        var c = harness.AddInstance(third, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // One correlated failure, one firing: the cooldown must not strand the other instances.
        a.Status = InstanceStatus.Crashed;
        b.Status = InstanceStatus.Crashed;
        c.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        Assert.Equal([InstanceId, second, third], harness.Instances.Started.Order());
    }

    [Fact]
    public async Task Evaluator_CrashLoopEvidenceIsConsumedSoARecoveredInstanceIsNotRestartedAgain()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "loop-guard",
            Trigger = new CrashLoopTrigger { InstanceId = InstanceId, MaxCrashes = 2, WindowSeconds = 600 },
            Actions = [new RestartInstanceAction { BackoffBaseSeconds = 1, BackoffMaxSeconds = 2 }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        for (var index = 0; index < 2; index++)
        {
            instance.Status = InstanceStatus.Crashed;
            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
            instance.Status = InstanceStatus.Running;
            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        }

        var afterLoop = harness.Instances.Started.Count;
        Assert.True(afterLoop >= 1, "the crash loop should have been repaired at least once");

        // The instance is healthy now. Stale crash timestamps inside the window must not keep
        // re-firing the trigger and restarting a server that already recovered.
        for (var index = 0; index < 5; index++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(60));
            await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        }

        Assert.Equal(afterLoop, harness.Instances.Started.Count);
    }

    [Fact]
    public async Task Evaluator_DeferredRestartAuditsBothHalves()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:10:00+00:00"));
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "window-restart",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 60 },
            Actions = [new RestartInstanceAction { InstanceId = InstanceId, BackoffBaseSeconds = 1, BackoffMaxSeconds = 2 }],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Running);

        // A running instance is stopped first; only that half has happened, so only that half is
        // claimed in the audit history.
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
        Assert.Empty(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry => entry.Permission == "instance.restart.stop" && entry.Succeeded);
        Assert.DoesNotContain(harness.Audit.Events, entry => entry.Permission == "instance.restart");

        // The second half runs on a later tick and carries its own record.
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart.start" &&
            entry.Succeeded &&
            entry.Target == InstanceId.ToString("D"));
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRefusesToFireAcrossAnUnobservedHole()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 600
        };
        harness.SystemCpu = 95;
        for (var index = 0; index < 4; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        // The daemon was down for five minutes; restarting writes the gap marker.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        using var afterRestart = harness.CreateSamplerOnSameHistory();
        for (var index = 0; index < 4; index++)
        {
            await afterRestart.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        // Above threshold on both sides of the hole, but nobody observed the middle.
        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("gap", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRefusesToFireAcrossAFailedSamplerTick()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 60
        };
        harness.SystemCpu = 95;
        for (var index = 0; index < 2; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        // One tick cannot read the machine. It observed nothing, so it leaves a marked hole rather
        // than a silence the evaluator would read as an uninterrupted stretch above the threshold.
        harness.SamplingFails = true;
        await Assert.ThrowsAsync<IOException>(() => harness.Metrics.SampleOnceAsync(CancellationToken.None));
        harness.SamplingFails = false;
        harness.Time.Advance(TimeSpan.FromSeconds(15));

        for (var index = 0; index < 2; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("recorded gap", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRefusesAWindowLongerThanItCanReadLosslessly()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 40000
        };
        harness.SystemCpu = 95;

        // Two samples eleven hours apart say nothing about the eleven hours between them, and the
        // window is longer than the lossless read budget, so it cannot be judged at all.
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(40000));
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("2000-sample lossless evaluation budget", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricSeesADipThatDownsamplingHides()
    {
        using var harness = new Harness(sampleInterval: TimeSpan.FromSeconds(1));
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 60
        };

        for (var second = 0; second <= 60; second++)
        {
            if (second > 0)
                harness.Time.Advance(TimeSpan.FromSeconds(1));
            // The dip sits inside the first downsampling bucket, whose last point is above the
            // threshold, so only the raw series still carries it.
            harness.SystemCpu = second == 3 ? 10 : 95;
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        }

        var now = harness.Time.GetUtcNow();
        var downsampled = harness.Metrics.Query(new MonitoringQuery(now - TimeSpan.FromSeconds(60), now, 6));
        Assert.All(downsampled.Samples, sample => Assert.True(sample.SystemCpuPercent >= 90));

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, now);
        Assert.False(evaluation.Fires);
        Assert.Contains("below threshold", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRefusesWhenRetentionRetiredTheWindowHead()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 600
        };
        harness.SystemCpu = 95;
        for (var index = 0; index < 20; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        // Retention retires the oldest segment: the first half of the window is no longer evidence
        // anybody holds, however healthy the retained half looks.
        foreach (var segment in Directory.EnumerateFiles(harness.MonitoringRoot, "metrics-*.jsonl"))
            File.Delete(segment);

        for (var index = 0; index < 20; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("retention", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRefusesWhenTheHistoryDroppedARecord()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 60
        };
        harness.SystemCpu = 95;
        for (var index = 0; index < 3; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        // The segment is readable but not writable, so the next append is counted as a dropped
        // record instead of thrown; the point it carried is simply gone.
        var segment = Directory.EnumerateFiles(harness.MonitoringRoot, "metrics-*.jsonl").Single();
        using (new FileStream(segment, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        }

        Assert.Equal(1, harness.Metrics.DroppedRecords);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("write failure inside the window", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricRecoversOnceTheWindowClearsTheDroppedRecord()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_cpu",
            Threshold = 90,
            SustainedSeconds = 60
        };
        harness.SystemCpu = 95;
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(15));

        var segment = Directory.EnumerateFiles(harness.MonitoringRoot, "metrics-*.jsonl").Single();
        using (new FileStream(segment, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        }

        Assert.Equal(1, harness.Metrics.DroppedRecords);

        // Sample past the hole until the sustained window no longer reaches back to it. The drop
        // count stays at 1 forever, so an evaluator judging on the count could never recover.
        for (var index = 0; index < 8; index++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        }

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());

        Assert.Equal(1, harness.Metrics.DroppedRecords);
        Assert.True(evaluation.Fires, evaluation.Reason);
    }

    [Fact]
    public async Task Test_RunsConcurrentlyWithTicksWithoutCorruptingSharedState()
    {
        using var harness = new Harness();
        harness.Store.Apply(Document(CrashGuardPolicy(cooldownSeconds: 0), version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        // mcsl.automation.test reaches Test() on RPC threads while the tick loop mutates the same
        // fact and guard state.
        var stop = false;
        var dryRuns = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
                harness.Evaluator.Test();
        });

        for (var index = 0; index < 200; index++)
        {
            instance.Status = index % 2 == 0 ? InstanceStatus.Crashed : InstanceStatus.Running;
            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        }

        Volatile.Write(ref stop, true);
        await dryRuns;
    }

    [Fact]
    public async Task Provisioning_RefusesToExecuteAnAutomationIntentPlan()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "guarded-stop",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions =
            [
                new ConfirmationPlanAction
                {
                    Summary = "approve the stop",
                    Deferred = new StopInstanceAction { InstanceId = InstanceId }
                }
            ],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        var plan = Assert.Single(harness.PlanKernel.ListActive(AutomationIntents.PlanKind));
        harness.PlanKernel.Confirm(plan.PlanId, plan.PlanHash, "user-a").Unwrap();

        // Plan kinds are not interchangeable: a confirmed automation intent must not be consumable
        // through the provisioning executor, which would read its payload as a factory config.
        var provisioning = new LocalProvisioningApplication(
            harness.PlanKernel,
            harness.Instances,
            new ThrowingOperationApplication());
        var executed = await provisioning.ExecuteAsync(
            new ProvisioningExecuteRequest(plan.PlanId, "user-a"),
            CancellationToken.None);

        Assert.True(executed.IsErr(out var error));
        Assert.Equal("plan.kind_mismatch", error!.Code);
        // The rejected admission re-opens the plan instead of consuming it.
        Assert.Equal(PlanStatus.Ready, harness.PlanKernel.Get(plan.PlanId).Unwrap().Status);
    }

    [Fact]
    public async Task Evaluator_DailyCapStopsExecutionsUntilTheNextUtcDay()
    {
        using var harness = new Harness();
        var policy = CrashGuardPolicy(cooldownSeconds: 0);
        policy.MaxExecutionsPerDay = 2;
        policy.Actions = [new RestartInstanceAction { BackoffBaseSeconds = 3600, BackoffMaxSeconds = 7200 }];
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        // First crash restarts; the second within the same day trips the restart backoff; after
        // two executions the daily cap suppresses the policy before its actions even run.
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.Instances.Started);

        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry => entry.ErrorCode == "automation.backoff");

        instance.Status = InstanceStatus.Running;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Contains(harness.Audit.Events, entry => entry.ErrorCode == "daily execution cap");

        // A new UTC day resets the cap and the policy fires again.
        instance.Status = InstanceStatus.Running;
        harness.Time.Advance(TimeSpan.FromDays(1));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal(2, harness.Instances.Started.Count);
    }

    [Fact]
    public async Task Evaluator_NotificationAndMaintenanceWindow()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T03:10:00+00:00"));
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "window-notify",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 3, StartMinuteUtc = 0, DurationMinutes = 30 },
            Actions = [new NotificationAction { Title = "window", Message = "inside", Severity = "Info" }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        var published = Assert.Single(harness.DomainEvents.Notifications);
        Assert.Equal("window", published.Title);

        var outcomes = harness.Evaluator.Test();
        var outcome = Assert.Single(outcomes);
        Assert.True(outcome.WouldFire);

        harness.Time.Advance(TimeSpan.FromHours(2));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.DomainEvents.Notifications);
        Assert.False(Assert.Single(harness.Evaluator.Test()).WouldFire);
    }

    [Fact]
    public async Task ConfirmationPlan_FilesOneIntentBoundToTheFirstHumanConfirmer()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "guarded-stop",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions =
            [
                new ConfirmationPlanAction
                {
                    Summary = "approve the stop",
                    Deferred = new StopInstanceAction { InstanceId = InstanceId }
                }
            ],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        var plan = Assert.Single(harness.PlanKernel.ListActive(AutomationIntents.PlanKind));
        Assert.Equal(PlanStatus.Blocked, plan.Status);
        Assert.Equal(AutomationEvaluator.ServicePrincipalSubject, plan.CreatorPrincipal);

        // The same firing policy files one plan, not a flood.
        instance.Status = InstanceStatus.Running;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.PlanKernel.ListActive(AutomationIntents.PlanKind));

        // The filing service can never close the confirmation gate it opened.
        Assert.True(harness.PlanKernel
            .Confirm(plan.PlanId, plan.PlanHash, AutomationEvaluator.ServicePrincipalSubject)
            .IsErr(out var selfConfirm));
        Assert.Equal("plan.forbidden", selfConfirm!.Code);

        // Service-filed plans bind to the first human confirmer, and execution requires that human.
        var confirmed = harness.PlanKernel.Confirm(plan.PlanId, plan.PlanHash, "user-a");
        Assert.Equal("user-a", confirmed.Unwrap().ConfirmedBy);
        Assert.Equal(PlanStatus.Ready, confirmed.Unwrap().Status);
        Assert.True(harness.PlanKernel.Confirm(plan.PlanId, plan.PlanHash, "user-b").IsErr(out var foreign));
        Assert.Equal("plan.forbidden", foreign!.Code);
        Assert.True(harness.PlanKernel.TryBeginExecute(plan.PlanId, "user-b").IsErr(out var wrongExecutor));
        Assert.Equal("plan.forbidden", wrongExecutor!.Code);
        Assert.True(harness.PlanKernel.TryBeginExecute(plan.PlanId, AutomationEvaluator.ServicePrincipalSubject)
            .IsErr(out var creatorExecutor));
        Assert.Equal("plan.forbidden", creatorExecutor!.Code);
        var begun = harness.PlanKernel.TryBeginExecute(plan.PlanId, "user-a");
        Assert.True(begun.IsOk(out _));

        // The deferred action executes under the service principal when finally approved.
        harness.PlanKernel.AbortExecuteAdmission(plan.PlanId);
        var payloadOk = AutomationIntents.TryParsePayload(
            harness.PlanKernel.Get(plan.PlanId).Unwrap().Payload,
            out var payload,
            out var deferred);
        Assert.True(payloadOk);
        Assert.Equal(PolicyId, payload.PolicyId);
        var executed = await harness.Evaluator.ExecuteDeferredAsync(deferred, payload.TargetInstanceId, CancellationToken.None);
        Assert.True(executed.IsOk(out _));
        Assert.Equal([InstanceId], harness.Instances.Stopped);
    }

    [Fact]
    public async Task Evaluator_SustainedMetricUsesRetainedHistory()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "hot-cpu",
            Trigger = new SustainedMetricTrigger
            {
                Metric = "system_cpu",
                Threshold = 90,
                SustainedSeconds = 45
            },
            Actions = [new NotificationAction { Title = "hot", Message = "cpu", Severity = "Warning" }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();

        harness.SystemCpu = 95;
        for (var index = 0; index < 4; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        var (fires, _, _) = harness.Evaluator.EvaluateTrigger(policy.Trigger!, harness.Time.GetUtcNow());
        Assert.True(fires);

        harness.SystemCpu = 10;
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        var (firesAfterDip, _, _) = harness.Evaluator.EvaluateTrigger(policy.Trigger!, harness.Time.GetUtcNow());
        Assert.False(firesAfterDip);
    }

    [Fact]
    public async Task Evaluator_SustainedDiskJudgesUsedPercentOfTheWholeVolume()
    {
        using var harness = new Harness();
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_disk_percent",
            Threshold = 90,
            SustainedSeconds = 45
        };

        harness.DiskTotalBytes = 1000;
        harness.DiskFreeBytes = 50;
        for (var index = 0; index < 4; index++)
        {
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
            harness.Time.Advance(TimeSpan.FromSeconds(15));
        }

        Assert.True(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);

        // Space freed up once: the condition is no longer sustained across the whole window.
        harness.DiskFreeBytes = 500;
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        var afterCleanup = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(afterCleanup.Fires);
        Assert.Contains("below threshold", afterCleanup.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record written before the history carried disk readings measured no disk at all. Reading
    /// that as "not above the threshold" would be a verdict on evidence nobody collected, which is
    /// exactly the distinction the monitoring contract keeps null for.
    /// </summary>
    [Fact]
    public async Task Evaluator_SustainedDiskRefusesToJudgeASampleThatMeasuredNoDisk()
    {
        const string legacySample =
            """
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[]}
            """;
        using var harness = new Harness(seedHistoryJsonl: legacySample);
        var trigger = new SustainedMetricTrigger
        {
            Metric = "system_disk_percent",
            Threshold = 90,
            SustainedSeconds = 45
        };

        harness.DiskTotalBytes = 1000;
        harness.DiskFreeBytes = 50;
        for (var index = 0; index < 3; index++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(15));
            await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        }

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("no disk reading", evaluation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_UnresponsiveReadsTheNewestSampleAndTreatsAnUnmeasuredSilenceAsQuiet()
    {
        // Silence is measured from process output, which no in-process fake produces, so the record
        // carrying it is seeded the way a previous daemon run would have written it.
        const string silentSample =
            """
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[{"instance_id":"11111111-1111-1111-1111-111111111111","name":"demo","status":"running","cpu_percent":1.5,"memory_bytes":1024,"silent_seconds":600}],"disk_total_bytes":1024,"disk_free_bytes":512}
            """;
        using var harness = new Harness(seedHistoryJsonl: silentSample);
        var trigger = new UnresponsiveInstanceTrigger { SilentSeconds = 300 };

        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.True(evaluation.Fires);
        Assert.Equal([InstanceId], evaluation.Targets);

        // A threshold above the observed silence is not met.
        Assert.False(harness.Evaluator
            .EvaluateTrigger(new UnresponsiveInstanceTrigger { SilentSeconds = 900 }, harness.Time.GetUtcNow())
            .Fires);

        // The newest sample never measured the silence. Unmeasured is not responsive and not silent
        // either, so nothing fires on it.
        harness.AddInstance(InstanceId, InstanceStatus.Running);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        var unmeasured = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(unmeasured.Fires);

        // And a history that stopped covering the present says so rather than reusing a stale read.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        var stale = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.False(stale.Fires);
        Assert.Contains("covers the present", stale.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluator_StatusDurationFiresOnlyOnceTheStatusHasHeldLongEnough()
    {
        using var harness = new Harness();
        var trigger = new StatusDurationTrigger { Status = InstanceStatus.Stopped, DurationSeconds = 120 };
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "stuck-stopped",
            Trigger = trigger,
            Actions = [new NotificationAction { Title = "stopped", Message = "still down", Severity = "Warning" }],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Stopped);

        // The duration is measured from the evaluator's own first observation, so nothing fires
        // before it has watched the status for that long.
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.False(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.DomainEvents.Notifications);

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        var fired = Assert.Single(harness.DomainEvents.Notifications);
        Assert.Equal("stopped", fired.Title);
        var evaluation = harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow());
        Assert.Equal([InstanceId], evaluation.Targets);

        // Leaving the status restarts the clock: the old elapsed time belongs to a spell that ended.
        instance.Status = InstanceStatus.Running;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Stopped;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.False(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);
    }

    /// <summary>
    /// The hold one policy places is enforced against every other policy, not only against the one
    /// that wrote it, and the refusal is audited rather than silently dropped.
    /// </summary>
    [Fact]
    public async Task Evaluator_MaintenanceHoldRefusesAnotherPolicysRestartUntilItExpires()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var maintenance = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "nightly-maintenance",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions =
            [
                new MaintenanceStateAction
                {
                    InstanceId = InstanceId,
                    DurationSeconds = 600,
                    Reason = "operator is patching the world"
                }
            ],
            CooldownSeconds = 0
        };
        var restartGuard = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "crash-guard",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions = [new RestartInstanceAction()],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [maintenance, restartGuard], Version = 0 }).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Contains(harness.Audit.Events, entry => entry.Permission == "instance.maintenance" && entry.Succeeded);

        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart" &&
            entry.ErrorCode == "automation.suppressed" &&
            !entry.Succeeded);

        // The hold is visible to a reader while it lasts.
        var held = Assert.Single(harness.Evaluator.ActiveSuppressions());
        Assert.Equal(InstanceId, held.InstanceId);
        Assert.Equal(AutomationSuppressionScope.All, held.Scope);
        Assert.Equal(PolicyId, held.PolicyId);
        Assert.Equal("operator is patching the world", held.Reason);

        // Past the window and past the hold, the same crash is repaired normally.
        harness.Time.Advance(TimeSpan.FromMinutes(11));
        instance.Status = InstanceStatus.Running;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Evaluator.ActiveSuppressions());

        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Started);
    }

    [Fact]
    public async Task Evaluator_RestartHoldRefusesTheRestartAndStillAllowsTheStop()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var suppression = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "no-auto-restart",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new RestartSuppressionAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "under investigation" }],
            CooldownSeconds = 0
        };
        var crashGuard = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "crash-guard",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions = [new RestartInstanceAction(), new StopInstanceAction()],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [suppression, crashGuard], Version = 0 }).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // A restart hold exists to leave an instance down, so the stop it needs still runs.
        Assert.Empty(harness.Instances.Started);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart" && entry.ErrorCode == "automation.suppressed");
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.stop" && entry.Succeeded);
        Assert.Equal(AutomationSuppressionScope.Restarts, Assert.Single(harness.Evaluator.ActiveSuppressions()).Scope);

        // The dry run reports the hold without writing anything.
        var outcomes = harness.Evaluator.Test();
        Assert.Contains(outcomes, outcome => outcome.Reason.Contains("Restarts' suppression", StringComparison.Ordinal));
        Assert.Single(harness.Evaluator.ActiveSuppressions());
    }

    [Fact]
    public async Task Evaluator_AuditRecordActionWritesTheAuthoredMessage()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "note-it",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 30 },
            Actions = [new AuditRecordAction { Message = "disk is filling but nothing was done", Severity = "Warning" }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        var recorded = Assert.Single(harness.Audit.Events, entry => entry.Permission.StartsWith("audit.record", StringComparison.Ordinal));
        Assert.Equal("audit.record:Warning", recorded.Permission);
        Assert.Equal(AutomationEvaluator.ServicePrincipalSubject, recorded.Principal);
        Assert.Equal("disk is filling but nothing was done", recorded.Target);
        Assert.True(recorded.Succeeded);
    }

    [Fact]
    public void Validator_RejectsImpossibleShapesOfTheNewTriggersAndActions()
    {
        Assert.Empty(Diagnostics(
            new SustainedMetricTrigger { Metric = "system_disk_percent", Threshold = 90, SustainedSeconds = 45 },
            new NotificationAction { Title = "full", Message = "disk", Severity = "Warning" }));

        Assert.Contains(
            Diagnostics(new UnresponsiveInstanceTrigger { SilentSeconds = 0 }, new StopInstanceAction()),
            diagnostic => diagnostic.Code == "automation.trigger_invalid");

        Assert.Contains(
            Diagnostics(new StatusDurationTrigger { DurationSeconds = 0 }, new StopInstanceAction()),
            diagnostic => diagnostic.Code == "automation.trigger_invalid");

        Assert.Contains(
            Diagnostics(new StatusDurationTrigger { Status = (InstanceStatus)99 }, new StopInstanceAction()),
            diagnostic => diagnostic.Message.Contains("Unknown instance status", StringComparison.Ordinal));

        // Nothing lifts a suppression early, so a typo must not be able to hold an instance forever.
        Assert.Contains(
            Diagnostics(new UnexpectedExitTrigger(), new MaintenanceStateAction { DurationSeconds = (int)TimeSpan.FromDays(8).TotalSeconds }),
            diagnostic => diagnostic.Message.Contains("cannot exceed", StringComparison.Ordinal));

        Assert.Contains(
            Diagnostics(new UnexpectedExitTrigger(), new RestartSuppressionAction { DurationSeconds = 0 }),
            diagnostic => diagnostic.Code == "automation.action_invalid");

        Assert.Contains(
            Diagnostics(new UnexpectedExitTrigger(), new AuditRecordAction { Message = "   " }),
            diagnostic => diagnostic.Message.Contains("needs a message", StringComparison.Ordinal));

        Assert.Contains(
            Diagnostics(new UnexpectedExitTrigger(), new AuditRecordAction { Message = new string('x', 1025) }),
            diagnostic => diagnostic.Message.Contains("1024 characters", StringComparison.Ordinal));

        Assert.Contains(
            Diagnostics(new UnexpectedExitTrigger(), new AuditRecordAction { Message = "hi", Severity = "Fatal" }),
            diagnostic => diagnostic.Message.Contains("Unknown audit severity", StringComparison.Ordinal));
    }

    /// <summary>
    /// The union converters are the outermost gate a wire policy passes, and a malformed field there
    /// must be refused rather than coerced: a dropped instance_id silently widens a single-instance
    /// policy into a fleet-wide one, and validation runs too late to see the loss.
    /// </summary>
    [Fact]
    public void Converter_RejectsMalformedTriggerAndActionPayloads()
    {
        Assert.Contains("instance.unresponsive", Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{\"type\":\"instance.telepathy\"}", ApplicationContractJsonContext.Default.AutomationTrigger)).Message);

        Assert.Contains("audit.record", Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{\"type\":\"instance.reboot_world\"}", ApplicationContractJsonContext.Default.AutomationAction)).Message);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{\"instance_id\":null}", ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"silent_seconds\":\"600\"}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"instance_id\":\"not-a-guid\"}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.status_duration\",\"status\":\"hibernating\"}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.maintenance\",\"duration_seconds\":600.5}",
            ApplicationContractJsonContext.Default.AutomationAction));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"audit.record\",\"message\":42}",
            ApplicationContractJsonContext.Default.AutomationAction));
    }

    [Fact]
    public void Converter_RoundTripsEveryNewTriggerAndActionKind()
    {
        var trigger = new StatusDurationTrigger
        {
            InstanceId = InstanceId,
            Status = InstanceStatus.Starting,
            DurationSeconds = 90
        };
        var triggerJson = JsonSerializer.Serialize<AutomationTrigger>(trigger, ApplicationContractJsonContext.Default.AutomationTrigger);
        Assert.Contains("\"status\":\"starting\"", triggerJson, StringComparison.Ordinal);
        var restoredTrigger = Assert.IsType<StatusDurationTrigger>(
            JsonSerializer.Deserialize(triggerJson, ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Equal(InstanceStatus.Starting, restoredTrigger.Status);
        Assert.Equal(90, restoredTrigger.DurationSeconds);
        Assert.Equal(InstanceId, restoredTrigger.InstanceId);

        var unresponsive = Assert.IsType<UnresponsiveInstanceTrigger>(RoundTrip<AutomationTrigger>(
            new UnresponsiveInstanceTrigger { SilentSeconds = 450 },
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Equal(450, unresponsive.SilentSeconds);
        Assert.Null(unresponsive.InstanceId);

        var maintenance = Assert.IsType<MaintenanceStateAction>(RoundTrip<AutomationAction>(
            new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" },
            ApplicationContractJsonContext.Default.AutomationAction));
        Assert.Equal("patching", maintenance.Reason);
        Assert.Equal(600, maintenance.DurationSeconds);

        var restartSuppression = Assert.IsType<RestartSuppressionAction>(RoundTrip<AutomationAction>(
            new RestartSuppressionAction { DurationSeconds = 60 },
            ApplicationContractJsonContext.Default.AutomationAction));
        Assert.Equal(60, restartSuppression.DurationSeconds);

        var auditRecord = Assert.IsType<AuditRecordAction>(RoundTrip<AutomationAction>(
            new AuditRecordAction { Message = "noted", Severity = "Error" },
            ApplicationContractJsonContext.Default.AutomationAction));
        Assert.Equal("noted", auditRecord.Message);
        Assert.Equal("Error", auditRecord.Severity);
    }

    private static T RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, typeInfo), typeInfo)!;

    private static ImmutableArray<AutomationPolicyDiagnostic> Diagnostics(
        AutomationTrigger trigger,
        AutomationAction action) =>
        AutomationPolicyValidator.Validate(Document(
            new AutomationPolicy
            {
                Id = PolicyId,
                Name = "candidate",
                Trigger = trigger,
                Actions = [action]
            },
            version: 0));

    private static AutomationPolicySet Document(AutomationPolicy policy, long version) =>
        new() { Policies = [policy], Version = version };

    private static AutomationPolicy CrashGuardPolicy(int cooldownSeconds = 300) => new()
    {
        Id = PolicyId,
        Name = "crash-guard",
        Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
        Actions = [new RestartInstanceAction { BackoffBaseSeconds = 30, BackoffMaxSeconds = 1800 }],
        CooldownSeconds = cooldownSeconds
    };

    private sealed class Harness : IDisposable
    {
        private readonly TimeSpan? _sampleInterval;

        internal Harness(DateTimeOffset? start = null, TimeSpan? sampleInterval = null, string? seedHistoryJsonl = null)
        {
            _sampleInterval = sampleInterval;
            Time = new ManualTimeProvider(start ?? DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
            MonitoringRoot = Directory.CreateTempSubdirectory("mcsl-automation-metrics-").FullName;
            // A history the sampler finds already on disk. Some readings — a measured silence above
            // all — come from process state no in-process fake can produce, so the record carrying
            // them is written the way a previous daemon run would have left it.
            if (seedHistoryJsonl is not null)
                File.WriteAllText(Path.Combine(MonitoringRoot, "metrics-000000.jsonl"), seedHistoryJsonl + "\n");
            var storeRoot = Directory.CreateTempSubdirectory("mcsl-automation-policies-").FullName;
            var planRoot = Directory.CreateTempSubdirectory("mcsl-automation-plans-").FullName;
            Manager = new FakeInstanceManager();
            Metrics = CreateSamplerOnSameHistory();
            Store = new AutomationPolicyStore(storeRoot);
            PlanKernel = new PlanKernel(Time, planRoot);
            Instances = new RecordingInstanceApplication(Manager);
            Audit = new RecordingAuditSink();
            DomainEvents = new RecordingDomainEventPort();
            Evaluator = new AutomationEvaluator(
                Store,
                Manager,
                Metrics,
                Instances,
                DomainEvents,
                PlanKernel,
                Audit,
                Time);
        }

        internal ManualTimeProvider Time { get; }
        internal FakeInstanceManager Manager { get; }
        internal string MonitoringRoot { get; }
        internal MonitoringSampler Metrics { get; }
        internal AutomationPolicyStore Store { get; }
        internal PlanKernel PlanKernel { get; }
        internal RecordingInstanceApplication Instances { get; }
        internal RecordingAuditSink Audit { get; }
        internal RecordingDomainEventPort DomainEvents { get; }
        internal AutomationEvaluator Evaluator { get; }
        internal double SystemCpu { get; set; } = 5.5;
        internal ulong DiskTotalBytes { get; set; } = 1024;
        internal ulong DiskFreeBytes { get; set; } = 512;

        /// <summary>When set, a sampling tick cannot read the machine and fails.</summary>
        internal bool SamplingFails { get; set; }

        internal TestInstance AddInstance(Guid id, InstanceStatus status)
        {
            var instance = new TestInstance(id, $"instance-{id:N}", status);
            Manager.Instances[id] = instance;
            return instance;
        }

        /// <summary>
        /// A second sampler over the same retained history, which is how a daemon restart appears
        /// to the metrics log: it writes the gap marker for the time nobody observed.
        /// </summary>
        internal MonitoringSampler CreateSamplerOnSameHistory() =>
            new(
                new DaemonMonitoringConfig(),
                Manager,
                new DelegateSystemInfoCell(() => SamplingFails
                    ? throw new IOException("the machine counters are unavailable")
                    : new SystemInfo(
                        new OperatingSystemInfo("Windows", "x64"),
                        new ProcessorInfo("vendor", "cpu", 16, SystemCpu, 8, 16),
                        new MemoryInfo(32768, 16384),
                        new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", DiskTotalBytes, DiskFreeBytes, "C:\\"),
                        [new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", DiskTotalBytes, DiskFreeBytes, "C:\\")],
                        "2.0.0")),
                Time,
                MonitoringRoot,
                interval: _sampleInterval);

        public void Dispose()
        {
            Evaluator.Dispose();
            Metrics.Dispose();
        }
    }

    private sealed class ThrowingOperationApplication : IOperationApplication
    {
        public Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(OperationListQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(OperationReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(OperationCancelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        internal List<AuditEvent> Events { get; } = [];

        public void Record(AuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class RecordingDomainEventPort : IDomainEventPort
    {
        internal List<ClientNotificationDomainEvent> Notifications { get; } = [];

        public DomainEventOwner CreateOwner(string name) => new(name);

        public void Subscribe<TEvent>(DomainEventOwner owner, Func<TEvent, CancellationToken, ValueTask> handler)
            where TEvent : IDomainEvent
        {
        }

        public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            if (domainEvent is ClientNotificationDomainEvent notification)
                Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }

        public void DisposeOwner(DomainEventOwner owner)
        {
        }
    }

    /// <summary>
    /// Minimal IInstanceApplication double: start/stop mutate the fake instance statuses and
    /// record the call order; everything else is unreachable from automation.
    /// </summary>
    private sealed class RecordingInstanceApplication(FakeInstanceManager manager) : IInstanceApplication
    {
        internal List<Guid> Started { get; } = [];

        internal List<Guid> Stopped { get; } = [];

        public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference request, CancellationToken cancellationToken)
        {
            Started.Add(request.InstanceId);
            if (manager.Instances.TryGetValue(request.InstanceId, out var instance) && instance is TestInstance test)
                test.Status = InstanceStatus.Running;
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }

        public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference request, CancellationToken cancellationToken)
        {
            Stopped.Add(request.InstanceId);
            if (manager.Instances.TryGetValue(request.InstanceId, out var instance) && instance is TestInstance test)
                test.Status = InstanceStatus.Stopped;
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }

        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(ConsoleOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(ConsoleResizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseConsoleAsync(ConsoleSessionReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteConsoleAsync(Guid sessionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ContractInstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestInstance(Guid id, string name, InstanceStatus status) : IInstance
    {
        public InstanceConfig Config { get; } = new() { Uuid = id, Name = name, Target = "server.jar" };

        public InstanceProcess? Process => null;

        public InstanceStatus Status { get; set; } = status;

        public int ServerProcessId => -1;

        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }

        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<ProtoInstanceReport> GetReportAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> StopAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private sealed class DelegateSystemInfoCell(Func<SystemInfo> factory) : IAsyncTimedLazyCell<SystemInfo>
    {
        public ValueTask<SystemInfo> Value => ValueTask.FromResult(factory());

        public DateTime LastUpdated => DateTime.UtcNow;

        public TimeSpan CacheDuration => TimeSpan.FromSeconds(2);

        public bool IsExpired() => false;

        public Task Update() => Task.CompletedTask;
    }

    private sealed class FakeInstanceManager : IInstanceManager
    {
        public ConcurrentDictionary<Guid, IInstance> Instances { get; } = new();

        public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; } = new();

        public Task<Result<ContractInstanceConfiguration, DaemonError>> TryAddInstance(InstanceFactoryConfiguration setting, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryRemoveInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IInstance?> TryStartInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryStopInstance(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public bool SendToInstance(Guid instanceId, string message) => throw new NotSupportedException();
        public bool TryWriteConsole(Guid instanceId, ReadOnlyMemory<byte> data) => throw new NotSupportedException();
        public bool TryResizeConsole(Guid instanceId, ushort columns, ushort rows) => throw new NotSupportedException();
        public Guid? AttachConsole(Guid instanceId, Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler, bool replayHistory = true) => throw new NotSupportedException();
        public void DetachConsole(Guid instanceId, Guid subscriberId) => throw new NotSupportedException();
        public Task KillInstanceAsync(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProtoInstanceReport?> GetInstanceReport(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<Guid, ProtoInstanceReport>> GetAllReports(CancellationToken ct = default) => throw new NotSupportedException();
        public bool TryGetInstanceLog(Guid instanceId, out IReadOnlyList<string> logs) => throw new NotSupportedException();
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettings(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettings(UpdateInstanceSettingsRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public IDisposable AcquireInstanceMutation(Guid instanceId) => throw new NotSupportedException();
        public ValueTask<IDisposable> AcquireInstanceMutationAsync(Guid instanceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopAllInstances(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
