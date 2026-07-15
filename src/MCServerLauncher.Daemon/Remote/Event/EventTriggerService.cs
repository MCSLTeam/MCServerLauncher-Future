using System.Text.Json;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Event;

internal sealed class EventTriggerService : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private const int Stopped = 0;
    private const int Started = 1;
    private const int Disposed = 2;

    private readonly IDaemonApplication _application;
    private readonly IDomainEventPort _domainEvents;
    private readonly ILogger<EventTriggerService> _logger;
    private readonly TimeSpan _restartDelay;
    private readonly object _lifecycleGate = new();
    private readonly List<OwnedTaskSupervisor> _retiredSupervisors = [];
    private DomainEventOwner? _eventOwner;
    private OwnedTaskSupervisor? _supervisor;
    private int _state;

    public EventTriggerService(
        IDaemonApplication application,
        IDomainEventPort domainEvents,
        ILogger<EventTriggerService> logger)
        : this(application, domainEvents, logger, RestartDelay)
    {
    }

    internal EventTriggerService(
        IDaemonApplication application,
        IDomainEventPort domainEvents,
        ILogger<EventTriggerService> logger,
        TimeSpan restartDelay)
    {
        _application = application;
        _domainEvents = domainEvents;
        _logger = logger;
        _restartDelay = restartDelay;
        Start();
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_state == Started)
                return;
            ObjectDisposedException.ThrowIf(_state == Disposed, this);

            var eventOwner = _domainEvents.CreateOwner(nameof(EventTriggerService));
            var supervisor = new OwnedTaskSupervisor(nameof(EventTriggerService), _logger);
            _eventOwner = eventOwner;
            _supervisor = supervisor;
            _domainEvents.Subscribe<InstanceLogDomainEvent>(eventOwner, (domainEvent, cancellationToken) =>
            {
                supervisor.Schedule(
                    $"log:{domainEvent.InstanceId}",
                    token => ExecuteTriggeredRulesAsync(
                        domainEvent.InstanceId,
                        rule => MatchesConsoleOutput(rule, domainEvent.Log),
                        token),
                    cancellationToken);
                return ValueTask.CompletedTask;
            });
            _domainEvents.Subscribe<InstanceStatusChangedDomainEvent>(eventOwner, (domainEvent, cancellationToken) =>
            {
                supervisor.Schedule(
                    $"status:{domainEvent.InstanceId}",
                    token => ExecuteTriggeredRulesAsync(
                        domainEvent.InstanceId,
                        rule => MatchesStatus(rule, domainEvent.Status.ToString()),
                        token),
                    cancellationToken);
                return ValueTask.CompletedTask;
            });
            Volatile.Write(ref _state, Started);
        }
    }

    public void Stop()
    {
        DomainEventOwner? eventOwner;
        OwnedTaskSupervisor? supervisor;
        lock (_lifecycleGate)
        {
            if (_state != Started)
                return;

            Volatile.Write(ref _state, Stopped);
            eventOwner = _eventOwner;
            supervisor = _supervisor;
            _eventOwner = null;
            _supervisor = null;
            if (supervisor is not null)
                _retiredSupervisors.Add(supervisor);
        }

        supervisor?.RequestStop();
        if (eventOwner is not null)
            _domainEvents.DisposeOwner(eventOwner);
    }

    public void Dispose()
    {
        DomainEventOwner? eventOwner;
        OwnedTaskSupervisor? supervisor;
        lock (_lifecycleGate)
        {
            if (_state == Disposed)
                return;

            Volatile.Write(ref _state, Disposed);
            eventOwner = _eventOwner;
            supervisor = _supervisor;
            _eventOwner = null;
            _supervisor = null;
            if (supervisor is not null)
                _retiredSupervisors.Add(supervisor);
        }

        supervisor?.RequestStop();
        if (eventOwner is not null)
            _domainEvents.DisposeOwner(eventOwner);
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        await DrainSupervisorsAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DrainSupervisorsAsync(CancellationToken.None);
    }

    private async Task DrainSupervisorsAsync(CancellationToken cancellationToken)
    {
        OwnedTaskSupervisor[] supervisors;
        lock (_lifecycleGate)
        {
            supervisors = [.. _retiredSupervisors];
        }

        foreach (var supervisor in supervisors)
        {
            await supervisor.DrainAsync(cancellationToken);
            await supervisor.DisposeAsync();
            lock (_lifecycleGate)
                _retiredSupervisors.Remove(supervisor);
        }
    }

    private async Task ExecuteTriggeredRulesAsync(
        Guid instanceId,
        Func<EventRule, bool> isTriggered,
        CancellationToken cancellationToken)
    {
        try
        {
            var rulesResult = await _application.EventRules.GetEventRulesAsync(
                new EventRuleQuery(instanceId),
                cancellationToken);
            if (rulesResult.IsErr(out var rulesError))
            {
                _logger.LogWarning(
                    "Could not read event rules for instance '{InstanceId}': {ErrorCode} ({ErrorKind}) {ErrorMessage}",
                    instanceId,
                    rulesError?.Code ?? "unknown",
                    rulesError?.Kind.ToString() ?? "unknown",
                    rulesError?.Message ?? "No error details were supplied.");
                return;
            }

            var ruleSet = rulesResult.Unwrap();
            List<EventRule>? rules;
            try
            {
                rules = EventRuleDocumentCodec.Deserialize(ruleSet.Rules);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Ignoring malformed event rules for instance '{InstanceId}'", instanceId);
                return;
            }

            if (rules is null)
            {
                _logger.LogWarning("Ignoring null event rules for instance '{InstanceId}'", instanceId);
                return;
            }

            foreach (var rule in rules.Where(static rule => rule is not null && rule.IsEnabled))
            {
                try
                {
                    if (isTriggered(rule))
                        await ExecuteActionsAsync(instanceId, rule, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "Ignoring malformed rule '{RuleId}' for instance '{InstanceId}'", rule.Id, instanceId);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to process event rules for instance '{InstanceId}'", instanceId);
        }
    }

    private bool MatchesConsoleOutput(EventRule rule, string log)
    {
        foreach (var trigger in rule.Triggers.OfType<ConsoleOutputTrigger>())
        {
            try
            {
                if (trigger.IsRegex
                    ? Regex.IsMatch(log, trigger.Pattern)
                    : log.Contains(trigger.Pattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (ArgumentException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Ignoring invalid console trigger '{TriggerId}' for rule '{RuleId}'",
                    trigger.Id,
                    rule.Id);
            }
        }

        return false;
    }

    private static bool MatchesStatus(EventRule rule, string status)
    {
        return rule.Triggers.OfType<InstanceStatusTrigger>().Any(trigger =>
            string.Equals(status, trigger.TargetStatus, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ExecuteActionsAsync(Guid instanceId, EventRule rule, CancellationToken cancellationToken)
    {
        if (!await EvaluateRulesetsAsync(instanceId, rule, cancellationToken))
        {
            _logger.LogDebug("Rulesets did not match for rule '{RuleId}' on instance '{InstanceId}'", rule.Id, instanceId);
            return;
        }

        _logger.LogInformation(
            "Executing rule '{RuleId}' for instance '{InstanceId}' in {Mode} mode",
            rule.Id,
            instanceId,
            rule.ActionExecutionMode);

        if (string.Equals(rule.ActionExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
        {
            await Task.WhenAll(rule.Actions.Select(action => ExecuteSingleActionAsync(instanceId, rule, action, cancellationToken)));
            return;
        }

        foreach (var action in rule.Actions)
            await ExecuteSingleActionAsync(instanceId, rule, action, cancellationToken);
    }

    private async Task<bool> EvaluateRulesetsAsync(Guid instanceId, EventRule rule, CancellationToken cancellationToken)
    {
        if (rule.Rulesets.Count == 0)
            return true;

        var reportResult = await _application.Instances.GetInstanceReportAsync(
            new InstanceReference(instanceId),
            cancellationToken);
        if (reportResult.IsErr(out var reportError))
        {
            _logger.LogWarning(
                "Could not evaluate rulesets for rule '{RuleId}' on instance '{InstanceId}': {ErrorCode} ({ErrorKind}) {ErrorMessage}",
                rule.Id,
                instanceId,
                reportError?.Code ?? "unknown",
                reportError?.Kind.ToString() ?? "unknown",
                reportError?.Message ?? "No error details were supplied.");
            return false;
        }

        var status = reportResult.Unwrap().Status.ToString();
        foreach (var ruleset in rule.Rulesets)
        {
            switch (ruleset)
            {
                case AlwaysTrueRuleset:
                    break;
                case AlwaysFalseRuleset:
                    return false;
                case InstanceStatusRuleset statusRuleset when string.Equals(
                    status,
                    statusRuleset.TargetStatus,
                    StringComparison.OrdinalIgnoreCase):
                    break;
                case InstanceStatusRuleset:
                    return false;
                default:
                    _logger.LogWarning(
                        "Unsupported ruleset type '{RulesetType}' for rule '{RuleId}' on instance '{InstanceId}'",
                        ruleset?.GetType().Name ?? "<null>",
                        rule.Id,
                        instanceId);
                    return false;
            }
        }

        return true;
    }

    private async Task ExecuteSingleActionAsync(
        Guid instanceId,
        EventRule rule,
        ActionDefinition action,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (action)
            {
                case SendCommandAction sendCommand:
                    LogActionFailure(
                        await _application.Instances.SendCommandAsync(
                            new InstanceCommandRequest(instanceId, sendCommand.Command),
                            cancellationToken),
                        instanceId,
                        rule.Id,
                        sendCommand.Type);
                    break;
                case ChangeInstanceStatusAction changeStatus:
                    await ExecuteStatusActionAsync(instanceId, rule.Id, changeStatus.Action, cancellationToken);
                    break;
                case SendNotificationAction sendNotification:
                    await _domainEvents.PublishAsync(
                        new ClientNotificationDomainEvent(
                            sendNotification.Title,
                            sendNotification.Message,
                            sendNotification.Severity,
                            instanceId,
                            rule.Id,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                        cancellationToken);
                    break;
                default:
                    _logger.LogWarning(
                        "Unsupported event rule action '{RuleActionType}' for rule '{RuleId}' on instance '{InstanceId}'",
                        action.GetType().Name,
                        rule.Id,
                        instanceId);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Error executing action '{RuleActionType}' for rule '{RuleId}'", action.GetType().Name, rule.Id);
        }
    }

    private async Task ExecuteStatusActionAsync(
        Guid instanceId,
        Guid ruleId,
        string action,
        CancellationToken cancellationToken)
    {
        switch (action.ToLowerInvariant())
        {
            case "start":
                LogActionFailure(
                    await _application.Instances.StartInstanceAsync(new InstanceReference(instanceId), cancellationToken),
                    instanceId,
                    ruleId,
                    action);
                break;
            case "stop":
                LogActionFailure(
                    await _application.Instances.StopInstanceAsync(new InstanceReference(instanceId), cancellationToken),
                    instanceId,
                    ruleId,
                    action);
                break;
            case "kill":
                LogActionFailure(
                    await _application.Instances.HaltInstanceAsync(new InstanceReference(instanceId), cancellationToken),
                    instanceId,
                    ruleId,
                    action);
                break;
            case "restart":
                LogActionFailure(
                    await _application.Instances.StopInstanceAsync(new InstanceReference(instanceId), cancellationToken),
                    instanceId,
                    ruleId,
                    "restart.stop");
                await Task.Delay(_restartDelay, cancellationToken);
                LogActionFailure(
                    await _application.Instances.StartInstanceAsync(new InstanceReference(instanceId), cancellationToken),
                    instanceId,
                    ruleId,
                    "restart.start");
                break;
            default:
                _logger.LogWarning(
                    "Unknown instance status action '{StatusAction}' for rule '{RuleId}' on instance '{InstanceId}'",
                    action,
                    ruleId,
                    instanceId);
                break;
        }
    }

    private void LogActionFailure<T>(
        Result<T, DaemonError> result,
        Guid instanceId,
        Guid ruleId,
        string actionType)
        where T : notnull
    {
        if (!result.IsErr(out var error))
            return;

        _logger.LogWarning(
            "Event rule action '{RuleActionType}' failed for rule '{RuleId}' on instance '{InstanceId}': {ErrorCode} ({ErrorKind}) {ErrorMessage}",
            actionType,
            ruleId,
            instanceId,
            error?.Code ?? "unknown",
            error?.Kind.ToString() ?? "unknown",
            error?.Message ?? "No error details were supplied.");
    }
}
