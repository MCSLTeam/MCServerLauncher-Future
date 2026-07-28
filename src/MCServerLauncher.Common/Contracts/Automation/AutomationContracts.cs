using System.Collections.Immutable;
using SysTextJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using StjJsonDocument = System.Text.Json.JsonDocument;
using StjJsonElement = System.Text.Json.JsonElement;
using StjJsonException = System.Text.Json.JsonException;
using StjJsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using StjJsonValueKind = System.Text.Json.JsonValueKind;
using StjUtf8JsonReader = System.Text.Json.Utf8JsonReader;
using StjUtf8JsonWriter = System.Text.Json.Utf8JsonWriter;

namespace MCServerLauncher.Common.Contracts.Automation;

/// <summary>
/// One typed automation policy: a closed-union trigger, a closed-union action list, and the
/// guard rails (cooldown, daily execution cap) the evaluator enforces. Policies never carry
/// shell commands, scripts, or dynamic code by construction.
/// </summary>
public class AutomationPolicy
{
    private ImmutableArray<AutomationAction> _actions = [];

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public AutomationTrigger? Trigger { get; set; }

    public ImmutableArray<AutomationAction> Actions
    {
        get => _actions.IsDefault ? [] : _actions;
        set => _actions = value;
    }

    public int CooldownSeconds { get; set; } = 300;

    public int MaxExecutionsPerDay { get; set; } = 20;
}

/// <summary>
/// The complete applied policy document. <see cref="Version" /> is the compare-and-swap token:
/// apply and enable must echo the current version or fail with a version conflict.
/// </summary>
public class AutomationPolicySet
{
    private ImmutableArray<AutomationPolicy> _policies = [];

    public ImmutableArray<AutomationPolicy> Policies
    {
        get => _policies.IsDefault ? [] : _policies;
        set => _policies = value;
    }

    public long Version { get; set; }
}

/// <summary>
/// Base class for the closed automation trigger union ('type' discriminator).
/// </summary>
[SysTextJsonConverter(typeof(AutomationTriggerStjConverter))]
public abstract class AutomationTrigger
{
    public abstract string Type { get; }
}

/// <summary>
/// Fires when an instance crashes at least <see cref="MaxCrashes" /> times within
/// <see cref="WindowSeconds" />. Null <see cref="InstanceId" /> watches every instance.
/// </summary>
public sealed class CrashLoopTrigger : AutomationTrigger
{
    public override string Type => "instance.crash_loop";

    public Guid? InstanceId { get; set; }

    public int MaxCrashes { get; set; } = 3;

    public int WindowSeconds { get; set; } = 600;
}

/// <summary>
/// Fires on any observed transition into the crashed status.
/// </summary>
public sealed class UnexpectedExitTrigger : AutomationTrigger
{
    public override string Type => "instance.unexpected_exit";

    public Guid? InstanceId { get; set; }
}

/// <summary>
/// Fires when a retained metric stays at or above <see cref="Threshold" /> for
/// <see cref="SustainedSeconds" />. Metrics: system_cpu, system_memory_percent,
/// instance_cpu, instance_memory_bytes.
/// </summary>
public sealed class SustainedMetricTrigger : AutomationTrigger
{
    public override string Type => "metric.sustained";

    public string Metric { get; set; } = "system_cpu";

    public Guid? InstanceId { get; set; }

    public double Threshold { get; set; }

    public int SustainedSeconds { get; set; } = 60;
}

/// <summary>
/// Fires on every evaluation tick inside a daily UTC window; the policy cooldown decides how
/// often actions actually run within it.
/// </summary>
public sealed class MaintenanceWindowTrigger : AutomationTrigger
{
    public override string Type => "schedule.window";

    public int StartHourUtc { get; set; }

    public int StartMinuteUtc { get; set; }

    public int DurationMinutes { get; set; } = 60;
}

/// <summary>
/// Base class for the closed automation action union ('type' discriminator).
/// </summary>
[SysTextJsonConverter(typeof(AutomationActionStjConverter))]
public abstract class AutomationAction
{
    public abstract string Type { get; }
}

/// <summary>
/// Restarts the target instance with exponential backoff between consecutive automated
/// restarts of the same instance.
/// </summary>
public sealed class RestartInstanceAction : AutomationAction
{
    public override string Type => "instance.restart";

    /// <summary>Null targets the instance that fired the trigger.</summary>
    public Guid? InstanceId { get; set; }

    public int BackoffBaseSeconds { get; set; } = 30;

    public int BackoffMaxSeconds { get; set; } = 1800;
}

/// <summary>
/// Requests a graceful stop of the target instance.
/// </summary>
public sealed class StopInstanceAction : AutomationAction
{
    public override string Type => "instance.stop";

    /// <summary>Null targets the instance that fired the trigger.</summary>
    public Guid? InstanceId { get; set; }
}

/// <summary>
/// Publishes a notification event.
/// </summary>
public sealed class NotificationAction : AutomationAction
{
    public override string Type => "notification";

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "Warning";
}

/// <summary>
/// Instead of acting directly, files an immutable confirmation plan carrying the deferred
/// action; a human confirms and executes it through the automation intent methods.
/// </summary>
public sealed class ConfirmationPlanAction : AutomationAction
{
    public override string Type => "plan.confirmation";

    public string Summary { get; set; } = string.Empty;

    /// <summary>The guarded action; nesting another confirmation plan is rejected by validation.</summary>
    public AutomationAction? Deferred { get; set; }
}

public sealed record AutomationPolicyDiagnostic(Guid? PolicyId, string Code, string Message);

public sealed record AutomationGetResult(AutomationPolicySet PolicySet);

public sealed record AutomationValidateRequest(AutomationPolicySet PolicySet);

public sealed record AutomationValidateResult(ImmutableArray<AutomationPolicyDiagnostic> Diagnostics);

/// <summary>
/// Replaces the applied policy document. <c>PolicySet.Version</c> must echo the currently
/// applied version; the stored document is then stamped with the next version.
/// </summary>
public sealed record AutomationApplyRequest(AutomationPolicySet PolicySet);

public sealed record AutomationApplyResult(long Version);

public sealed record AutomationEnableRequest(Guid PolicyId, bool Enabled, long ExpectedVersion);

public sealed record AutomationTestOutcome(Guid PolicyId, bool WouldFire, string Reason, string? Target);

public sealed record AutomationTestResult(ImmutableArray<AutomationTestOutcome> Outcomes);

public sealed record AutomationIntentConfirmRequest(Guid PlanId, string PlanHash, string ConfirmerPrincipal);

public sealed record AutomationIntentExecuteRequest(Guid PlanId, string ExecutorPrincipal);

public sealed record AutomationIntentExecuteResult(Guid PlanId, Guid OperationId);

/// <summary>
/// Canonical STJ wire-contract converter for <see cref="AutomationTrigger" /> polymorphic
/// deserialization via the 'type' discriminator, mirroring the event-rule union pattern.
/// </summary>
internal sealed class AutomationTriggerStjConverter : global::System.Text.Json.Serialization.JsonConverter<AutomationTrigger>
{
    private static readonly string[] KnownDiscriminators =
    {
        "instance.crash_loop",
        "instance.unexpected_exit",
        "metric.sustained",
        "schedule.window"
    };

    public override AutomationTrigger Read(ref StjUtf8JsonReader reader, Type typeToConvert, StjJsonSerializerOptions options)
    {
        using var doc = StjJsonDocument.ParseValue(ref reader);
        var obj = doc.RootElement;
        if (obj.ValueKind == StjJsonValueKind.Null)
        {
            return null!;
        }

        if (obj.ValueKind != StjJsonValueKind.Object)
        {
            throw new StjJsonException($"Expected object for {nameof(AutomationTrigger)}.");
        }

        var discriminator = AutomationUnionStjHelper.ReadDiscriminator(obj, nameof(AutomationTrigger));
        AutomationTrigger trigger = discriminator switch
        {
            "instance.crash_loop" => new CrashLoopTrigger(),
            "instance.unexpected_exit" => new UnexpectedExitTrigger(),
            "metric.sustained" => new SustainedMetricTrigger(),
            "schedule.window" => new MaintenanceWindowTrigger(),
            _ => throw AutomationUnionStjHelper.UnknownDiscriminator(
                nameof(AutomationTrigger),
                discriminator,
                KnownDiscriminators)
        };

        switch (trigger)
        {
            case CrashLoopTrigger crashLoop:
                crashLoop.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                crashLoop.MaxCrashes = AutomationUnionStjHelper.ReadIntOrDefault(obj, "max_crashes", crashLoop.MaxCrashes);
                crashLoop.WindowSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "window_seconds", crashLoop.WindowSeconds);
                break;

            case UnexpectedExitTrigger unexpectedExit:
                unexpectedExit.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                break;

            case SustainedMetricTrigger sustained:
                sustained.Metric = AutomationUnionStjHelper.ReadStringOrDefault(obj, "metric", sustained.Metric);
                sustained.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                sustained.Threshold = AutomationUnionStjHelper.ReadDoubleOrDefault(obj, "threshold", sustained.Threshold);
                sustained.SustainedSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "sustained_seconds", sustained.SustainedSeconds);
                break;

            case MaintenanceWindowTrigger window:
                window.StartHourUtc = AutomationUnionStjHelper.ReadIntOrDefault(obj, "start_hour_utc", window.StartHourUtc);
                window.StartMinuteUtc = AutomationUnionStjHelper.ReadIntOrDefault(obj, "start_minute_utc", window.StartMinuteUtc);
                window.DurationMinutes = AutomationUnionStjHelper.ReadIntOrDefault(obj, "duration_minutes", window.DurationMinutes);
                break;
        }

        return trigger;
    }

    public override void Write(StjUtf8JsonWriter writer, AutomationTrigger value, StjJsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        switch (value)
        {
            case CrashLoopTrigger crashLoop:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", crashLoop.InstanceId);
                writer.WriteNumber("max_crashes", crashLoop.MaxCrashes);
                writer.WriteNumber("window_seconds", crashLoop.WindowSeconds);
                break;

            case UnexpectedExitTrigger unexpectedExit:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", unexpectedExit.InstanceId);
                break;

            case SustainedMetricTrigger sustained:
                writer.WriteString("metric", sustained.Metric);
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", sustained.InstanceId);
                writer.WriteNumber("threshold", sustained.Threshold);
                writer.WriteNumber("sustained_seconds", sustained.SustainedSeconds);
                break;

            case MaintenanceWindowTrigger window:
                writer.WriteNumber("start_hour_utc", window.StartHourUtc);
                writer.WriteNumber("start_minute_utc", window.StartMinuteUtc);
                writer.WriteNumber("duration_minutes", window.DurationMinutes);
                break;

            default:
                throw new StjJsonException(
                    $"Unsupported runtime type '{value.GetType().Name}' for {nameof(AutomationTrigger)}.");
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Canonical STJ wire-contract converter for <see cref="AutomationAction" /> polymorphic
/// deserialization via the 'type' discriminator, mirroring the event-rule union pattern.
/// </summary>
internal sealed class AutomationActionStjConverter : global::System.Text.Json.Serialization.JsonConverter<AutomationAction>
{
    private static readonly string[] KnownDiscriminators =
    {
        "instance.restart",
        "instance.stop",
        "notification",
        "plan.confirmation"
    };

    public override AutomationAction Read(ref StjUtf8JsonReader reader, Type typeToConvert, StjJsonSerializerOptions options)
    {
        using var doc = StjJsonDocument.ParseValue(ref reader);
        var obj = doc.RootElement;
        if (obj.ValueKind == StjJsonValueKind.Null)
        {
            return null!;
        }

        return ReadAction(obj);
    }

    public override void Write(StjUtf8JsonWriter writer, AutomationAction value, StjJsonSerializerOptions options)
    {
        WriteAction(writer, value);
    }

    internal static AutomationAction ReadAction(StjJsonElement obj)
    {
        if (obj.ValueKind != StjJsonValueKind.Object)
        {
            throw new StjJsonException($"Expected object for {nameof(AutomationAction)}.");
        }

        var discriminator = AutomationUnionStjHelper.ReadDiscriminator(obj, nameof(AutomationAction));
        AutomationAction action = discriminator switch
        {
            "instance.restart" => new RestartInstanceAction(),
            "instance.stop" => new StopInstanceAction(),
            "notification" => new NotificationAction(),
            "plan.confirmation" => new ConfirmationPlanAction(),
            _ => throw AutomationUnionStjHelper.UnknownDiscriminator(
                nameof(AutomationAction),
                discriminator,
                KnownDiscriminators)
        };

        switch (action)
        {
            case RestartInstanceAction restart:
                restart.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                restart.BackoffBaseSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "backoff_base_seconds", restart.BackoffBaseSeconds);
                restart.BackoffMaxSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "backoff_max_seconds", restart.BackoffMaxSeconds);
                break;

            case StopInstanceAction stop:
                stop.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                break;

            case NotificationAction notification:
                notification.Title = AutomationUnionStjHelper.ReadStringOrDefault(obj, "title", notification.Title);
                notification.Message = AutomationUnionStjHelper.ReadStringOrDefault(obj, "message", notification.Message);
                notification.Severity = AutomationUnionStjHelper.ReadStringOrDefault(obj, "severity", notification.Severity);
                break;

            case ConfirmationPlanAction confirmation:
                confirmation.Summary = AutomationUnionStjHelper.ReadStringOrDefault(obj, "summary", confirmation.Summary);
                if (obj.TryGetProperty("deferred", out var deferred) && deferred.ValueKind != StjJsonValueKind.Null)
                {
                    confirmation.Deferred = ReadAction(deferred);
                }

                break;
        }

        return action;
    }

    internal static void WriteAction(StjUtf8JsonWriter writer, AutomationAction? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        switch (value)
        {
            case RestartInstanceAction restart:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", restart.InstanceId);
                writer.WriteNumber("backoff_base_seconds", restart.BackoffBaseSeconds);
                writer.WriteNumber("backoff_max_seconds", restart.BackoffMaxSeconds);
                break;

            case StopInstanceAction stop:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", stop.InstanceId);
                break;

            case NotificationAction notification:
                writer.WriteString("title", notification.Title);
                writer.WriteString("message", notification.Message);
                writer.WriteString("severity", notification.Severity);
                break;

            case ConfirmationPlanAction confirmation:
                writer.WriteString("summary", confirmation.Summary);
                writer.WritePropertyName("deferred");
                WriteAction(writer, confirmation.Deferred);
                break;

            default:
                throw new StjJsonException(
                    $"Unsupported runtime type '{value.GetType().Name}' for {nameof(AutomationAction)}.");
        }

        writer.WriteEndObject();
    }
}

internal static class AutomationUnionStjHelper
{
    internal static string ReadDiscriminator(StjJsonElement obj, string baseTypeName)
    {
        if (!obj.TryGetProperty("type", out var typeToken))
        {
            throw new StjJsonException($"Missing discriminator 'type' for {baseTypeName}.");
        }

        if (typeToken.ValueKind != StjJsonValueKind.String)
        {
            throw new StjJsonException($"Invalid discriminator 'type' for {baseTypeName}: expected string.");
        }

        var discriminator = typeToken.GetString();
        if (string.IsNullOrWhiteSpace(discriminator))
        {
            throw new StjJsonException($"Missing discriminator 'type' for {baseTypeName}.");
        }

        return discriminator;
    }

    internal static StjJsonException UnknownDiscriminator(string baseTypeName, string discriminator, string[] knownValues) =>
        new($"Unknown {baseTypeName} discriminator '{discriminator}'. Known values: {string.Join(", ", knownValues)}.");

    internal static string ReadStringOrDefault(StjJsonElement obj, string name, string fallback) =>
        obj.TryGetProperty(name, out var token) && token.ValueKind == StjJsonValueKind.String
            ? token.GetString() ?? fallback
            : fallback;

    internal static int ReadIntOrDefault(StjJsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var token) && token.ValueKind == StjJsonValueKind.Number && token.TryGetInt32(out var value)
            ? value
            : fallback;

    internal static double ReadDoubleOrDefault(StjJsonElement obj, string name, double fallback) =>
        obj.TryGetProperty(name, out var token) && token.ValueKind == StjJsonValueKind.Number
            ? token.GetDouble()
            : fallback;

    internal static Guid? ReadNullableGuid(StjJsonElement obj, string name) =>
        obj.TryGetProperty(name, out var token) &&
        token.ValueKind == StjJsonValueKind.String &&
        Guid.TryParse(token.GetString(), out var value)
            ? value
            : null;

    internal static void WriteNullableGuid(StjUtf8JsonWriter writer, string name, Guid? value)
    {
        if (value is { } guid)
        {
            writer.WriteString(name, guid.ToString("D"));
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
