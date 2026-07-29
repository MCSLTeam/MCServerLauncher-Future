using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.ApplicationCore.Monitoring;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using MCServerLauncher.Daemon.Management;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Automation;

/// <summary>
/// The daemon-side policy engine. Every tick it derives facts (status transitions from polled
/// instance statuses, sustained metrics from the retained history, schedule windows), evaluates
/// the applied policies, enforces the guard rails (cooldown, daily cap, restart backoff), and
/// executes actions through permission-checked proxies under the explicit automation service
/// principal — an action the principal is not granted fails authorization like any other caller.
/// Runtime guard state is in memory and resets on daemon restart.
/// A crash that is repaired within a single tick can escape transition detection; the evaluator
/// trades that corner for full decoupling from instance event wiring.
/// </summary>
internal sealed class AutomationEvaluator : IDisposable, IAsyncDisposable
{
    internal const string ServicePrincipalSubject = "daemon-automation";
    internal const string AuditMethod = "automation.execute";

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How far back crash evidence is kept. It bounds the longest usable crash-loop window, so the
    /// policy validator refuses windows longer than this rather than silently undercounting them.
    /// </summary>
    internal static readonly TimeSpan CrashMemory = TimeSpan.FromHours(24);

    private static readonly ImmutableArray<string> ServicePermissions =
        ["mcsl.instance.start", "mcsl.instance.stop"];

    private readonly object _gate = new();

    /// <summary>
    /// Guards every fact and guard-state collection below. The tick loop and the wire-exposed
    /// dry run (mcsl.automation.test, on RPC threads) both reach them.
    /// </summary>
    private readonly object _stateGate = new();
    private readonly AutomationPolicyStore _store;
    private readonly IInstanceManager _instances;
    private readonly MonitoringSampler _metrics;
    private readonly IInstanceManagementApplication _authorizedInstances;
    private readonly IDomainEventPort _domainEvents;
    private readonly PlanKernel _planKernel;
    private readonly IAuditSink? _auditSink;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly ILogger<AutomationEvaluator>? _logger;

    private readonly Dictionary<Guid, InstanceStatus> _lastStatuses = new();
    private readonly Dictionary<Guid, List<DateTimeOffset>> _crashTimes = new();
    private readonly HashSet<Guid> _crashedThisTick = new();
    private readonly HashSet<Guid> _pendingRestartStarts = new();
    private readonly Dictionary<Guid, PolicyRuntime> _runtime = new();

    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;

    public AutomationEvaluator(
        AutomationPolicyStore store,
        IInstanceManager instances,
        MonitoringSampler metrics,
        IInstanceApplication instanceApplication,
        IDomainEventPort domainEvents,
        PlanKernel planKernel,
        IAuditSink? auditSink = null,
        TimeProvider? timeProvider = null,
        ILogger<AutomationEvaluator>? logger = null,
        TimeSpan? interval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        ArgumentNullException.ThrowIfNull(instanceApplication);
        _domainEvents = domainEvents ?? throw new ArgumentNullException(nameof(domainEvents));
        _planKernel = planKernel ?? throw new ArgumentNullException(nameof(planKernel));
        _auditSink = auditSink;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? DefaultInterval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_interval, TimeSpan.Zero);
        _logger = logger;
        _authorizedInstances = new AuthorizedInstanceManagementApplication(
            new CallerContext(ServicePrincipalSubject, ServicePermissions, isMainToken: false),
            instanceApplication);
    }

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is { IsCompleted: false })
                return;

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(_runCancellation.Token);
        }
    }

    internal void RequestStop()
    {
        lock (_gate)
            _runCancellation?.Cancel();
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        lock (_gate)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is not null)
            await runTask.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runCancellation?.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await StopAsync();
        lock (_gate)
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
        }
    }

    internal async Task EvaluateTickAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await ObserveInstancesAsync(now, cancellationToken).ConfigureAwait(false);

        var document = _store.Get();
        foreach (var policy in document.Policies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!policy.Enabled || policy.Trigger is null)
                continue;

            var evaluation = EvaluateTrigger(policy.Trigger, now, SnapshotFacts());
            if (!evaluation.Fires)
                continue;

            // Guards bound how often a policy fires, not how many instances one firing covers: a
            // correlated multi-instance crash must be repaired as a single firing, otherwise the
            // cooldown stamped by the first instance would strand every other one.
            var suppression = CheckGuards(policy, now, admit: true);
            if (suppression is not null)
            {
                Audit(policy.Id, "automation.policy", Describe(evaluation.Targets.FirstOrDefault()), false, suppression);
                continue;
            }

            foreach (var target in evaluation.Targets)
            {
                await ExecuteActionsAsync(policy, target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Dry run: evaluates every applied policy against the current facts and guard state without
    /// executing anything.
    /// </summary>
    internal ImmutableArray<AutomationTestOutcome> Test()
    {
        var now = _timeProvider.GetUtcNow();
        var facts = SnapshotFacts();
        var outcomes = ImmutableArray.CreateBuilder<AutomationTestOutcome>();
        foreach (var policy in _store.Get().Policies)
        {
            if (policy.Trigger is null)
            {
                outcomes.Add(new AutomationTestOutcome(policy.Id, false, "invalid: no trigger", null));
                continue;
            }

            if (!policy.Enabled)
            {
                outcomes.Add(new AutomationTestOutcome(policy.Id, false, "disabled", null));
                continue;
            }

            var evaluation = EvaluateTrigger(policy.Trigger, now, facts);
            var target = Describe(evaluation.Targets.FirstOrDefault());
            if (!evaluation.Fires)
            {
                outcomes.Add(new AutomationTestOutcome(policy.Id, false, evaluation.Reason, target));
                continue;
            }

            // A dry run observes guard state; it must never create or roll it.
            var suppression = CheckGuards(policy, now, admit: false);
            outcomes.Add(suppression is not null
                ? new AutomationTestOutcome(policy.Id, false, $"{evaluation.Reason}; suppressed: {suppression}", target)
                : new AutomationTestOutcome(policy.Id, true, evaluation.Reason, target));
        }

        return outcomes.ToImmutable();
    }

    /// <summary>
    /// Executes a human-confirmed deferred action under the service principal. Confirmation
    /// already happened at the plan layer, so restart backoff does not apply here.
    /// </summary>
    internal async Task<RustyOptions.Result<string, API.Errors.DaemonError>> ExecuteDeferredAsync(
        AutomationAction action,
        Guid? targetInstanceId,
        CancellationToken cancellationToken)
    {
        var outcome = await ExecuteSingleActionAsync(
            action,
            targetInstanceId,
            policy: null,
            applyRestartBackoff: false,
            waitForStopBeforeRestart: true,
            cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    internal TriggerEvaluation EvaluateTrigger(AutomationTrigger trigger, DateTimeOffset now) =>
        EvaluateTrigger(trigger, now, SnapshotFacts());

    /// <summary>
    /// Evaluates one trigger against an immutable fact snapshot. Instance-scoped triggers return
    /// EVERY matching instance: a wildcard policy must repair a whole correlated crash, not an
    /// arbitrary one of them.
    /// </summary>
    private TriggerEvaluation EvaluateTrigger(AutomationTrigger trigger, DateTimeOffset now, FactSnapshot facts)
    {
        switch (trigger)
        {
            case UnexpectedExitTrigger unexpectedExit:
            {
                var crashed = facts.CrashedThisTick
                    .Where(id => unexpectedExit.InstanceId is null || unexpectedExit.InstanceId == id)
                    .Order()
                    .ToArray();
                return crashed.Length == 0
                    ? TriggerEvaluation.Quiet("no crash observed")
                    : TriggerEvaluation.Firing(
                        crashed.Length == 1 ? "instance crashed" : $"{crashed.Length} instances crashed",
                        crashed);
            }

            case CrashLoopTrigger crashLoop:
            {
                var floor = now - TimeSpan.FromSeconds(crashLoop.WindowSeconds);
                var looping = new List<Guid>();
                var recentMax = 0;
                foreach (var (instanceId, times) in facts.CrashTimes)
                {
                    if (crashLoop.InstanceId is not null && crashLoop.InstanceId != instanceId)
                        continue;

                    var recent = times.Count(time => time >= floor);
                    if (recent < crashLoop.MaxCrashes)
                        continue;
                    looping.Add(instanceId);
                    recentMax = Math.Max(recentMax, recent);
                }

                looping.Sort();
                return looping.Count == 0
                    ? TriggerEvaluation.Quiet("below crash-loop threshold")
                    : TriggerEvaluation.Firing($"{recentMax} crashes within {crashLoop.WindowSeconds}s", looping);
            }

            case SustainedMetricTrigger sustained:
                return EvaluateSustainedMetric(sustained, now);

            case MaintenanceWindowTrigger window:
            {
                var minutesOfDay = now.UtcDateTime.Hour * 60 + now.UtcDateTime.Minute;
                var start = window.StartHourUtc * 60 + window.StartMinuteUtc;
                var end = start + window.DurationMinutes;
                var inside = end <= 1440
                    ? minutesOfDay >= start && minutesOfDay < end
                    : minutesOfDay >= start || minutesOfDay < end - 1440;
                return inside
                    ? TriggerEvaluation.Firing("inside maintenance window", [])
                    : TriggerEvaluation.Quiet("outside maintenance window");
            }

            default:
                return TriggerEvaluation.Quiet("unknown trigger");
        }
    }

    /// <summary>
    /// Sustained conditions are judged only on lossless raw evidence. Every way the evidence can be
    /// incomplete — a window too long to read raw, a truncated read, a dropped record, a gap marker,
    /// an unobserved stretch between samples, or an uncovered window edge — is non-firing and says
    /// which one it was: absence of contrary evidence is not evidence that the metric held.
    /// </summary>
    private TriggerEvaluation EvaluateSustainedMetric(
        SustainedMetricTrigger sustained,
        DateTimeOffset now)
    {
        var window = TimeSpan.FromSeconds(sustained.SustainedSeconds);
        // The sampler's own cadence, not this engine's tick rate, decides how many points the
        // window holds and how far apart two samples may sit before the space between them is a
        // stretch nobody observed.
        var interval = _metrics.SampleInterval;
        var needed = Math.Max(1, window.Ticks / interval.Ticks + 2);
        if (needed > MonitoringSampler.MaximumQueryPoints)
        {
            // A longer window could only be read downsampled, and a downsampled series cannot show
            // the dips that must veto a "sustained" verdict.
            return TriggerEvaluation.Quiet(
                $"sustained window needs {needed} samples, above the {MonitoringSampler.MaximumQueryPoints}-sample lossless evaluation budget");
        }

        var evidence = _metrics.ReadRawWindow(now - window, now, (int)needed);
        if (evidence.DroppedInside)
        {
            return TriggerEvaluation.Quiet(
                "metric history lost a record to a write failure inside the window");
        }

        if (evidence.Truncated)
        {
            return TriggerEvaluation.Quiet("metric history read was truncated inside the window");
        }

        var samples = evidence.Samples;
        // A gap marks time nobody observed. Treating it as absence of evidence would let a policy
        // claim a metric was sustained across a stretch the daemon never saw.
        if (samples.Any(static sample => sample.Gap))
        {
            return TriggerEvaluation.Quiet("metric history has a recorded gap in the window");
        }

        // Sustained means evidence across the whole window: it must reach both edges, and no two
        // consecutive samples may sit further apart than the sampler's own hole threshold.
        if (samples.Length == 0 || samples[0].Timestamp > now - window + interval)
        {
            return TriggerEvaluation.Quiet("metric history does not cover the window start (retention or startup)");
        }

        if (now - samples[^1].Timestamp > interval * 2)
        {
            return TriggerEvaluation.Quiet("metric history stops before the window end");
        }

        for (var index = 1; index < samples.Length; index++)
        {
            if (samples[index].Timestamp - samples[index - 1].Timestamp > interval * 2)
            {
                return TriggerEvaluation.Quiet("metric history has an unobserved stretch in the window");
            }
        }

        switch (sustained.Metric)
        {
            case "system_cpu":
                return samples.All(sample => sample.SystemCpuPercent >= sustained.Threshold)
                    ? TriggerEvaluation.Firing($"system cpu >= {sustained.Threshold} for {sustained.SustainedSeconds}s", [])
                    : TriggerEvaluation.Quiet("system cpu below threshold");

            case "system_memory_percent":
                return samples.All(sample =>
                    sample.MemoryTotalKilobytes > 0 &&
                    sample.MemoryUsedKilobytes * 100.0 / sample.MemoryTotalKilobytes >= sustained.Threshold)
                    ? TriggerEvaluation.Firing($"system memory >= {sustained.Threshold}% for {sustained.SustainedSeconds}s", [])
                    : TriggerEvaluation.Quiet("system memory below threshold");

            case "instance_cpu":
            case "instance_memory_bytes":
            {
                var candidates = sustained.InstanceId is { } fixedTarget
                    ? [fixedTarget]
                    : samples.SelectMany(static sample => sample.Instances.Select(static entry => entry.InstanceId))
                        .Distinct()
                        .Order()
                        .ToArray();
                var breaching = new List<Guid>();
                foreach (var candidate in candidates)
                {
                    var sustainedForCandidate = samples.All(sample =>
                    {
                        var entry = sample.Instances.FirstOrDefault(item => item.InstanceId == candidate);
                        if (entry is null)
                            return false;
                        var value = sustained.Metric == "instance_cpu" ? entry.CpuPercent : entry.MemoryBytes;
                        return value >= sustained.Threshold;
                    });
                    if (sustainedForCandidate)
                        breaching.Add(candidate);
                }

                return breaching.Count == 0
                    ? TriggerEvaluation.Quiet($"{sustained.Metric} below threshold")
                    : TriggerEvaluation.Firing(
                        $"{sustained.Metric} >= {sustained.Threshold} for {sustained.SustainedSeconds}s",
                        breaching);
            }

            default:
                return TriggerEvaluation.Quiet($"unknown metric '{sustained.Metric}'");
        }
    }

    /// <summary>
    /// Cooldown and daily cap. <paramref name="admit" /> false observes the guards without
    /// creating or rolling any state, which is what the dry run needs.
    /// </summary>
    private string? CheckGuards(AutomationPolicy policy, DateTimeOffset now, bool admit)
    {
        lock (_stateGate)
        {
            var runtime = GetRuntimeLocked(policy.Id, now, admit);
            if (runtime is null)
                return null;

            var day = DateOnly.FromDateTime(now.UtcDateTime);
            var dayCount = runtime.Day == day ? runtime.DayCount : 0;
            if (runtime.LastExecuted is { } last && now - last < TimeSpan.FromSeconds(policy.CooldownSeconds))
                return "cooldown";
            if (dayCount >= policy.MaxExecutionsPerDay)
                return "daily execution cap";

            if (admit)
            {
                runtime.LastExecuted = now;
                runtime.Day = day;
                runtime.DayCount = dayCount + 1;
            }

            return null;
        }
    }

    private async Task ExecuteActionsAsync(
        AutomationPolicy policy,
        Guid? target,
        CancellationToken cancellationToken)
    {
        // The crash evidence that fired this policy is consumed here: leaving it in place would
        // re-fire the trigger on every later tick and restart an instance that already recovered.
        if (target is { } fired && policy.Trigger is CrashLoopTrigger)
        {
            lock (_stateGate)
                _crashTimes.Remove(fired);
        }

        foreach (var action in policy.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteSingleActionAsync(
                action,
                target,
                policy,
                applyRestartBackoff: true,
                waitForStopBeforeRestart: false,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<RustyOptions.Result<string, API.Errors.DaemonError>> ExecuteSingleActionAsync(
        AutomationAction action,
        Guid? triggerTarget,
        AutomationPolicy? policy,
        bool applyRestartBackoff,
        bool waitForStopBeforeRestart,
        CancellationToken cancellationToken)
    {
        // Every branch audits its own outcome: only the branch knows which mutation actually ran,
        // and a deferred restart must not be recorded as a completed one.
        switch (action)
        {
            case RestartInstanceAction restart:
            {
                var instanceId = restart.InstanceId ?? triggerTarget;
                if (instanceId is not { } target)
                    return Audited(policy, action.Type, triggerTarget, Err("automation.no_target", "The restart action has no target instance."));

                if (applyRestartBackoff && policy is not null)
                {
                    var now = _timeProvider.GetUtcNow();
                    string? suppressed = null;
                    lock (_stateGate)
                    {
                        var runtime = GetRuntimeLocked(policy.Id, now, create: true)!;
                        var backoff = runtime.GetBackoff(target);
                        if (now < backoff.NextAllowed)
                        {
                            suppressed = "automation.backoff";
                        }
                        else
                        {
                            var delaySeconds = Math.Min(
                                restart.BackoffBaseSeconds * Math.Pow(2, Math.Min(backoff.ConsecutiveRestarts, 30)),
                                restart.BackoffMaxSeconds);
                            runtime.SetBackoff(target, backoff.ConsecutiveRestarts + 1, now + TimeSpan.FromSeconds(delaySeconds));
                        }
                    }

                    if (suppressed is not null)
                        return Audited(policy, action.Type, target, Err(suppressed, "The restart is suppressed by backoff."));
                }

                if (!_instances.Instances.TryGetValue(target, out var instance))
                    return Audited(policy, action.Type, target, Err("instance.not_found", "The restart target was not found."));

                if (instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed)
                {
                    var start = await _authorizedInstances.StartInstanceAsync(new InstanceReference(target), cancellationToken)
                        .ConfigureAwait(false);
                    return Audited(policy, action.Type, target, start.IsErr(out var startError)
                        ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(startError!)
                        : Ok(target));
                }

                var stop = await _authorizedInstances.StopInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                if (stop.IsErr(out var stopError))
                {
                    return Audited(policy, action.Type, target,
                        RustyOptions.Result.Err<string, API.Errors.DaemonError>(stopError!));
                }

                if (!waitForStopBeforeRestart)
                {
                    // The start half runs on a later tick once the stop is observed complete, and
                    // audits itself there. This record claims only the half that has happened.
                    lock (_stateGate)
                        _pendingRestartStarts.Add(target);
                    return Audited(policy, "instance.restart.stop", target, Ok(target));
                }

                var stopped = await WaitForStoppedAsync(instance, cancellationToken).ConfigureAwait(false);
                if (!stopped)
                {
                    return Audited(policy, action.Type, target,
                        Err("automation.stop_timeout", "The instance did not stop within the restart deadline."));
                }

                var restartStart = await _authorizedInstances.StartInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                return Audited(policy, action.Type, target, restartStart.IsErr(out var restartError)
                    ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(restartError!)
                    : Ok(target));
            }

            case StopInstanceAction stopAction:
            {
                var instanceId = stopAction.InstanceId ?? triggerTarget;
                if (instanceId is not { } target)
                    return Audited(policy, action.Type, triggerTarget, Err("automation.no_target", "The stop action has no target instance."));

                var stop = await _authorizedInstances.StopInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                return Audited(policy, action.Type, target, stop.IsErr(out var stopError)
                    ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(stopError!)
                    : Ok(target));
            }

            case NotificationAction notification:
                await _domainEvents.PublishAsync(
                    new ClientNotificationDomainEvent(
                        notification.Title,
                        notification.Message,
                        notification.Severity,
                        triggerTarget ?? Guid.Empty,
                        policy?.Id ?? Guid.Empty,
                        _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()),
                    cancellationToken).ConfigureAwait(false);
                return Audited(policy, action.Type, triggerTarget, Ok(triggerTarget));

            case ConfirmationPlanAction confirmation when policy is not null:
                return Audited(policy, action.Type, triggerTarget,
                    AutomationIntents.FilePlan(_planKernel, policy, confirmation, triggerTarget));

            case ConfirmationPlanAction:
                return Audited(policy, action.Type, triggerTarget,
                    Err("automation.deferred_invalid", "A deferred confirmation plan cannot nest another one."));

            default:
                return Audited(policy, action.Type, triggerTarget,
                    Err("automation.action_unknown", "The action type is not executable."));
        }

        static RustyOptions.Result<string, API.Errors.DaemonError> Err(string code, string message) =>
            RustyOptions.Result.Err<string, API.Errors.DaemonError>(
                new API.Errors.ValidationDaemonError(code, message));

        static RustyOptions.Result<string, API.Errors.DaemonError> Ok(Guid? target) =>
            RustyOptions.Result.Ok<string, API.Errors.DaemonError>(target?.ToString("D") ?? "-");
    }

    private RustyOptions.Result<string, API.Errors.DaemonError> Audited(
        AutomationPolicy? policy,
        string permission,
        Guid? target,
        RustyOptions.Result<string, API.Errors.DaemonError> outcome)
    {
        Audit(
            policy?.Id,
            permission,
            outcome.IsOk(out var reference) ? reference : Describe(target),
            outcome.IsOk(out _),
            outcome.IsErr(out var error) ? error!.Code : null);
        return outcome;
    }

    private async Task<bool> WaitForStoppedAsync(IInstance instance, CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(30);
        while (_timeProvider.GetUtcNow() < deadline)
        {
            if (instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed;
    }

    private async Task ObserveInstancesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The status read is a live probe, so snapshot it once and do all bookkeeping under the
        // state gate; the awaited starts then run without holding it.
        var observed = _instances.Instances
            .Select(static entry => (entry.Key, entry.Value.Status))
            .ToArray();
        var pendingStarts = new List<Guid>();
        lock (_stateGate)
        {
            _crashedThisTick.Clear();
            foreach (var (instanceId, status) in observed)
            {
                var known = _lastStatuses.TryGetValue(instanceId, out var previous);
                if (known && previous != InstanceStatus.Crashed && status == InstanceStatus.Crashed)
                {
                    _crashedThisTick.Add(instanceId);
                    if (!_crashTimes.TryGetValue(instanceId, out var times))
                        _crashTimes[instanceId] = times = new List<DateTimeOffset>();
                    times.Add(now);
                    times.RemoveAll(time => time < now - CrashMemory);
                }

                // An instance still running once its own backoff delay has elapsed survived the
                // episode, so the escalation resets. Reaching Running is not enough on its own:
                // the restart we just performed does that, and clearing there would mean a crash
                // loop never escalates at all.
                if (status == InstanceStatus.Running)
                {
                    foreach (var runtime in _runtime.Values)
                    {
                        var backoff = runtime.GetBackoff(instanceId);
                        if (backoff.ConsecutiveRestarts > 0 && now >= backoff.NextAllowed)
                            runtime.ClearBackoff(instanceId);
                    }
                }

                _lastStatuses[instanceId] = status;
                if (_pendingRestartStarts.Contains(instanceId) &&
                    status is InstanceStatus.Stopped or InstanceStatus.Crashed)
                {
                    _pendingRestartStarts.Remove(instanceId);
                    pendingStarts.Add(instanceId);
                }
            }
        }

        foreach (var instanceId in pendingStarts)
        {
            var start = await _authorizedInstances.StartInstanceAsync(new InstanceReference(instanceId), cancellationToken)
                .ConfigureAwait(false);
            // The second half of a deferred restart is an authorized mutation like any other, so
            // it carries its own audit record instead of only a log line.
            Audit(
                null,
                "instance.restart.start",
                instanceId.ToString("D"),
                start.IsOk(out _),
                start.IsErr(out var error) ? error!.Code : null);
            if (start.IsErr(out var startError))
            {
                _logger?.LogWarning(
                    "[AutomationEvaluator] Deferred restart start failed for {InstanceId}: {Code}.",
                    instanceId,
                    startError!.Code);
            }
        }
    }

    private FactSnapshot SnapshotFacts()
    {
        lock (_stateGate)
        {
            return new FactSnapshot(
                [.. _crashedThisTick],
                _crashTimes.ToDictionary(static entry => entry.Key, static entry => entry.Value.ToArray()));
        }
    }

    private PolicyRuntime? GetRuntimeLocked(Guid policyId, DateTimeOffset now, bool create)
    {
        if (_runtime.TryGetValue(policyId, out var runtime))
            return runtime;
        if (!create)
            return null;

        runtime = new PolicyRuntime { Day = DateOnly.FromDateTime(now.UtcDateTime) };
        _runtime[policyId] = runtime;
        return runtime;
    }

    private void Audit(Guid? policyId, string permission, string? target, bool succeeded, string? errorCode)
    {
        try
        {
            _auditSink?.Record(new AuditEvent(
                ServicePrincipalSubject,
                null,
                AuditMethod,
                permission,
                target ?? policyId?.ToString("D"),
                null,
                null,
                null,
                succeeded,
                errorCode,
                null));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "[AutomationEvaluator] Failed to record an automation audit event.");
        }
    }

    private static string? Describe(Guid? target) => target?.ToString("D");

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await EvaluateTickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A failed tick loses one evaluation, never the engine.
                    _logger?.LogWarning(exception, "[AutomationEvaluator] Skipped an evaluation tick.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "[AutomationEvaluator] The policy engine stopped unexpectedly.");
        }
    }

    private sealed class PolicyRuntime
    {
        private readonly Dictionary<Guid, (int ConsecutiveRestarts, DateTimeOffset NextAllowed)> _backoff = new();

        internal DateTimeOffset? LastExecuted { get; set; }

        internal DateOnly Day { get; set; }

        internal int DayCount { get; set; }

        internal (int ConsecutiveRestarts, DateTimeOffset NextAllowed) GetBackoff(Guid instanceId) =>
            _backoff.TryGetValue(instanceId, out var state) ? state : (0, DateTimeOffset.MinValue);

        internal void SetBackoff(Guid instanceId, int consecutiveRestarts, DateTimeOffset nextAllowed) =>
            _backoff[instanceId] = (consecutiveRestarts, nextAllowed);

        internal void ClearBackoff(Guid instanceId) => _backoff.Remove(instanceId);
    }

    /// <summary>
    /// An immutable copy of the fact state, so trigger evaluation never reads collections the tick
    /// loop is mutating.
    /// </summary>
    private readonly record struct FactSnapshot(
        IReadOnlyList<Guid> CrashedThisTick,
        IReadOnlyDictionary<Guid, DateTimeOffset[]> CrashTimes);

    /// <summary>
    /// One trigger verdict. <see cref="Targets" /> holds every instance the trigger matched, and is
    /// empty for daemon-wide triggers, whose actions carry their own target.
    /// </summary>
    internal readonly record struct TriggerEvaluation(bool Fires, string Reason, IReadOnlyList<Guid?> Targets)
    {
        internal static TriggerEvaluation Quiet(string reason) => new(false, reason, []);

        internal static TriggerEvaluation Firing(string reason, IReadOnlyList<Guid> targets) =>
            new(true, reason, targets.Count == 0 ? [null] : [.. targets.Select(static id => (Guid?)id)]);
    }
}
