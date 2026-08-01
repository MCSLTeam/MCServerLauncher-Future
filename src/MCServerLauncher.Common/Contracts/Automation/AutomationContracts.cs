using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Instance;
using SysTextJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using StjJsonNamingPolicy = System.Text.Json.JsonNamingPolicy;
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
/// system_disk_percent, instance_cpu, instance_memory_bytes.
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
/// Fires when the newest retained monitoring sample shows an instance that has produced no output
/// for at least <see cref="SilentSeconds" />. Null <see cref="InstanceId" /> watches every instance.
/// </summary>
/// <remarks>
/// This is a gauge read of the newest sample, not a windowed series: responsiveness is a live
/// property, and an instance that answered one second ago is responsive whatever it did earlier.
/// An unmeasured silence (null) is not evidence of responsiveness either, so it stays quiet.
/// </remarks>
public sealed class UnresponsiveInstanceTrigger : AutomationTrigger
{
    public override string Type => "instance.unresponsive";

    public Guid? InstanceId { get; set; }

    public double SilentSeconds { get; set; } = 300;
}

/// <summary>
/// Fires while an instance has been continuously in <see cref="Status" /> for at least
/// <see cref="DurationSeconds" />. Null <see cref="InstanceId" /> watches every instance.
/// </summary>
/// <remarks>
/// The duration is measured from the evaluator's own first observation of the status, so a status
/// entered before the daemon started counts from daemon start rather than from when it truly began.
/// The condition stays true for as long as the status holds; the policy cooldown, not the trigger,
/// is what keeps it from acting on every tick.
/// </remarks>
public sealed class StatusDurationTrigger : AutomationTrigger
{
    public override string Type => "instance.status_duration";

    public Guid? InstanceId { get; set; }

    public InstanceStatus Status { get; set; } = InstanceStatus.Stopped;

    public int DurationSeconds { get; set; } = 300;
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

/// <summary>
/// Holds the target instance out of automated hands for <see cref="DurationSeconds" />: while it is
/// active, automated restarts and stops of that instance are refused and the refusal is audited.
/// </summary>
public sealed class MaintenanceStateAction : AutomationAction
{
    public override string Type => "instance.maintenance";

    /// <summary>Null targets the instance that fired the trigger.</summary>
    public Guid? InstanceId { get; set; }

    public int DurationSeconds { get; set; } = 1800;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The narrow half of <see cref="MaintenanceStateAction" />: refuses automated restarts of the
/// target instance while leaving an automated stop available.
/// </summary>
public sealed class RestartSuppressionAction : AutomationAction
{
    public override string Type => "instance.restart_suppression";

    /// <summary>Null targets the instance that fired the trigger.</summary>
    public Guid? InstanceId { get; set; }

    public int DurationSeconds { get; set; } = 1800;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Records a caller-authored line in the audit history and does nothing else. It exists so a policy
/// can leave an explicit trace of a condition it deliberately chose not to act on.
/// </summary>
public sealed class AuditRecordAction : AutomationAction
{
    public override string Type => "audit.record";

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "Info";
}

public sealed record AutomationPolicyDiagnostic(Guid? PolicyId, string Code, string Message);

/// <summary>
/// How much of an instance's automated lifecycle a suppression holds back.
/// </summary>
public enum AutomationSuppressionScope
{
    /// <summary>Automated restarts and stops are both refused.</summary>
    All = 0,

    /// <summary>Only automated restarts are refused.</summary>
    Restarts = 1,
}

/// <summary>
/// One active hold on an instance's automated lifecycle. Suppressions live in memory for the
/// daemon's lifetime only, exactly like the cooldown and backoff guards that sit beside them.
/// </summary>
public sealed record AutomationSuppression(
    Guid InstanceId,
    AutomationSuppressionScope Scope,
    DateTimeOffset UntilUtc,
    string Reason,
    Guid PolicyId);

/// <summary>
/// The applied policy document plus the suppressions currently held over instances. A caller that
/// cannot see the active holds cannot tell a policy that is off from one that is being refused.
/// </summary>
/// <remarks>
/// The list is an init-only property rather than a second constructor parameter: a defaulted
/// <see cref="ImmutableArray{T}" /> parameter is a default instance, and the JSON schema exporter
/// enumerates parameter defaults, which throws on one of those.
/// </remarks>
public sealed record AutomationGetResult(AutomationPolicySet PolicySet)
{
    private readonly ImmutableArray<AutomationSuppression> _suppressions = [];

    public ImmutableArray<AutomationSuppression> Suppressions
    {
        get => _suppressions.IsDefault ? [] : _suppressions;
        init => _suppressions = value;
    }
}

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
    /// <summary>
    /// Every property each kind accepts, discriminator included. Doubles as the known-discriminator
    /// list, so a kind cannot be added to one and forgotten in the other.
    /// </summary>
    private static readonly Dictionary<string, string[]> FieldsByDiscriminator = new(StringComparer.Ordinal)
    {
        ["instance.crash_loop"] = ["type", "instance_id", "max_crashes", "window_seconds"],
        ["instance.unexpected_exit"] = ["type", "instance_id"],
        ["instance.unresponsive"] = ["type", "instance_id", "silent_seconds"],
        ["instance.status_duration"] = ["type", "instance_id", "status", "duration_seconds"],
        ["metric.sustained"] = ["type", "metric", "instance_id", "threshold", "sustained_seconds"],
        ["schedule.window"] = ["type", "start_hour_utc", "start_minute_utc", "duration_minutes"]
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
            "instance.unresponsive" => new UnresponsiveInstanceTrigger(),
            "instance.status_duration" => new StatusDurationTrigger(),
            "metric.sustained" => new SustainedMetricTrigger(),
            "schedule.window" => new MaintenanceWindowTrigger(),
            _ => throw AutomationUnionStjHelper.UnknownDiscriminator(
                nameof(AutomationTrigger),
                discriminator,
                FieldsByDiscriminator.Keys)
        };

        AutomationUnionStjHelper.RejectUnknownProperties(
            obj,
            nameof(AutomationTrigger),
            discriminator,
            FieldsByDiscriminator[discriminator]);

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

            case UnresponsiveInstanceTrigger unresponsive:
                unresponsive.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                unresponsive.SilentSeconds = AutomationUnionStjHelper.ReadDoubleOrDefault(obj, "silent_seconds", unresponsive.SilentSeconds);
                break;

            case StatusDurationTrigger statusDuration:
                statusDuration.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                statusDuration.Status = AutomationUnionStjHelper.ReadInstanceStatusOrDefault(obj, "status", statusDuration.Status);
                statusDuration.DurationSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "duration_seconds", statusDuration.DurationSeconds);
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

            case UnresponsiveInstanceTrigger unresponsive:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", unresponsive.InstanceId);
                writer.WriteNumber("silent_seconds", unresponsive.SilentSeconds);
                break;

            case StatusDurationTrigger statusDuration:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", statusDuration.InstanceId);
                writer.WriteString("status", AutomationUnionStjHelper.InstanceStatusWireName(statusDuration.Status));
                writer.WriteNumber("duration_seconds", statusDuration.DurationSeconds);
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
    /// <summary>
    /// Every property each kind accepts, discriminator included. Doubles as the known-discriminator
    /// list, so a kind cannot be added to one and forgotten in the other.
    /// </summary>
    private static readonly Dictionary<string, string[]> FieldsByDiscriminator = new(StringComparer.Ordinal)
    {
        ["instance.restart"] = ["type", "instance_id", "backoff_base_seconds", "backoff_max_seconds"],
        ["instance.stop"] = ["type", "instance_id"],
        ["instance.maintenance"] = ["type", "instance_id", "duration_seconds", "reason"],
        ["instance.restart_suppression"] = ["type", "instance_id", "duration_seconds", "reason"],
        ["notification"] = ["type", "title", "message", "severity"],
        ["audit.record"] = ["type", "message", "severity"],
        ["plan.confirmation"] = ["type", "summary", "deferred"]
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
            "instance.maintenance" => new MaintenanceStateAction(),
            "instance.restart_suppression" => new RestartSuppressionAction(),
            "notification" => new NotificationAction(),
            "audit.record" => new AuditRecordAction(),
            "plan.confirmation" => new ConfirmationPlanAction(),
            _ => throw AutomationUnionStjHelper.UnknownDiscriminator(
                nameof(AutomationAction),
                discriminator,
                FieldsByDiscriminator.Keys)
        };

        AutomationUnionStjHelper.RejectUnknownProperties(
            obj,
            nameof(AutomationAction),
            discriminator,
            FieldsByDiscriminator[discriminator]);

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

            case MaintenanceStateAction maintenance:
                maintenance.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                maintenance.DurationSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "duration_seconds", maintenance.DurationSeconds);
                maintenance.Reason = AutomationUnionStjHelper.ReadStringOrDefault(obj, "reason", maintenance.Reason);
                break;

            case RestartSuppressionAction restartSuppression:
                restartSuppression.InstanceId = AutomationUnionStjHelper.ReadNullableGuid(obj, "instance_id");
                restartSuppression.DurationSeconds = AutomationUnionStjHelper.ReadIntOrDefault(obj, "duration_seconds", restartSuppression.DurationSeconds);
                restartSuppression.Reason = AutomationUnionStjHelper.ReadStringOrDefault(obj, "reason", restartSuppression.Reason);
                break;

            case NotificationAction notification:
                notification.Title = AutomationUnionStjHelper.ReadStringOrDefault(obj, "title", notification.Title);
                notification.Message = AutomationUnionStjHelper.ReadStringOrDefault(obj, "message", notification.Message);
                notification.Severity = AutomationUnionStjHelper.ReadStringOrDefault(obj, "severity", notification.Severity);
                break;

            case AuditRecordAction auditRecord:
                auditRecord.Message = AutomationUnionStjHelper.ReadStringOrDefault(obj, "message", auditRecord.Message);
                auditRecord.Severity = AutomationUnionStjHelper.ReadStringOrDefault(obj, "severity", auditRecord.Severity);
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

            case MaintenanceStateAction maintenance:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", maintenance.InstanceId);
                writer.WriteNumber("duration_seconds", maintenance.DurationSeconds);
                writer.WriteString("reason", maintenance.Reason);
                break;

            case RestartSuppressionAction restartSuppression:
                AutomationUnionStjHelper.WriteNullableGuid(writer, "instance_id", restartSuppression.InstanceId);
                writer.WriteNumber("duration_seconds", restartSuppression.DurationSeconds);
                writer.WriteString("reason", restartSuppression.Reason);
                break;

            case NotificationAction notification:
                writer.WriteString("title", notification.Title);
                writer.WriteString("message", notification.Message);
                writer.WriteString("severity", notification.Severity);
                break;

            case AuditRecordAction auditRecord:
                writer.WriteString("message", auditRecord.Message);
                writer.WriteString("severity", auditRecord.Severity);
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

    internal static StjJsonException UnknownDiscriminator(string baseTypeName, string discriminator, IEnumerable<string> knownValues) =>
        new($"Unknown {baseTypeName} discriminator '{discriminator}'. Known values: {string.Join(", ", knownValues.Order(StringComparer.Ordinal))}.");

    /// <summary>
    /// Rejects any property the kind does not define or repeats, matching the strictness every
    /// source-generated DTO gets from <c>JsonUnmappedMemberHandling.Disallow</c> and
    /// <c>AllowDuplicateProperties = false</c>.
    /// </summary>
    /// <remarks>
    /// Ignoring an unknown property is the same failure as coercing a malformed one, and worse for
    /// the wildcard-capable kinds: a misspelled 'instance_id' reads as absent, absent means every
    /// instance, and a policy meant for one instance would quietly act on the whole fleet. A
    /// duplicated property is the same harm by a different route — the union reads through a
    /// <c>JsonDocument</c>, which keeps every copy and hands back the last, so a second
    /// 'instance_id' of null would silently widen the policy the first one scoped.
    /// </remarks>
    internal static void RejectUnknownProperties(
        StjJsonElement obj,
        string baseTypeName,
        string discriminator,
        string[] knownProperties)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (!knownProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new StjJsonException(
                    $"Unknown property '{property.Name}' for {baseTypeName} '{discriminator}'. " +
                    $"Known properties: {string.Join(", ", knownProperties)}.");
            }

            if (!seen.Add(property.Name))
            {
                throw new StjJsonException(
                    $"Duplicate property '{property.Name}' for {baseTypeName} '{discriminator}'.");
            }
        }
    }

    // A malformed field is rejected, never coerced: silently defaulting a bad instance_id to null
    // would widen a single-instance policy into a fleet-wide one, and validation runs after
    // conversion so it could never see the loss.
    internal static string ReadStringOrDefault(StjJsonElement obj, string name, string fallback)
    {
        if (!TryGetScalar(obj, name, out var token))
            return fallback;
        if (token.ValueKind != StjJsonValueKind.String)
            throw WrongType(name, "string");
        return token.GetString() ?? fallback;
    }

    internal static int ReadIntOrDefault(StjJsonElement obj, string name, int fallback)
    {
        if (!TryGetScalar(obj, name, out var token))
            return fallback;
        if (token.ValueKind != StjJsonValueKind.Number || !token.TryGetInt32(out var value))
            throw WrongType(name, "32-bit integer");
        return value;
    }

    // A number JSON accepts but a double cannot hold (1e400) reads back as infinity. Keeping it
    // would seat a threshold no reading can reach and then throw on the way back out, because
    // infinity has no JSON form to persist the policy with.
    internal static double ReadDoubleOrDefault(StjJsonElement obj, string name, double fallback)
    {
        if (!TryGetScalar(obj, name, out var token))
            return fallback;
        if (token.ValueKind != StjJsonValueKind.Number)
            throw WrongType(name, "number");
        return token.TryGetDouble(out var value) && double.IsFinite(value)
            ? value
            : throw WrongType(name, "finite number");
    }

    // Derived from the same naming policy the InstanceStatus converter uses, so a status added to
    // the enum reads and writes here identically to everywhere else without a second table to keep.
    private static readonly Dictionary<string, InstanceStatus> InstanceStatusByWireName =
        Enum.GetValues<InstanceStatus>().ToDictionary(InstanceStatusWireName, StringComparer.Ordinal);

    internal static string InstanceStatusWireName(InstanceStatus status) =>
        StjJsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());

    internal static InstanceStatus ReadInstanceStatusOrDefault(StjJsonElement obj, string name, InstanceStatus fallback)
    {
        if (!TryGetScalar(obj, name, out var token))
            return fallback;
        if (token.ValueKind != StjJsonValueKind.String ||
            !InstanceStatusByWireName.TryGetValue(token.GetString() ?? string.Empty, out var value))
        {
            throw WrongType(name, $"instance status ({string.Join(", ", InstanceStatusByWireName.Keys)})");
        }

        return value;
    }

    internal static Guid? ReadNullableGuid(StjJsonElement obj, string name)
    {
        if (!TryGetValue(obj, name, out var token))
            return null;
        if (token.ValueKind != StjJsonValueKind.String || !Guid.TryParse(token.GetString(), out var value))
            throw WrongType(name, "GUID");
        return value;
    }

    private static bool TryGetValue(StjJsonElement obj, string name, out StjJsonElement token) =>
        obj.TryGetProperty(name, out token) && token.ValueKind != StjJsonValueKind.Null;

    // Null on a scalar is not the same as omitting it. Omitting it asks for the default; writing
    // null asserts a value the field cannot hold, and silently substituting the default there is the
    // same quiet coercion an unknown property would be.
    private static bool TryGetScalar(StjJsonElement obj, string name, out StjJsonElement token)
    {
        if (!obj.TryGetProperty(name, out token))
            return false;
        if (token.ValueKind == StjJsonValueKind.Null)
            throw new StjJsonException($"'{name}' cannot be null.");
        return true;
    }

    private static StjJsonException WrongType(string name, string expected) =>
        new($"Cannot convert '{name}' to {expected}.");

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
