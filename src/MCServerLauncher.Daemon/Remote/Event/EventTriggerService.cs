using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Notification;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Serialization;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.Daemon.Remote.Event;

public class EventTriggerService
{
    private readonly IInstanceManager _instanceManager;
    private readonly ILogger<EventTriggerService> _logger;
    private readonly WsContextContainer _wsContexts;
    private readonly ConcurrentDictionary<Guid, Timer> _scheduleTimers = new();

    public EventTriggerService(
        IInstanceManager instanceManager,
        ILogger<EventTriggerService> logger,
        WsContextContainer wsContexts)
    {
        _instanceManager = instanceManager;
        _logger = logger;
        _wsContexts = wsContexts;
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
                default:
                    _logger.LogWarning(
                        "Unsupported ruleset type '{RulesetType}' for rule '{RuleId}' on instance '{InstanceId}'",
                        ruleset?.GetType().Name ?? "<null>",
                        rule.Id,
                        instance.Config.Uuid);
                    return false;
            }
        }

        return true;
    }

    private async Task ExecuteActionsAsync(IInstance instance, EventRule rule)
    {
        if (!EvaluateRulesets(instance, rule))
        {
            _logger.LogDebug("Rulesets did not match for rule '{RuleId}' on instance '{InstanceId}'", rule.Id, instance.Config.Uuid);
            return;
        }

        _logger.LogInformation("Executing rule '{RuleId}' for instance '{InstanceId}' in {Mode} mode", rule.Id, instance.Config.Uuid, rule.ActionExecutionMode);

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
                    await SendNotificationAsync(instance, rule, sendNotification);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{ActionType}' for rule '{RuleId}'", action.GetType().Name, rule.Id);
        }
    }

    private async Task SendNotificationAsync(IInstance instance, EventRule rule, SendNotificationAction action)
    {
        var packet = new NotificationPacket
        {
            Title = action.Title,
            Message = action.Message,
            Severity = action.Severity,
            SourceInstanceId = instance.Config.Uuid,
            RuleId = rule.Id
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(packet, DaemonRpcTypeInfoCache<NotificationPacket>.TypeInfo);
        var sendTasks = _wsContexts.Select(context => SendTextFrameAsync(context.Value.GetWebsocket(), payload));
        await Task.WhenAll(sendTasks);
    }

    private static async Task SendTextFrameAsync(IWebSocket webSocket, byte[] utf8Payload)
    {
        var frame = new WSDataFrame(utf8Payload)
        {
            Opcode = WSDataType.Text,
            FIN = true
        };
        await webSocket.SendAsync(frame);
    }
}
