using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventTriggerService
{
    private readonly IInstanceManager _instanceManager;
    private readonly ILogger<EventTriggerService> _logger;
    private readonly ConcurrentDictionary<Guid, Timer> _scheduleTimers = new();

    public EventTriggerService(IInstanceManager instanceManager, ILogger<EventTriggerService> logger)
    {
        _instanceManager = instanceManager;
        _logger = logger;
    }

    public void Start()
    {
        foreach (var instance in _instanceManager.RunningInstances.Values)
        {
            HookInstance(instance);
        }
    }

    public void Stop()
    {
        foreach (var timer in _scheduleTimers.Values)
        {
            timer.Dispose();
        }
        _scheduleTimers.Clear();
    }

    public void HookInstance(IInstance instance)
    {
        instance.OnLog -= OnInstanceLog;
        instance.OnLog += OnInstanceLog;

        instance.OnStatusChanged -= OnInstanceStatusChanged;
        instance.OnStatusChanged += OnInstanceStatusChanged;

        // Setup schedule triggers
        SetupScheduleTriggers(instance);
    }

    public void UnhookInstance(IInstance instance)
    {
        instance.OnLog -= OnInstanceLog;
        instance.OnStatusChanged -= OnInstanceStatusChanged;

        // Remove schedule timers for this instance
        // This is a bit complex as we need to track which timers belong to which instance
        // For simplicity, we can just clear all timers and re-setup, or track them properly.
    }

    private void SetupScheduleTriggers(IInstance instance)
    {
        // Simple implementation: just check rules with ScheduleTrigger
        // A real implementation would use a Cron library like NCrontab or Quartz.NET
        // For now, we'll skip the actual cron scheduling to keep it simple, or implement a basic timer.
    }

    private void OnInstanceLog(Guid instanceId, string log)
    {
        if (!_instanceManager.Instances.TryGetValue(instanceId, out var instance)) return;

        var rules = instance.Config.EventRules.Where(r => r.IsEnabled);
        foreach (var rule in rules)
        {
            var consoleTriggers = rule.Triggers.OfType<ConsoleOutputTrigger>().ToList();
            if (!consoleTriggers.Any()) continue;

            bool triggered = false;
            foreach (var trigger in consoleTriggers)
            {
                if (trigger.IsRegex)
                {
                    if (Regex.IsMatch(log, trigger.Pattern))
                    {
                        triggered = true;
                        break;
                    }
                }
                else
                {
                    if (log.Contains(trigger.Pattern))
                    {
                        triggered = true;
                        break;
                    }
                }
            }

            if (triggered)
            {
                _ = ExecuteActionsAsync(instance, rule);
            }
        }
    }

    private void OnInstanceStatusChanged(Guid instanceId, InstanceStatus status)
    {
        if (!_instanceManager.Instances.TryGetValue(instanceId, out var instance)) return;

        var rules = instance.Config.EventRules.Where(r => r.IsEnabled);
        foreach (var rule in rules)
        {
            var statusTriggers = rule.Triggers.OfType<InstanceStatusTrigger>().ToList();
            if (!statusTriggers.Any()) continue;

            bool triggered = false;
            foreach (var trigger in statusTriggers)
            {
                if (status.ToString().Equals(trigger.TargetStatus, StringComparison.OrdinalIgnoreCase))
                {
                    triggered = true;
                    break;
                }
            }

            if (triggered)
            {
                _ = ExecuteActionsAsync(instance, rule);
            }
        }
    }

    private bool EvaluateRulesets(IInstance instance, EventRule rule)
    {
        if (rule.Rulesets == null || !rule.Rulesets.Any())
            return true;

        foreach (var ruleset in rule.Rulesets)
        {
            switch (ruleset)
            {
                case AlwaysTrueRuleset:
                    continue;
                case AlwaysFalseRuleset:
                    return false;
                case InstanceStatusRuleset statusRuleset:
                    if (!instance.Status.ToString().Equals(statusRuleset.TargetStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    private async Task ExecuteActionsAsync(IInstance instance, EventRule rule)
    {
        if (!EvaluateRulesets(instance, rule))
        {
            _logger.LogInformation("Rulesets evaluation failed for rule '{RuleName}' on instance '{InstanceId}'", rule.Name, instance.Config.Uuid);
            return;
        }

        _logger.LogInformation("Executing rule '{RuleName}' for instance '{InstanceId}' in {Mode} mode", rule.Name, instance.Config.Uuid, rule.ActionExecutionMode);

        if (string.Equals(rule.ActionExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var tasks = rule.Actions.Select(action => ExecuteSingleActionAsync(instance, rule, action));
            await Task.WhenAll(tasks);
        }
        else
        {
            foreach (var action in rule.Actions)
            {
                await ExecuteSingleActionAsync(instance, rule, action);
            }
        }
    }

    private async Task ExecuteSingleActionAsync(IInstance instance, EventRule rule, ActionDefinition action)
    {
        try
        {
            switch (action)
            {
                case SendCommandAction sendCommand:
                    _instanceManager.SendToInstance(instance.Config.Uuid, sendCommand.Command);
                    break;
                case ChangeInstanceStatusAction changeStatus:
                    switch (changeStatus.Action.ToLowerInvariant())
                    {
                        case "start":
                            await _instanceManager.TryStartInstance(instance.Config.Uuid);
                            break;
                        case "stop":
                            _instanceManager.TryStopInstance(instance.Config.Uuid);
                            break;
                        case "kill":
                            _instanceManager.KillInstance(instance.Config.Uuid);
                            break;
                        case "restart":
                            _instanceManager.TryStopInstance(instance.Config.Uuid);
                            // Need a way to start it after it stops, maybe a delay or listen to stopped event
                            await Task.Delay(5000); // Simple delay for now
                            await _instanceManager.TryStartInstance(instance.Config.Uuid);
                            break;
                    }
                    break;
                case SendNotificationAction sendNotification:
                    // TODO: Send notification to connected clients via WebSocket
                    // This requires access to WsContextContainer or similar to broadcast
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{ActionType}' for rule '{RuleName}'", action.GetType().Name, rule.Name);
        }
    }
}
