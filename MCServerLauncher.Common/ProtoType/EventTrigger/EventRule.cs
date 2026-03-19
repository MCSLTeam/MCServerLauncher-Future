using System;
using System.Collections.Generic;
using JsonSubTypes;
using Newtonsoft.Json;

namespace MCServerLauncher.Common.ProtoType.EventTrigger
{
    /// <summary>
    /// Represents a complete rule consisting of multiple triggers and actions.
    /// </summary>
    public class EventRule
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("is_enabled")]
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// How triggers are evaluated. "All" (AND) or "Any" (OR).
        /// </summary>
        [JsonProperty("trigger_condition")]
        public string TriggerCondition { get; set; } = "Any";

        [JsonProperty("triggers")]
        public List<TriggerDefinition> Triggers { get; set; } = new();

        [JsonProperty("action_execution_mode")]
        public string ActionExecutionMode { get; set; } = "Sequential"; // "Sequential" or "Parallel"

        [JsonProperty("rulesets")]
        public List<RulesetDefinition> Rulesets { get; set; } = new();

        [JsonProperty("actions")]
        public List<ActionDefinition> Actions { get; set; } = new();
    }

    /// <summary>
    /// Base class for all rulesets (conditions that must be met for actions to execute).
    /// </summary>
    [JsonConverter(typeof(JsonSubtypes), "type")]
    [JsonSubtypes.KnownSubType(typeof(AlwaysTrueRuleset), "AlwaysTrue")]
    [JsonSubtypes.KnownSubType(typeof(AlwaysFalseRuleset), "AlwaysFalse")]
    [JsonSubtypes.KnownSubType(typeof(InstanceStatusRuleset), "InstanceStatus")]
    public abstract class RulesetDefinition
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// Always returns true.
    /// </summary>
    public class AlwaysTrueRuleset : RulesetDefinition
    {
        public override string Type => "AlwaysTrue";
    }

    /// <summary>
    /// Always returns false.
    /// </summary>
    public class AlwaysFalseRuleset : RulesetDefinition
    {
        public override string Type => "AlwaysFalse";
    }

    /// <summary>
    /// Checks if the instance status matches the target status.
    /// </summary>
    public class InstanceStatusRuleset : RulesetDefinition
    {
        public override string Type => "InstanceStatus";

        [JsonProperty("target_status")]
        public string TargetStatus { get; set; } = string.Empty; // e.g., "Running", "Stopped", "Crashed"
    }

    /// <summary>
    /// Base class for all triggers.
    /// </summary>
    [JsonConverter(typeof(JsonSubtypes), "type")]
    [JsonSubtypes.KnownSubType(typeof(ConsoleOutputTrigger), "ConsoleOutput")]
    [JsonSubtypes.KnownSubType(typeof(ScheduleTrigger), "Schedule")]
    [JsonSubtypes.KnownSubType(typeof(InstanceStatusTrigger), "InstanceStatus")]
    public abstract class TriggerDefinition
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// Triggered when a specific keyword or regex is found in the console output.
    /// </summary>
    public class ConsoleOutputTrigger : TriggerDefinition
    {
        public override string Type => "ConsoleOutput";

        [JsonProperty("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonProperty("is_regex")]
        public bool IsRegex { get; set; } = false;
    }

    /// <summary>
    /// Triggered at a specific time or interval (Cron expression).
    /// </summary>
    public class ScheduleTrigger : TriggerDefinition
    {
        public override string Type => "Schedule";

        [JsonProperty("cron_expression")]
        public string CronExpression { get; set; } = string.Empty;
    }

    /// <summary>
    /// Triggered when the instance status changes (e.g., Started, Stopped, Crashed).
    /// </summary>
    public class InstanceStatusTrigger : TriggerDefinition
    {
        public override string Type => "InstanceStatus";

        [JsonProperty("target_status")]
        public string TargetStatus { get; set; } = string.Empty; // e.g., "Running", "Stopped", "Crashed"
    }

    /// <summary>
    /// Base class for all actions.
    /// </summary>
    [JsonConverter(typeof(JsonSubtypes), "type")]
    [JsonSubtypes.KnownSubType(typeof(SendCommandAction), "SendCommand")]
    [JsonSubtypes.KnownSubType(typeof(ChangeInstanceStatusAction), "ChangeInstanceStatus")]
    [JsonSubtypes.KnownSubType(typeof(SendNotificationAction), "SendNotification")]
    public abstract class ActionDefinition
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// Sends a command to the instance console.
    /// </summary>
    public class SendCommandAction : ActionDefinition
    {
        public override string Type => "SendCommand";

        [JsonProperty("command")]
        public string Command { get; set; } = string.Empty;
    }

    /// <summary>
    /// Changes the instance status (Start, Stop, Restart, Kill).
    /// </summary>
    public class ChangeInstanceStatusAction : ActionDefinition
    {
        public override string Type => "ChangeInstanceStatus";

        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty; // e.g., "Start", "Stop", "Restart", "Kill"
    }

    /// <summary>
    /// Sends a notification to the client.
    /// </summary>
    public class SendNotificationAction : ActionDefinition
    {
        public override string Type => "SendNotification";

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("severity")]
        public string Severity { get; set; } = "Info"; // Info, Success, Warning, Error
    }
}
