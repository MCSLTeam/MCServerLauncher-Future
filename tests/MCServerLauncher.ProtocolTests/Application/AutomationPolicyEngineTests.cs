using System.Collections.Concurrent;
using MCServerLauncher.Common.Contracts.Automation;
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
        Assert.Contains("dropped 1 record(s)", evaluation.Reason, StringComparison.Ordinal);
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

        internal Harness(DateTimeOffset? start = null, TimeSpan? sampleInterval = null)
        {
            _sampleInterval = sampleInterval;
            Time = new ManualTimeProvider(start ?? DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
            MonitoringRoot = Directory.CreateTempSubdirectory("mcsl-automation-metrics-").FullName;
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
                        new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1024, 512, "C:\\"),
                        [new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1024, 512, "C:\\")],
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
        public Guid? AttachConsole(Guid instanceId, Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler) => throw new NotSupportedException();
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
