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
    private static readonly TimeSpan CrashMemory = TimeSpan.FromHours(1);

    private static readonly ImmutableArray<string> ServicePermissions =
        ["mcsl.instance.start", "mcsl.instance.stop"];

    private readonly object _gate = new();
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

            var (fires, _, target) = EvaluateTrigger(policy.Trigger, now);
            if (!fires)
                continue;

            var suppression = CheckGuards(policy, now);
            if (suppression is not null)
            {
                Audit(policy, "automation.policy", Describe(target), false, suppression);
                continue;
            }

            await ExecuteActionsAsync(policy, target, now, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dry run: evaluates every applied policy against the current facts and guard state without
    /// executing anything.
    /// </summary>
    internal ImmutableArray<AutomationTestOutcome> Test()
    {
        var now = _timeProvider.GetUtcNow();
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

            var (fires, reason, target) = EvaluateTrigger(policy.Trigger, now);
            if (!fires)
            {
                outcomes.Add(new AutomationTestOutcome(policy.Id, false, reason, Describe(target)));
                continue;
            }

            var suppression = CheckGuards(policy, now);
            outcomes.Add(suppression is not null
                ? new AutomationTestOutcome(policy.Id, false, $"{reason}; suppressed: {suppression}", Describe(target))
                : new AutomationTestOutcome(policy.Id, true, reason, Describe(target)));
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

    internal (bool Fires, string Reason, Guid? Target) EvaluateTrigger(AutomationTrigger trigger, DateTimeOffset now)
    {
        switch (trigger)
        {
            case UnexpectedExitTrigger unexpectedExit:
            {
                foreach (var crashed in _crashedThisTick)
                {
                    if (unexpectedExit.InstanceId is null || unexpectedExit.InstanceId == crashed)
                        return (true, "instance crashed", crashed);
                }

                return (false, "no crash observed", null);
            }

            case CrashLoopTrigger crashLoop:
            {
                var floor = now - TimeSpan.FromSeconds(crashLoop.WindowSeconds);
                foreach (var (instanceId, times) in _crashTimes)
                {
                    if (crashLoop.InstanceId is not null && crashLoop.InstanceId != instanceId)
                        continue;

                    var recent = times.Count(time => time >= floor);
                    if (recent >= crashLoop.MaxCrashes)
                        return (true, $"{recent} crashes within {crashLoop.WindowSeconds}s", instanceId);
                }

                return (false, "below crash-loop threshold", null);
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
                    ? (true, "inside maintenance window", null)
                    : (false, "outside maintenance window", null);
            }

            default:
                return (false, "unknown trigger", null);
        }
    }

    private (bool Fires, string Reason, Guid? Target) EvaluateSustainedMetric(
        SustainedMetricTrigger sustained,
        DateTimeOffset now)
    {
        var window = TimeSpan.FromSeconds(sustained.SustainedSeconds);
        var result = _metrics.Query(new Common.Contracts.Monitoring.MonitoringQuery(now - window, now));
        var samples = result.Samples.Where(static sample => !sample.Gap).ToArray();
        // Sustained means evidence across the whole window: the oldest usable sample must sit in
        // the window's first sampling interval, and no sample inside may dip below the threshold.
        if (samples.Length == 0 || samples[0].Timestamp > now - window + _interval)
        {
            return (false, "insufficient metric history", null);
        }

        switch (sustained.Metric)
        {
            case "system_cpu":
                return samples.All(sample => sample.SystemCpuPercent >= sustained.Threshold)
                    ? (true, $"system cpu >= {sustained.Threshold} for {sustained.SustainedSeconds}s", null)
                    : (false, "system cpu below threshold", null);

            case "system_memory_percent":
                return samples.All(sample =>
                    sample.MemoryTotalKilobytes > 0 &&
                    sample.MemoryUsedKilobytes * 100.0 / sample.MemoryTotalKilobytes >= sustained.Threshold)
                    ? (true, $"system memory >= {sustained.Threshold}% for {sustained.SustainedSeconds}s", null)
                    : (false, "system memory below threshold", null);

            case "instance_cpu":
            case "instance_memory_bytes":
            {
                var candidates = sustained.InstanceId is { } fixedTarget
                    ? [fixedTarget]
                    : samples.SelectMany(static sample => sample.Instances.Select(static entry => entry.InstanceId))
                        .Distinct()
                        .ToArray();
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
                        return (true, $"{sustained.Metric} >= {sustained.Threshold} for {sustained.SustainedSeconds}s", candidate);
                }

                return (false, $"{sustained.Metric} below threshold", null);
            }

            default:
                return (false, $"unknown metric '{sustained.Metric}'", null);
        }
    }

    private string? CheckGuards(AutomationPolicy policy, DateTimeOffset now)
    {
        var runtime = GetRuntime(policy.Id, now);
        if (runtime.LastExecuted is { } last && now - last < TimeSpan.FromSeconds(policy.CooldownSeconds))
            return "cooldown";

        if (runtime.DayCount >= policy.MaxExecutionsPerDay)
            return "daily execution cap";

        return null;
    }

    private async Task ExecuteActionsAsync(
        AutomationPolicy policy,
        Guid? target,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var runtime = GetRuntime(policy.Id, now);
        runtime.LastExecuted = now;
        runtime.DayCount++;

        foreach (var action in policy.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await ExecuteSingleActionAsync(
                action,
                target,
                policy,
                applyRestartBackoff: true,
                waitForStopBeforeRestart: false,
                cancellationToken).ConfigureAwait(false);
            Audit(
                policy,
                action.Type,
                outcome.IsOk(out var reference) ? reference : Describe(target),
                outcome.IsOk(out _),
                outcome.IsErr(out var error) ? error!.Code : null);
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
        switch (action)
        {
            case RestartInstanceAction restart:
            {
                var instanceId = restart.InstanceId ?? triggerTarget;
                if (instanceId is not { } target)
                    return Err("automation.no_target", "The restart action has no target instance.");

                if (applyRestartBackoff && policy is not null)
                {
                    var runtime = GetRuntime(policy.Id, _timeProvider.GetUtcNow());
                    var backoff = runtime.GetBackoff(target);
                    var now = _timeProvider.GetUtcNow();
                    if (now < backoff.NextAllowed)
                        return Err("automation.backoff", "The restart is suppressed by backoff.");

                    var delaySeconds = Math.Min(
                        restart.BackoffBaseSeconds * Math.Pow(2, backoff.ConsecutiveRestarts),
                        restart.BackoffMaxSeconds);
                    runtime.SetBackoff(target, backoff.ConsecutiveRestarts + 1, now + TimeSpan.FromSeconds(delaySeconds));
                }

                if (!_instances.Instances.TryGetValue(target, out var instance))
                    return Err("instance.not_found", "The restart target was not found.");

                if (instance.Status is InstanceStatus.Stopped or InstanceStatus.Crashed)
                {
                    var start = await _authorizedInstances.StartInstanceAsync(new InstanceReference(target), cancellationToken)
                        .ConfigureAwait(false);
                    return start.IsErr(out var startError)
                        ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(startError!)
                        : Ok(target);
                }

                var stop = await _authorizedInstances.StopInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                if (stop.IsErr(out var stopError))
                    return RustyOptions.Result.Err<string, API.Errors.DaemonError>(stopError!);

                if (!waitForStopBeforeRestart)
                {
                    // The start half runs on a later tick once the stop is observed complete.
                    lock (_gate)
                        _pendingRestartStarts.Add(target);
                    return Ok(target);
                }

                var stopped = await WaitForStoppedAsync(instance, cancellationToken).ConfigureAwait(false);
                if (!stopped)
                    return Err("automation.stop_timeout", "The instance did not stop within the restart deadline.");

                var restartStart = await _authorizedInstances.StartInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                return restartStart.IsErr(out var restartError)
                    ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(restartError!)
                    : Ok(target);
            }

            case StopInstanceAction stopAction:
            {
                var instanceId = stopAction.InstanceId ?? triggerTarget;
                if (instanceId is not { } target)
                    return Err("automation.no_target", "The stop action has no target instance.");

                var stop = await _authorizedInstances.StopInstanceAsync(new InstanceReference(target), cancellationToken)
                    .ConfigureAwait(false);
                return stop.IsErr(out var stopError)
                    ? RustyOptions.Result.Err<string, API.Errors.DaemonError>(stopError!)
                    : Ok(target);
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
                return Ok(triggerTarget);

            case ConfirmationPlanAction confirmation when policy is not null:
                return AutomationIntents.FilePlan(_planKernel, policy, confirmation, triggerTarget);

            case ConfirmationPlanAction:
                return Err("automation.deferred_invalid", "A deferred confirmation plan cannot nest another one.");

            default:
                return Err("automation.action_unknown", "The action type is not executable.");
        }

        static RustyOptions.Result<string, API.Errors.DaemonError> Err(string code, string message) =>
            RustyOptions.Result.Err<string, API.Errors.DaemonError>(
                new API.Errors.ValidationDaemonError(code, message));

        static RustyOptions.Result<string, API.Errors.DaemonError> Ok(Guid? target) =>
            RustyOptions.Result.Ok<string, API.Errors.DaemonError>(target?.ToString("D") ?? "-");
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
        _crashedThisTick.Clear();
        var pendingStarts = new List<Guid>();
        foreach (var (instanceId, instance) in _instances.Instances)
        {
            var status = instance.Status;
            if (_lastStatuses.TryGetValue(instanceId, out var previous) &&
                previous != InstanceStatus.Crashed &&
                status == InstanceStatus.Crashed)
            {
                _crashedThisTick.Add(instanceId);
                if (!_crashTimes.TryGetValue(instanceId, out var times))
                    _crashTimes[instanceId] = times = new List<DateTimeOffset>();
                times.Add(now);
                times.RemoveAll(time => time < now - CrashMemory);
            }

            _lastStatuses[instanceId] = status;
            lock (_gate)
            {
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
            if (start.IsErr(out var error))
            {
                _logger?.LogWarning(
                    "[AutomationEvaluator] Deferred restart start failed for {InstanceId}: {Code}.",
                    instanceId,
                    error!.Code);
            }
        }
    }

    private PolicyRuntime GetRuntime(Guid policyId, DateTimeOffset now)
    {
        if (!_runtime.TryGetValue(policyId, out var runtime))
            _runtime[policyId] = runtime = new PolicyRuntime();

        var day = DateOnly.FromDateTime(now.UtcDateTime);
        if (runtime.Day != day)
        {
            runtime.Day = day;
            runtime.DayCount = 0;
        }

        return runtime;
    }

    private void Audit(AutomationPolicy policy, string permission, string? target, bool succeeded, string? errorCode)
    {
        try
        {
            _auditSink?.Record(new AuditEvent(
                ServicePrincipalSubject,
                null,
                AuditMethod,
                permission,
                target ?? policy.Id.ToString("D"),
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
    }
}
