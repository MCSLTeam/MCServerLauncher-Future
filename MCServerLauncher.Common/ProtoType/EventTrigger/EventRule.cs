using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SysTextJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using StjJsonDocument = System.Text.Json.JsonDocument;
using StjJsonElement = System.Text.Json.JsonElement;
using StjJsonException = System.Text.Json.JsonException;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using StjJsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using StjJsonValueKind = System.Text.Json.JsonValueKind;
using StjUtf8JsonReader = System.Text.Json.Utf8JsonReader;
using StjUtf8JsonWriter = System.Text.Json.Utf8JsonWriter;

namespace MCServerLauncher.Common.ProtoType.EventTrigger
{
    /// <summary>
    /// Represents a complete rule consisting of multiple triggers and actions.
    /// </summary>
    public class EventRule
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// How triggers are evaluated. "All" (AND) or "Any" (OR).
        /// </summary>
        [JsonPropertyName("trigger_condition")]
        public string TriggerCondition { get; set; } = "Any";

        [JsonPropertyName("triggers")]
        public List<TriggerDefinition> Triggers { get; set; } = new();

        [JsonPropertyName("action_execution_mode")]
        public string ActionExecutionMode { get; set; } = "Sequential"; // "Sequential" or "Parallel"

        [JsonPropertyName("rulesets")]
        public List<RulesetDefinition> Rulesets { get; set; } = new();

        [JsonPropertyName("actions")]
        public List<ActionDefinition> Actions { get; set; } = new();
    }

    /// <summary>
    /// Base class for all rulesets (conditions that must be met for actions to execute).
    /// </summary>
    [SysTextJsonConverter(typeof(RulesetDefinitionStjConverter))]
    public abstract class RulesetDefinition
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("type")]
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

        [JsonPropertyName("target_status")]
        public string TargetStatus { get; set; } = string.Empty; // e.g., "Running", "Stopped", "Crashed"
    }

    /// <summary>
    /// Base class for all triggers.
    /// </summary>
    [SysTextJsonConverter(typeof(TriggerDefinitionStjConverter))]
    public abstract class TriggerDefinition
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// Triggered when a specific keyword or regex is found in the console output.
    /// </summary>
    public class ConsoleOutputTrigger : TriggerDefinition
    {
        public override string Type => "ConsoleOutput";

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("is_regex")]
        public bool IsRegex { get; set; } = false;
    }

    /// <summary>
    /// Triggered at a specific time or interval (Cron expression).
    /// </summary>
    public class ScheduleTrigger : TriggerDefinition
    {
        public override string Type => "Schedule";

        [JsonPropertyName("cron_expression")]
        public string CronExpression { get; set; } = string.Empty;
    }

    /// <summary>
    /// Triggered when the instance status changes (e.g., Started, Stopped, Crashed).
    /// </summary>
    public class InstanceStatusTrigger : TriggerDefinition
    {
        public override string Type => "InstanceStatus";

        [JsonPropertyName("target_status")]
        public string TargetStatus { get; set; } = string.Empty; // e.g., "Running", "Stopped", "Crashed"
    }

    /// <summary>
    /// Base class for all actions.
    /// </summary>
    [SysTextJsonConverter(typeof(ActionDefinitionStjConverter))]
    public abstract class ActionDefinition
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }

    /// <summary>
    /// Sends a command to the instance console.
    /// </summary>
    public class SendCommandAction : ActionDefinition
    {
        public override string Type => "SendCommand";

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;
    }

    /// <summary>
    /// Changes the instance status (Start, Stop, Restart, Kill).
    /// </summary>
    public class ChangeInstanceStatusAction : ActionDefinition
    {
        public override string Type => "ChangeInstanceStatus";

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty; // e.g., "Start", "Stop", "Restart", "Kill"
    }

    /// <summary>
    /// Sends a notification to the client.
    /// </summary>
    public class SendNotificationAction : ActionDefinition
    {
        public override string Type => "SendNotification";

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "Info"; // Info, Success, Warning, Error
    }

    /// <summary>
    /// Canonical STJ wire-contract converter for <see cref="RulesetDefinition" /> polymorphic deserialization.
    /// Handles discriminator-based deserialization using the 'type' field.
    /// Discriminator error behavior is locked by characterization tests.
    /// </summary>
    internal sealed class RulesetDefinitionStjConverter : System.Text.Json.Serialization.JsonConverter<RulesetDefinition>
    {
        private static readonly string[] KnownDiscriminators =
        {
            "AlwaysTrue",
            "AlwaysFalse",
            "InstanceStatus"
        };

        public override RulesetDefinition Read(ref StjUtf8JsonReader reader, Type typeToConvert, StjJsonSerializerOptions options)
        {
            using var doc = StjJsonDocument.ParseValue(ref reader);
            var obj = doc.RootElement;

            if (obj.ValueKind == StjJsonValueKind.Null)
            {
                return null!;
            }

            if (obj.ValueKind != StjJsonValueKind.Object)
            {
                throw new StjJsonException($"Expected object for {nameof(RulesetDefinition)}.");
            }

            var discriminator = EventRuleDiscriminatorStjHelper.ReadDiscriminator(obj, nameof(RulesetDefinition));
            RulesetDefinition ruleset = discriminator switch
            {
                "AlwaysTrue" => new AlwaysTrueRuleset(),
                "AlwaysFalse" => new AlwaysFalseRuleset(),
                "InstanceStatus" => new InstanceStatusRuleset(),
                _ => throw EventRuleDiscriminatorStjHelper.UnknownDiscriminator(
                    nameof(RulesetDefinition),
                    discriminator,
                    KnownDiscriminators)
            };

            ruleset.Id = EventRuleDiscriminatorStjHelper.ReadGuidOrDefault(obj, "id", ruleset.Id);

            if (ruleset is InstanceStatusRuleset instanceStatus)
            {
                instanceStatus.TargetStatus = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                    obj,
                    "target_status",
                    instanceStatus.TargetStatus);
            }

            return ruleset;
        }

        public override void Write(StjUtf8JsonWriter writer, RulesetDefinition value, StjJsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("id");
            writer.WriteStringValue(value.Id);
            writer.WriteString("type", value.Type);

            switch (value)
            {
                case InstanceStatusRuleset instanceStatus:
                    writer.WriteString("target_status", instanceStatus.TargetStatus);
                    break;

                case AlwaysTrueRuleset:
                case AlwaysFalseRuleset:
                    break;

                default:
                    throw new StjJsonException(
                        $"Unsupported runtime type '{value.GetType().Name}' for {nameof(RulesetDefinition)}.");
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Canonical STJ wire-contract converter for <see cref="TriggerDefinition" /> polymorphic deserialization.
    /// Handles discriminator-based deserialization using the 'type' field.
    /// Discriminator error behavior is locked by characterization tests.
    /// </summary>
    internal sealed class TriggerDefinitionStjConverter : System.Text.Json.Serialization.JsonConverter<TriggerDefinition>
    {
        private static readonly string[] KnownDiscriminators =
        {
            "ConsoleOutput",
            "Schedule",
            "InstanceStatus"
        };

        public override TriggerDefinition Read(ref StjUtf8JsonReader reader, Type typeToConvert, StjJsonSerializerOptions options)
        {
            using var doc = StjJsonDocument.ParseValue(ref reader);
            var obj = doc.RootElement;

            if (obj.ValueKind == StjJsonValueKind.Null)
            {
                return null!;
            }

            if (obj.ValueKind != StjJsonValueKind.Object)
            {
                throw new StjJsonException($"Expected object for {nameof(TriggerDefinition)}.");
            }

            var discriminator = EventRuleDiscriminatorStjHelper.ReadDiscriminator(obj, nameof(TriggerDefinition));
            TriggerDefinition trigger = discriminator switch
            {
                "ConsoleOutput" => new ConsoleOutputTrigger(),
                "Schedule" => new ScheduleTrigger(),
                "InstanceStatus" => new InstanceStatusTrigger(),
                _ => throw EventRuleDiscriminatorStjHelper.UnknownDiscriminator(
                    nameof(TriggerDefinition),
                    discriminator,
                    KnownDiscriminators)
            };

            trigger.Id = EventRuleDiscriminatorStjHelper.ReadGuidOrDefault(obj, "id", trigger.Id);

            switch (trigger)
            {
                case ConsoleOutputTrigger consoleOutput:
                    consoleOutput.Pattern = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "pattern",
                        consoleOutput.Pattern);
                    consoleOutput.IsRegex = EventRuleDiscriminatorStjHelper.ReadBoolOrDefault(
                        obj,
                        "is_regex",
                        consoleOutput.IsRegex);
                    break;

                case ScheduleTrigger schedule:
                    schedule.CronExpression = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "cron_expression",
                        schedule.CronExpression);
                    break;

                case InstanceStatusTrigger instanceStatus:
                    instanceStatus.TargetStatus = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "target_status",
                        instanceStatus.TargetStatus);
                    break;
            }

            return trigger;
        }

        public override void Write(StjUtf8JsonWriter writer, TriggerDefinition value, StjJsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("id");
            writer.WriteStringValue(value.Id);
            writer.WriteString("type", value.Type);

            switch (value)
            {
                case ConsoleOutputTrigger consoleOutput:
                    writer.WriteString("pattern", consoleOutput.Pattern);
                    writer.WriteBoolean("is_regex", consoleOutput.IsRegex);
                    break;

                case ScheduleTrigger schedule:
                    writer.WriteString("cron_expression", schedule.CronExpression);
                    break;

                case InstanceStatusTrigger instanceStatus:
                    writer.WriteString("target_status", instanceStatus.TargetStatus);
                    break;

                default:
                    throw new StjJsonException(
                        $"Unsupported runtime type '{value.GetType().Name}' for {nameof(TriggerDefinition)}.");
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Canonical STJ wire-contract converter for <see cref="ActionDefinition" /> polymorphic deserialization.
    /// Handles discriminator-based deserialization using the 'type' field.
    /// Discriminator error behavior is locked by characterization tests.
    /// </summary>
    internal sealed class ActionDefinitionStjConverter : System.Text.Json.Serialization.JsonConverter<ActionDefinition>
    {
        private static readonly string[] KnownDiscriminators =
        {
            "SendCommand",
            "ChangeInstanceStatus",
            "SendNotification"
        };

        public override ActionDefinition Read(ref StjUtf8JsonReader reader, Type typeToConvert, StjJsonSerializerOptions options)
        {
            using var doc = StjJsonDocument.ParseValue(ref reader);
            var obj = doc.RootElement;

            if (obj.ValueKind == StjJsonValueKind.Null)
            {
                return null!;
            }

            if (obj.ValueKind != StjJsonValueKind.Object)
            {
                throw new StjJsonException($"Expected object for {nameof(ActionDefinition)}.");
            }

            var discriminator = EventRuleDiscriminatorStjHelper.ReadDiscriminator(obj, nameof(ActionDefinition));
            ActionDefinition action = discriminator switch
            {
                "SendCommand" => new SendCommandAction(),
                "ChangeInstanceStatus" => new ChangeInstanceStatusAction(),
                "SendNotification" => new SendNotificationAction(),
                _ => throw EventRuleDiscriminatorStjHelper.UnknownDiscriminator(
                    nameof(ActionDefinition),
                    discriminator,
                    KnownDiscriminators)
            };

            action.Id = EventRuleDiscriminatorStjHelper.ReadGuidOrDefault(obj, "id", action.Id);

            switch (action)
            {
                case SendCommandAction sendCommand:
                    sendCommand.Command = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "command",
                        sendCommand.Command);
                    break;

                case ChangeInstanceStatusAction changeInstanceStatus:
                    changeInstanceStatus.Action = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "action",
                        changeInstanceStatus.Action);
                    break;

                case SendNotificationAction sendNotification:
                    sendNotification.Title = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "title",
                        sendNotification.Title);
                    sendNotification.Message = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "message",
                        sendNotification.Message);
                    sendNotification.Severity = EventRuleDiscriminatorStjHelper.ReadStringOrDefault(
                        obj,
                        "severity",
                        sendNotification.Severity);
                    break;
            }

            return action;
        }

        public override void Write(StjUtf8JsonWriter writer, ActionDefinition value, StjJsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("id");
            writer.WriteStringValue(value.Id);
            writer.WriteString("type", value.Type);

            switch (value)
            {
                case SendCommandAction sendCommand:
                    writer.WriteString("command", sendCommand.Command);
                    break;

                case ChangeInstanceStatusAction changeInstanceStatus:
                    writer.WriteString("action", changeInstanceStatus.Action);
                    break;

                case SendNotificationAction sendNotification:
                    writer.WriteString("title", sendNotification.Title);
                    writer.WriteString("message", sendNotification.Message);
                    writer.WriteString("severity", sendNotification.Severity);
                    break;

                default:
                    throw new StjJsonException(
                        $"Unsupported runtime type '{value.GetType().Name}' for {nameof(ActionDefinition)}.");
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Discriminator helpers for the canonical STJ converter path.
    /// Error messages and behavior are locked by characterization tests.
    /// </summary>
    internal static class EventRuleDiscriminatorStjHelper
    {
        public static string ReadDiscriminator(StjJsonElement obj, string baseTypeName)
        {
            if (!obj.TryGetProperty("type", out var typeToken) ||
                typeToken.ValueKind is StjJsonValueKind.Null or StjJsonValueKind.Undefined)
            {
                throw new StjJsonException($"Missing discriminator 'type' for {baseTypeName}.");
            }

            if (typeToken.ValueKind != StjJsonValueKind.String)
            {
                throw new StjJsonException(
                    $"Invalid discriminator 'type' for {baseTypeName}: expected string.");
            }

            var discriminator = typeToken.GetString();
            if (string.IsNullOrWhiteSpace(discriminator))
            {
                throw new StjJsonException($"Missing discriminator 'type' for {baseTypeName}.");
            }

            return discriminator;
        }

        public static StjJsonException UnknownDiscriminator(string baseTypeName, string discriminator,
            IReadOnlyCollection<string> knownValues)
        {
            return new StjJsonException(
                $"Unknown {baseTypeName} discriminator '{discriminator}'. Known values: {string.Join(", ", knownValues)}.");
        }

        public static Guid ReadGuidOrDefault(StjJsonElement obj, string propertyName, Guid defaultValue)
        {
            if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind is StjJsonValueKind.Undefined)
            {
                return defaultValue;
            }

            if (value.ValueKind == StjJsonValueKind.Null)
            {
                return Guid.Empty;
            }

            if (value.ValueKind != StjJsonValueKind.String)
            {
                throw new StjJsonException($"Cannot convert {propertyName} to Guid");
            }

            var guid = value.GetString();
            return Guid.TryParse(guid, out var parsed) ? parsed : Guid.Empty;
        }

        public static string ReadStringOrDefault(StjJsonElement obj, string propertyName, string defaultValue)
        {
            if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind is StjJsonValueKind.Undefined)
            {
                return defaultValue;
            }

            if (value.ValueKind == StjJsonValueKind.Null)
            {
                return string.Empty;
            }

            if (value.ValueKind != StjJsonValueKind.String)
            {
                throw new StjJsonException($"Cannot convert {propertyName} to string");
            }

            return value.GetString() ?? string.Empty;
        }

        public static bool ReadBoolOrDefault(StjJsonElement obj, string propertyName, bool defaultValue)
        {
            if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind is StjJsonValueKind.Undefined)
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                StjJsonValueKind.True => true,
                StjJsonValueKind.False => false,
                _ => throw new StjJsonException($"Cannot convert {propertyName} to boolean")
            };
        }
    }
}
