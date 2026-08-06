using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Audit;
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
    private static readonly Guid SecondInstanceId = Guid.Parse("44444444-4444-4444-4444-444444444444");

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

        var segment = Directory.EnumerateFiles(harness.MonitoringRoot, "metrics-*.jsonl").Single();
        File.Delete(segment);
        Directory.CreateDirectory(segment);
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        Directory.Delete(segment);

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
        File.Delete(segment);
        Directory.CreateDirectory(segment);
        await harness.Metrics.SampleOnceAsync(CancellationToken.None);
        Directory.Delete(segment);

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
        Assert.Contains(outcomes, outcome =>
            outcome.Reason.Contains($"actions held for {InstanceId:D}", StringComparison.Ordinal));
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

        var recorded = Assert.Single(harness.Audit.Events, entry => entry.Permission == "audit.record");
        Assert.Equal(AutomationEvaluator.ServicePrincipalSubject, recorded.Principal);
        // No instance fired this window trigger, so there is nothing to file it under.
        Assert.Equal(string.Empty, recorded.Target);
        // The authored text is the record's detail. ErrorCode is a closed vocabulary of outcome
        // codes, and it is null on every record that succeeded — this one included.
        Assert.Equal("Warning: disk is filling but nothing was done", recorded.Detail);
        Assert.Null(recorded.ErrorCode);
        Assert.True(recorded.Succeeded);
    }

    /// <summary>
    /// The validator bounds the authored message at 1024, but the composed detail carries a severity
    /// prefix on top of it, so a message at the limit would otherwise store an over-long field.
    /// </summary>
    [Fact]
    public async Task Evaluator_AuditRecordDetailIsBoundedAfterTheSeverityPrefixIsAdded()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var message = new string('x', AutomationPolicyValidator.MaximumAuditMessageLength);
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "long-note",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 30 },
            Actions = [new AuditRecordAction { Message = message, Severity = "Warning" }],
            CooldownSeconds = 0
        };
        // The message itself is exactly at the validator's limit, so the policy is legal.
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        var recorded = Assert.Single(harness.Audit.Events, entry => entry.Permission == "audit.record");
        Assert.Equal(AutomationPolicyValidator.MaximumAuditMessageLength, recorded.Detail!.Length);
        Assert.StartsWith("Warning: xxx", recorded.Detail, StringComparison.Ordinal);
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

    /// <summary>
    /// A property the kind does not define is rejected, not ignored. A misspelled instance_id would
    /// otherwise read as absent, and absent means every instance on the wildcard-capable kinds — so
    /// a policy written for one instance would quietly act on the whole fleet.
    /// </summary>
    [Fact]
    public void Converter_RejectsAPropertyOutsideTheKindsOwnFields()
    {
        var misspelled = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"instanceId\":\"" + InstanceId + "\",\"silent_seconds\":300}",
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Contains("instanceId", misspelled.Message, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.status_duration\",\"instance-id\":\"" + InstanceId + "\",\"status\":\"stopped\"}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        // The rule is uniform across the union, not a rule the new kinds carry alone.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.crash_loop\",\"instanceId\":\"" + InstanceId + "\",\"max_crashes\":3}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart_suppression\",\"instanceId\":\"" + InstanceId + "\",\"duration_seconds\":60}",
            ApplicationContractJsonContext.Default.AutomationAction));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart\",\"backoff_base\":30}",
            ApplicationContractJsonContext.Default.AutomationAction));

        // A nested deferred action is read through the same gate.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"plan.confirmation\",\"summary\":\"s\",\"deferred\":{\"type\":\"instance.stop\",\"instanceId\":\"" + InstanceId + "\"}}",
            ApplicationContractJsonContext.Default.AutomationAction));
    }

    [Fact]
    public void Converter_RejectsExplicitNullOnAScalarButKeepsItMeaningfulOnInstanceId()
    {
        // Null on instance_id is the wildcard, and the writer emits it, so it must keep round-tripping.
        var wildcard = Assert.IsType<UnresponsiveInstanceTrigger>(JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"instance_id\":null,\"silent_seconds\":300}",
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Null(wildcard.InstanceId);

        // Null on a scalar asserts a value the field cannot hold; silently substituting the default
        // is the same quiet coercion an unknown property would be.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"silent_seconds\":null}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.status_duration\",\"status\":null}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"audit.record\",\"message\":null}",
            ApplicationContractJsonContext.Default.AutomationAction));
    }

    [Fact]
    public void Converter_RejectsMalformedRestartSuppressionAndDiskMetricPayloads()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart_suppression\",\"duration_seconds\":\"600\"}",
            ApplicationContractJsonContext.Default.AutomationAction));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart_suppression\",\"reason\":42}",
            ApplicationContractJsonContext.Default.AutomationAction));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart_suppression\",\"instance_id\":\"not-a-guid\"}",
            ApplicationContractJsonContext.Default.AutomationAction));

        // The disk metric is a value on the existing trigger, so it inherits that trigger's strictness.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"metric.sustained\",\"metric\":\"system_disk_percent\",\"threshold\":\"95\"}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"metric.sustained\",\"metric\":\"system_disk_percent\",\"sustained_seconds\":300.5}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        var disk = Assert.IsType<SustainedMetricTrigger>(JsonSerializer.Deserialize(
            "{\"type\":\"metric.sustained\",\"metric\":\"system_disk_percent\",\"instance_id\":null,\"threshold\":95,\"sustained_seconds\":300}",
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Equal("system_disk_percent", disk.Metric);
        Assert.Equal(95, disk.Threshold);
    }

    [Fact]
    public void Validator_RejectsMetricThresholdsThatCouldNeverBeReached()
    {
        Assert.Contains(
            Diagnostics(
                new SustainedMetricTrigger { Metric = "system_disk_percent", Threshold = 150, SustainedSeconds = 60 },
                new StopInstanceAction()),
            diagnostic => diagnostic.Message.Contains("cannot exceed 100", StringComparison.Ordinal));

        Assert.Contains(
            Diagnostics(
                new SustainedMetricTrigger { Metric = "system_disk_bytes", Threshold = 95, SustainedSeconds = 60 },
                new StopInstanceAction()),
            diagnostic => diagnostic.Message.Contains("Unknown metric", StringComparison.Ordinal));

        // A byte-valued metric is not a percentage, so a large threshold is legitimate there.
        Assert.Empty(Diagnostics(
            new SustainedMetricTrigger { Metric = "instance_memory_bytes", Threshold = 4_000_000_000, SustainedSeconds = 60 },
            new StopInstanceAction()));
    }

    /// <summary>
    /// A human-confirmed intent executes through the same action path as a tick, so it meets the
    /// same holds. Confirmation authorizes the action; it does not exempt it from maintenance.
    /// </summary>
    [Fact]
    public async Task Evaluator_DeferredIntentRestartIsRefusedWhileTheInstanceIsHeld()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var hold = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "hold",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" }],
            CooldownSeconds = 0
        };
        var guarded = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "guarded-restart",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions =
            [
                new ConfirmationPlanAction
                {
                    Summary = "approve the restart",
                    Deferred = new RestartInstanceAction { InstanceId = InstanceId }
                }
            ],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [hold, guarded], Version = 0 }).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        var plan = Assert.Single(harness.PlanKernel.ListActive(AutomationIntents.PlanKind));
        harness.PlanKernel.Confirm(plan.PlanId, plan.PlanHash, "user-a").Unwrap();
        Assert.True(AutomationIntents.TryParsePayload(
            harness.PlanKernel.Get(plan.PlanId).Unwrap().Payload,
            out var payload,
            out var deferred));

        var refused = await harness.Evaluator.ExecuteDeferredAsync(deferred, payload.TargetInstanceId, CancellationToken.None);
        Assert.True(refused.IsErr(out var refusedError));
        Assert.Equal("automation.suppressed", refusedError!.Code);
        Assert.Empty(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart" && entry.ErrorCode == "automation.suppressed" && !entry.Succeeded);

        // Once the hold lapses the same confirmed intent goes through.
        harness.Time.Advance(TimeSpan.FromMinutes(11));
        var executed = await harness.Evaluator.ExecuteDeferredAsync(deferred, payload.TargetInstanceId, CancellationToken.None);
        Assert.True(executed.IsOk(out _));
        Assert.Equal([InstanceId], harness.Instances.Started);
    }

    /// <summary>
    /// A removed instance's status can never change again, so a surviving observation would satisfy
    /// a duration trigger forever.
    /// </summary>
    [Fact]
    public async Task Evaluator_ForgetsInstancesRemovedFromTheCatalog()
    {
        using var harness = new Harness();
        var trigger = new StatusDurationTrigger { Status = InstanceStatus.Stopped, DurationSeconds = 60 };
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "stuck-stopped",
            Trigger = trigger,
            Actions =
            [
                new NotificationAction { Title = "stopped", Message = "down", Severity = "Warning" },
                new MaintenanceStateAction { DurationSeconds = 600, Reason = "held" }
            ],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Stopped);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(90));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Single(harness.DomainEvents.Notifications);
        Assert.Single(harness.Evaluator.ActiveSuppressions());

        harness.Manager.Instances.TryRemove(InstanceId, out _);
        harness.Time.Advance(TimeSpan.FromSeconds(90));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // No further firing, and the facts and holds the instance left behind are gone.
        Assert.Single(harness.DomainEvents.Notifications);
        Assert.Empty(harness.Evaluator.ActiveSuppressions());
        Assert.False(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);
    }

    /// <summary>
    /// The audit record this action writes has to be findable the way every other automation record
    /// is found — audit queries filter on an exact target match.
    /// </summary>
    [Fact]
    public async Task Evaluator_AuditRecordIsReturnedByATargetFilteredAuditQuery()
    {
        var auditRoot = Directory.CreateTempSubdirectory("mcsl-automation-audit-").FullName;
        var auditLog = new AuditLog(new DaemonAuditConfig(), rootDirectory: auditRoot);
        using var harness = new Harness(
            start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"),
            forwardAudit: auditLog);
        var targeted = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "note-the-crash",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions = [new AuditRecordAction { Message = "crashed and left alone on purpose", Severity = "Warning" }],
            CooldownSeconds = 0
        };
        var untargeted = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "note-the-window",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 30 },
            Actions = [new AuditRecordAction { Message = "maintenance window opened", Severity = "Info" }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [targeted, untargeted], Version = 0 }).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // "*" is the trusted-admin marker: an operator reading the whole history.
        var application = new LocalAuditApplication(auditLog);
        var filtered = await application.QueryAsync(
            new AuditQuery(Target: InstanceId.ToString("D"), OwnerPrincipal: "*"),
            CancellationToken.None);
        var record = Assert.Single(
            filtered.Unwrap().Records,
            candidate => candidate.Permission == "audit.record");
        Assert.Equal(AutomationEvaluator.ServicePrincipalSubject, record.Principal);
        Assert.Equal(InstanceId.ToString("D"), record.Target);
        Assert.Contains("crashed and left alone on purpose", record.Detail!, StringComparison.Ordinal);
        Assert.Contains("Warning", record.Detail!, StringComparison.Ordinal);
        Assert.Null(record.ErrorCode);
        Assert.True(record.Succeeded);

        // An untargeted policy still records, it simply has no instance to be filed under.
        var all = await application.QueryAsync(new AuditQuery(OwnerPrincipal: "*"), CancellationToken.None);
        Assert.Contains(all.Unwrap().Records, candidate =>
            candidate.Permission == "audit.record" &&
            candidate.Target == string.Empty &&
            candidate.Detail!.Contains("maintenance window opened", StringComparison.Ordinal));
    }

    /// <summary>
    /// One firing can cover several instances and be held on only some of them, so the dry run has
    /// to answer per target and per action exactly as execution does.
    /// </summary>
    [Fact]
    public async Task Test_ReportsHeldTargetsIndividuallyForAWildcardFiring()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var hold = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "hold-one",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" }],
            CooldownSeconds = 0
        };
        var wildcardGuard = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "repair-everything",
            Trigger = new StatusDurationTrigger { Status = InstanceStatus.Stopped, DurationSeconds = 60 },
            Actions = [new RestartInstanceAction()],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [hold, wildcardGuard], Version = 0 }).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Stopped);
        harness.AddInstance(SecondInstanceId, InstanceStatus.Stopped);

        // Places the hold on one instance and starts the duration clock on both.
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(90));

        var outcome = Assert.Single(harness.Evaluator.Test(), candidate => candidate.PolicyId == wildcardGuard.Id);
        Assert.True(outcome.WouldFire);
        Assert.Contains(InstanceId.ToString("D"), outcome.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondInstanceId.ToString("D"), outcome.Reason, StringComparison.Ordinal);

        // And the dry run wrote nothing: the real tick still repairs the instance that is free.
        Assert.Single(harness.Evaluator.ActiveSuppressions());
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([SecondInstanceId], harness.Instances.Started);
    }

    [Fact]
    public async Task Evaluator_WildcardUnresponsiveAndStatusDurationActOnEveryMatchingInstance()
    {
        const string twoSilentInstances =
            """
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[{"instance_id":"11111111-1111-1111-1111-111111111111","name":"a","status":"running","cpu_percent":1.5,"memory_bytes":1024,"silent_seconds":600},{"instance_id":"44444444-4444-4444-4444-444444444444","name":"b","status":"running","cpu_percent":1.5,"memory_bytes":1024,"silent_seconds":900}],"disk_total_bytes":1024,"disk_free_bytes":512}
            """;
        using var harness = new Harness(seedHistoryJsonl: twoSilentInstances);

        var unresponsive = harness.Evaluator.EvaluateTrigger(
            new UnresponsiveInstanceTrigger { SilentSeconds = 300 },
            harness.Time.GetUtcNow());
        Assert.True(unresponsive.Fires);
        Assert.Equal([InstanceId, SecondInstanceId], unresponsive.Targets);

        // One firing must stop both, the way a wildcard crash repair covers a whole correlated crash.
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "quiet-guard",
            Trigger = new StatusDurationTrigger { Status = InstanceStatus.Stopped, DurationSeconds = 60 },
            Actions = [new StopInstanceAction()],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Stopped);
        harness.AddInstance(SecondInstanceId, InstanceStatus.Stopped);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        harness.Time.Advance(TimeSpan.FromSeconds(90));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        Assert.Equal([InstanceId, SecondInstanceId], harness.Instances.Stopped.Order());
    }

    /// <summary>
    /// The union reads through a JsonDocument, which keeps both copies of a repeated property and
    /// hands back the last. A second instance_id of null would therefore widen a policy written for
    /// one instance into a fleet-wide one — the same harm a misspelled property does.
    /// </summary>
    [Fact]
    public void Converter_RejectsADuplicatedPropertyOnEitherUnion()
    {
        var trigger = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"instance_id\":\"" + InstanceId + "\",\"instance_id\":null,\"silent_seconds\":300}",
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Contains("instance_id", trigger.Message, StringComparison.Ordinal);

        var action = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.restart\",\"instance_id\":\"" + InstanceId + "\",\"instance_id\":null}",
            ApplicationContractJsonContext.Default.AutomationAction));
        Assert.Contains("instance_id", action.Message, StringComparison.Ordinal);

        // The nested deferred action is read through the same gate.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"plan.confirmation\",\"summary\":\"s\",\"deferred\":{\"type\":\"instance.stop\",\"instance_id\":\"" + InstanceId + "\",\"instance_id\":null}}",
            ApplicationContractJsonContext.Default.AutomationAction));

        // Control: one level up, the source-generated path already refused duplicates and still does.
        // The union converters exist to mirror that strictness, not to relax it.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"policies\":[],\"version\":1,\"version\":2}",
            ApplicationContractJsonContext.Default.AutomationPolicySet));
    }

    /// <summary>
    /// 1e400 is a well-formed JSON number that no double can hold: it reads back as infinity, which
    /// seats a threshold no reading can reach and then cannot be written back out at all.
    /// </summary>
    [Fact]
    public void Converter_RejectsANumberTooLargeForADoubleAndValidatorRejectsANonFiniteOne()
    {
        var silent = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"instance.unresponsive\",\"silent_seconds\":1e400}",
            ApplicationContractJsonContext.Default.AutomationTrigger));
        Assert.Contains("silent_seconds", silent.Message, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"type\":\"metric.sustained\",\"metric\":\"instance_memory_bytes\",\"threshold\":1e400,\"sustained_seconds\":60}",
            ApplicationContractJsonContext.Default.AutomationTrigger));

        // The validator does not lean on the converter having run: a policy built in process is a
        // path that never passes through the wire gate.
        Assert.Contains(
            Diagnostics(
                new SustainedMetricTrigger { Metric = "instance_memory_bytes", Threshold = double.PositiveInfinity, SustainedSeconds = 60 },
                new StopInstanceAction()),
            diagnostic => diagnostic.Code == "automation.trigger_invalid");

        Assert.Contains(
            Diagnostics(new UnresponsiveInstanceTrigger { SilentSeconds = double.NaN }, new StopInstanceAction()),
            diagnostic => diagnostic.Code == "automation.trigger_invalid");
    }

    /// <summary>
    /// A tick the sampler could not take is the newest thing the history knows. Reaching past it for
    /// the last real reading would answer a question about the present with evidence the sampler
    /// already disowned — the instance may have recovered during the tick nobody observed.
    /// </summary>
    [Fact]
    public void Evaluator_UnresponsiveStaysQuietWhenTheNewestSampleIsAGap()
    {
        const string staleSilenceThenGap =
            """
            {"timestamp":"2026-07-27T23:59:45+00:00","gap":false,"system_cpu_percent":5.5,"memory_used_kilobytes":16384,"memory_total_kilobytes":32768,"instances":[{"instance_id":"11111111-1111-1111-1111-111111111111","name":"demo","status":"running","cpu_percent":1.5,"memory_bytes":1024,"silent_seconds":600}],"disk_total_bytes":1024,"disk_free_bytes":512}
            {"timestamp":"2026-07-28T00:00:00+00:00","gap":true,"system_cpu_percent":0,"memory_used_kilobytes":0,"memory_total_kilobytes":0,"instances":[],"disk_total_bytes":null,"disk_free_bytes":null}
            """;
        using var harness = new Harness(
            sampleInterval: TimeSpan.FromSeconds(15),
            seedHistoryJsonl: staleSilenceThenGap);

        // The real reading behind the gap is still inside the window and would fire on its own —
        // the sibling test seeds exactly it — so only the gap in front of it keeps this quiet.
        var evaluation = harness.Evaluator.EvaluateTrigger(
            new UnresponsiveInstanceTrigger { SilentSeconds = 300 },
            harness.Time.GetUtcNow());
        Assert.False(evaluation.Fires);
        Assert.Contains("gap", evaluation.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two policies that do not know about each other can hold the same instance at once. A short
    /// restart hold must not end a long maintenance hold early, so the scopes get a slot each and
    /// expire on their own clocks.
    /// </summary>
    [Fact]
    public async Task Evaluator_ScopedHoldsExpireIndependentlyOfEachOther()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var maintenance = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "hour-long-maintenance",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 3600, Reason = "patching" }],
            CooldownSeconds = 3600
        };
        var briefRestartHold = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "minute-long-restart-hold",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new RestartSuppressionAction { InstanceId = InstanceId, DurationSeconds = 60, Reason = "flapping" }],
            CooldownSeconds = 3600
        };
        var stopGuard = new AutomationPolicy
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Name = "stop-on-crash",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            Actions = [new StopInstanceAction()],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet
        {
            Policies = [maintenance, briefRestartHold, stopGuard],
            Version = 0
        }).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // Both holds are in force and both are visible: a reader that saw only one could not tell
        // which of the two refusals it is looking at.
        var holds = harness.Evaluator.ActiveSuppressions();
        Assert.Equal(2, holds.Length);
        Assert.Equal([AutomationSuppressionScope.All, AutomationSuppressionScope.Restarts], holds.Select(hold => hold.Scope));
        Assert.All(holds, hold => Assert.Equal(InstanceId, hold.InstanceId));

        // The minute-long restart hold lapses; the hour-long maintenance hold it was written beside
        // must still be refusing stops.
        harness.Time.Advance(TimeSpan.FromMinutes(2));
        instance.Status = InstanceStatus.Crashed;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        Assert.Empty(harness.Instances.Stopped);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.stop" &&
            entry.ErrorCode == "automation.suppressed" &&
            !entry.Succeeded);
        var surviving = Assert.Single(harness.Evaluator.ActiveSuppressions());
        Assert.Equal(AutomationSuppressionScope.All, surviving.Scope);
        Assert.Equal("patching", surviving.Reason);

        // Past the maintenance hold, the same crash is handled normally.
        harness.Time.Advance(TimeSpan.FromMinutes(60));
        instance.Status = InstanceStatus.Running;
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
    }

    /// <summary>
    /// A second hold in the same scope is the same decision being revised, so it replaces the first
    /// rather than sitting beside it.
    /// </summary>
    [Fact]
    public async Task Evaluator_ASecondHoldInTheSameScopeReplacesTheFirst()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "re-declared-maintenance",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 60 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "first" }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Running);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal("first", Assert.Single(harness.Evaluator.ActiveSuppressions()).Reason);

        policy.Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "second" }];
        harness.Store.Apply(new AutomationPolicySet { Policies = [policy], Version = 1 }).Unwrap();
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        var held = Assert.Single(harness.Evaluator.ActiveSuppressions());
        Assert.Equal("second", held.Reason);
    }

    /// <summary>
    /// The start half of a two-phase restart runs a tick or more after the check that authorized it,
    /// so it meets the holds in force now. A maintenance hold placed in between is a decision to
    /// keep the instance down, and it outranks a restart already under way.
    /// </summary>
    [Fact]
    public async Task Evaluator_DelayedRestartStartIsRefusedByAHoldPlacedAfterTheStop()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var restart = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "window-restart",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new RestartInstanceAction { InstanceId = InstanceId, BackoffBaseSeconds = 1, BackoffMaxSeconds = 2 }],
            CooldownSeconds = 3600
        };
        // Ordered after the restart, so the hold lands between the stop half and the start half.
        var hold = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "hold-it-down",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" }],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [restart, hold], Version = 0 }).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
        Assert.Empty(harness.Instances.Started);
        Assert.Single(harness.Evaluator.ActiveSuppressions());

        // The drain tick observes the stop and finds the instance held: nothing starts, and the
        // refusal is recorded rather than dropped silently.
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart.start" &&
            entry.ErrorCode == "automation.suppressed" &&
            !entry.Succeeded);

        // The pending restart is spent, not deferred: the condition that asked for it is long gone
        // by the time the hold lifts, so nothing brings the instance back on its own.
        harness.Time.Advance(TimeSpan.FromMinutes(20));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Evaluator.ActiveSuppressions());
        Assert.Empty(harness.Instances.Started);
    }

    /// <summary>
    /// The confirmed-intent restart waits for the stop to complete before starting, and that wait
    /// runs for up to thirty seconds. A hold placed inside it is as binding as one placed a tick
    /// earlier — confirmation authorizes the action, it does not exempt it from maintenance.
    /// </summary>
    [Fact]
    public async Task Evaluator_DeferredIntentRestartIsRefusedByAHoldPlacedDuringTheStopWait()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        harness.AddInstance(InstanceId, InstanceStatus.Running);

        // The hold is placed from inside the stop call, which is where a real one would land: after
        // the check that authorized this restart and before the start it is waiting to perform.
        harness.Instances.OnStop = () => harness.Evaluator.ExecuteDeferredAsync(
            new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" },
            InstanceId,
            CancellationToken.None).GetAwaiter().GetResult();

        var refused = await harness.Evaluator.ExecuteDeferredAsync(
            new RestartInstanceAction { InstanceId = InstanceId },
            InstanceId,
            CancellationToken.None);

        Assert.True(refused.IsErr(out var refusedError));
        Assert.Equal("automation.suppressed", refusedError!.Code);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
        Assert.Empty(harness.Instances.Started);
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart" &&
            entry.ErrorCode == "automation.suppressed" &&
            !entry.Succeeded);
    }

    /// <summary>
    /// A pending restart start survives its instance unless the tick sweeps it, and the drain fires
    /// on the recorded id alone — so a later instance holding that id inherits a start nobody asked
    /// for.
    /// </summary>
    [Fact]
    public async Task Evaluator_ForgetsThePendingRestartOfAnInstanceRemovedFromTheCatalog()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "window-restart",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new RestartInstanceAction { InstanceId = InstanceId, BackoffBaseSeconds = 1, BackoffMaxSeconds = 2 }],
            CooldownSeconds = 3600
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Running);

        // The stop half runs and leaves a pending start behind.
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Stopped);
        Assert.Empty(harness.Instances.Started);

        harness.Manager.Instances.TryRemove(InstanceId, out _);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // A new instance takes the id. It never asked to be restarted, so nothing starts it.
        harness.AddInstance(InstanceId, InstanceStatus.Stopped);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Empty(harness.Instances.Started);
    }

    /// <summary>
    /// Restart backoff is per policy and per instance, and it outlives the instance unless the tick
    /// sweeps it — so a later instance holding that id would inherit an escalation it never earned.
    /// </summary>
    [Fact]
    public async Task Evaluator_ForgetsTheRestartBackoffOfAnInstanceRemovedFromTheCatalog()
    {
        using var harness = new Harness();
        var policy = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "crash-guard",
            Trigger = new UnexpectedExitTrigger { InstanceId = InstanceId },
            // A ten-minute floor: any surviving backoff refuses every restart this test performs.
            Actions = [new RestartInstanceAction { BackoffBaseSeconds = 600, BackoffMaxSeconds = 1800 }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(Document(policy, version: 0)).Unwrap();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        instance.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        Assert.Equal([InstanceId], harness.Instances.Started);

        harness.Manager.Instances.TryRemove(InstanceId, out _);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // A new instance takes the id and crashes. It is entitled to its first restart immediately.
        var replacement = harness.AddInstance(InstanceId, InstanceStatus.Running);
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        replacement.Status = InstanceStatus.Crashed;
        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        Assert.Equal([InstanceId, InstanceId], harness.Instances.Started);
        Assert.DoesNotContain(harness.Audit.Events, entry => entry.ErrorCode == "automation.backoff");
    }

    /// <summary>
    /// Crash evidence outlives the instance unless the tick sweeps it: a wildcard crash-loop trigger
    /// matches on the recorded id alone, so a ghost entry keeps firing actions at something that no
    /// longer exists.
    /// </summary>
    [Fact]
    public async Task Evaluator_ForgetsTheCrashHistoryOfAnInstanceRemovedFromTheCatalog()
    {
        using var harness = new Harness();
        var instance = harness.AddInstance(InstanceId, InstanceStatus.Running);

        // The crashes accumulate before any policy is applied, so nothing consumes the evidence the
        // way a firing crash-loop policy would.
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

        var trigger = new CrashLoopTrigger { MaxCrashes = 2, WindowSeconds = 600 };
        Assert.True(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);

        harness.Manager.Instances.TryRemove(InstanceId, out _);
        harness.Store.Apply(Document(new AutomationPolicy
        {
            Id = PolicyId,
            Name = "loop-notice",
            Trigger = trigger,
            Actions = [new NotificationAction { Title = "looping", Message = "crash loop", Severity = "Warning" }],
            CooldownSeconds = 0
        }, version: 0)).Unwrap();

        harness.Time.Advance(TimeSpan.FromSeconds(15));
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);
        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        Assert.False(harness.Evaluator.EvaluateTrigger(trigger, harness.Time.GetUtcNow()).Fires);
        Assert.Empty(harness.DomainEvents.Notifications);
    }

    /// <summary>
    /// A daemon-wide trigger has no target of its own, but its actions can each name one. The dry
    /// run has to resolve targets the way execution does, or it reports a free hand where a real
    /// tick would be refused.
    /// </summary>
    [Fact]
    public async Task Test_ReportsAHoldOnADaemonWideTriggersExplicitlyTargetedAction()
    {
        using var harness = new Harness(start: DateTimeOffset.Parse("2026-07-28T00:00:00+00:00"));
        var hold = new AutomationPolicy
        {
            Id = PolicyId,
            Name = "hold-one",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 1 },
            Actions = [new MaintenanceStateAction { InstanceId = InstanceId, DurationSeconds = 600, Reason = "patching" }],
            CooldownSeconds = 3600
        };
        var nightlyRestart = new AutomationPolicy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "nightly-restart",
            Trigger = new MaintenanceWindowTrigger { StartHourUtc = 0, StartMinuteUtc = 0, DurationMinutes = 60 },
            Actions = [new RestartInstanceAction { InstanceId = InstanceId, BackoffBaseSeconds = 1, BackoffMaxSeconds = 2 }],
            CooldownSeconds = 0
        };
        harness.Store.Apply(new AutomationPolicySet { Policies = [hold, nightlyRestart], Version = 0 }).Unwrap();
        harness.AddInstance(InstanceId, InstanceStatus.Running);

        await harness.Evaluator.EvaluateTickAsync(CancellationToken.None);

        // Execution refuses it, so the dry run has to say so too.
        Assert.Contains(harness.Audit.Events, entry =>
            entry.Permission == "instance.restart" && entry.ErrorCode == "automation.suppressed");
        var outcome = Assert.Single(harness.Evaluator.Test(), candidate => candidate.PolicyId == nightlyRestart.Id);
        Assert.True(outcome.WouldFire);
        Assert.Contains($"actions held for {InstanceId:D}", outcome.Reason, StringComparison.Ordinal);
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

        internal Harness(
            DateTimeOffset? start = null,
            TimeSpan? sampleInterval = null,
            string? seedHistoryJsonl = null,
            IAuditSink? forwardAudit = null)
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
            Audit = new RecordingAuditSink(forwardAudit);
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

    /// <summary>Records for assertions, and optionally forwards to a real sink under test.</summary>
    private sealed class RecordingAuditSink(IAuditSink? forward = null) : IAuditSink
    {
        internal List<AuditEvent> Events { get; } = [];

        public void Record(AuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            forward?.Record(auditEvent);
        }
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

        /// <summary>Runs inside the stop, for tests that need state to change mid-mutation.</summary>
        internal Action? OnStop { get; set; }

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
            OnStop?.Invoke();
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
