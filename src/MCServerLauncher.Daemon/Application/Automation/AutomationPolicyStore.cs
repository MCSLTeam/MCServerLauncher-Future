using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Automation;

/// <summary>
/// Durable applied-policy document with compare-and-swap versioning. The serialized bytes are the
/// source of truth: readers get a fresh deserialized copy, so no caller can mutate the applied
/// document in place. An unreadable document on load starts empty at version 0 and is logged —
/// automation silently running half a corrupt policy set would be worse than pausing it.
/// </summary>
internal sealed class AutomationPolicyStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILogger? _logger;
    private byte[] _documentUtf8;
    private long _version;

    public AutomationPolicyStore(
        string? rootDirectory = null,
        ILogger<AutomationPolicyStore>? logger = null)
    {
        var root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "automation"));
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "policies.json");
        _logger = logger;
        (_documentUtf8, _version) = Load();
    }

    internal AutomationPolicySet Get()
    {
        lock (_gate)
        {
            return Deserialize(_documentUtf8);
        }
    }

    internal Result<long, DaemonError> Apply(AutomationPolicySet incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var diagnostics = AutomationPolicyValidator.Validate(incoming);
        if (!diagnostics.IsEmpty)
        {
            var first = diagnostics[0];
            return Result.Err<long, DaemonError>(new ValidationDaemonError(
                "automation.policy_invalid",
                $"{first.Code}: {first.Message} ({diagnostics.Length} diagnostic(s) total)"));
        }

        lock (_gate)
        {
            if (incoming.Version != _version)
            {
                return Result.Err<long, DaemonError>(new ConflictDaemonError(
                    "automation.version_conflict",
                    $"The apply expected version {incoming.Version} but the applied document is version {_version}."));
            }

            return CommitLocked(incoming);
        }
    }

    internal Result<long, DaemonError> Enable(Guid policyId, bool enabled, long expectedVersion)
    {
        lock (_gate)
        {
            if (expectedVersion != _version)
            {
                return Result.Err<long, DaemonError>(new ConflictDaemonError(
                    "automation.version_conflict",
                    $"The toggle expected version {expectedVersion} but the applied document is version {_version}."));
            }

            var document = Deserialize(_documentUtf8);
            var policy = document.Policies.FirstOrDefault(candidate => candidate.Id == policyId);
            if (policy is null)
            {
                return Result.Err<long, DaemonError>(new NotFoundDaemonError(
                    "automation.policy_not_found",
                    "The policy was not found in the applied document."));
            }

            policy.Enabled = enabled;
            return CommitLocked(document);
        }
    }

    private Result<long, DaemonError> CommitLocked(AutomationPolicySet document)
    {
        var next = _version + 1;
        document.Version = next;
        byte[] serialized;
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(
                document,
                ApplicationContractJsonContext.Default.AutomationPolicySet);
            var temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, serialized);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result.Err<long, DaemonError>(new StorageDaemonError(
                "automation.persist_failed",
                "The policy document could not be persisted."));
        }

        _documentUtf8 = serialized;
        _version = next;
        return Result.Ok<long, DaemonError>(next);
    }

    private (byte[] DocumentUtf8, long Version) Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var bytes = File.ReadAllBytes(_path);
                var document = JsonSerializer.Deserialize(
                    bytes,
                    ApplicationContractJsonContext.Default.AutomationPolicySet);
                if (document is not null && AutomationPolicyValidator.Validate(document).IsEmpty)
                {
                    return (bytes, document.Version);
                }

                _logger?.LogError(
                    "[AutomationPolicyStore] The persisted policy document at '{Path}' is invalid; automation starts empty.",
                    _path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogError(
                exception,
                "[AutomationPolicyStore] Failed to load '{Path}'; automation starts empty.",
                _path);
        }

        return (SerializeEmpty(), 0);
    }

    private static byte[] SerializeEmpty() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new AutomationPolicySet(),
            ApplicationContractJsonContext.Default.AutomationPolicySet);

    private static AutomationPolicySet Deserialize(byte[] documentUtf8) =>
        JsonSerializer.Deserialize(documentUtf8, ApplicationContractJsonContext.Default.AutomationPolicySet)
        ?? new AutomationPolicySet();
}

/// <summary>
/// Structural validation of a policy document. Every rule is enforced both on apply and on load,
/// so the evaluator only ever sees well-formed policies.
/// </summary>
internal static class AutomationPolicyValidator
{
    private static readonly string[] KnownMetrics =
    [
        "system_cpu",
        "system_memory_percent",
        "system_disk_percent",
        "instance_cpu",
        "instance_memory_bytes"
    ];

    /// <summary>
    /// The metrics whose readings are percentages. A threshold above 100 on one of them is a rule
    /// that can never fire, which is a typo rather than a policy.
    /// </summary>
    private static readonly string[] PercentMetrics =
    [
        "system_cpu",
        "system_memory_percent",
        "system_disk_percent",
        "instance_cpu"
    ];

    private static readonly string[] KnownSeverities = ["Info", "Success", "Warning", "Error"];

    /// <summary>
    /// The audit history is a bounded log shared by every principal, so one policy cannot be allowed
    /// to spend its budget on a single message.
    /// </summary>
    internal const int MaximumAuditMessageLength = 1024;

    internal static ImmutableArray<AutomationPolicyDiagnostic> Validate(AutomationPolicySet document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = ImmutableArray.CreateBuilder<AutomationPolicyDiagnostic>();
        if (document.Version < 0)
        {
            diagnostics.Add(new AutomationPolicyDiagnostic(null, "automation.version_invalid", "The document version cannot be negative."));
        }

        var seen = new HashSet<Guid>();
        foreach (var policy in document.Policies)
        {
            if (policy is null)
            {
                diagnostics.Add(new AutomationPolicyDiagnostic(null, "automation.policy_null", "A policy entry is null."));
                continue;
            }

            if (!seen.Add(policy.Id))
            {
                diagnostics.Add(Diagnostic(policy, "automation.policy_duplicate", "The policy id appears more than once."));
            }

            if (string.IsNullOrWhiteSpace(policy.Name))
            {
                diagnostics.Add(Diagnostic(policy, "automation.name_required", "The policy needs a name."));
            }

            if (policy.CooldownSeconds < 0)
            {
                diagnostics.Add(Diagnostic(policy, "automation.cooldown_invalid", "The cooldown cannot be negative."));
            }

            if (policy.MaxExecutionsPerDay < 1)
            {
                diagnostics.Add(Diagnostic(policy, "automation.cap_invalid", "The daily execution cap must be at least 1."));
            }

            ValidateTrigger(policy, diagnostics);
            if (policy.Actions.Length == 0)
            {
                diagnostics.Add(Diagnostic(policy, "automation.actions_required", "The policy needs at least one action."));
            }

            foreach (var action in policy.Actions)
            {
                ValidateAction(policy, action, diagnostics, allowConfirmation: true);
            }
        }

        return diagnostics.ToImmutable();
    }

    private static void ValidateTrigger(AutomationPolicy policy, ImmutableArray<AutomationPolicyDiagnostic>.Builder diagnostics)
    {
        switch (policy.Trigger)
        {
            case null:
                diagnostics.Add(Diagnostic(policy, "automation.trigger_required", "The policy needs a trigger."));
                break;

            case CrashLoopTrigger crashLoop:
                if (crashLoop.MaxCrashes < 1)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "Crash-loop max crashes must be at least 1."));
                if (crashLoop.WindowSeconds < 10)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The crash-loop window must be at least 10 seconds."));
                // Beyond the retained crash memory the trigger could only ever undercount, so the
                // policy is rejected instead of quietly never firing.
                if (crashLoop.WindowSeconds > AutomationEvaluator.CrashMemory.TotalSeconds)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", $"The crash-loop window cannot exceed {AutomationEvaluator.CrashMemory.TotalHours} hours."));
                break;

            case UnexpectedExitTrigger:
                break;

            case UnresponsiveInstanceTrigger unresponsive:
                if (unresponsive.SilentSeconds <= 0)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The silence threshold must be positive."));
                break;

            case StatusDurationTrigger statusDuration:
                if (statusDuration.DurationSeconds < 1)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The status duration must be at least 1 second."));
                // An undefined status can never equal an observed one, so the policy would be a
                // rule that silently never fires rather than a rule that is off.
                if (!Enum.IsDefined(statusDuration.Status))
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", $"Unknown instance status '{statusDuration.Status}'."));
                break;

            case SustainedMetricTrigger sustained:
                if (!KnownMetrics.Contains(sustained.Metric, StringComparer.Ordinal))
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", $"Unknown metric '{sustained.Metric}'."));
                if (sustained.Threshold <= 0)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The metric threshold must be positive."));
                if (PercentMetrics.Contains(sustained.Metric, StringComparer.Ordinal) && sustained.Threshold > 100)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", $"A percentage threshold cannot exceed 100 for '{sustained.Metric}'."));
                if (sustained.SustainedSeconds < 15)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The sustained window must be at least 15 seconds."));
                break;

            case MaintenanceWindowTrigger window:
                if (window.StartHourUtc is < 0 or > 23)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The window start hour must be 0-23."));
                if (window.StartMinuteUtc is < 0 or > 59)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The window start minute must be 0-59."));
                if (window.DurationMinutes is < 1 or > 1440)
                    diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "The window duration must be 1-1440 minutes."));
                break;

            default:
                diagnostics.Add(Diagnostic(policy, "automation.trigger_invalid", "Unknown trigger type."));
                break;
        }
    }

    private static void ValidateAction(
        AutomationPolicy policy,
        AutomationAction? action,
        ImmutableArray<AutomationPolicyDiagnostic>.Builder diagnostics,
        bool allowConfirmation)
    {
        switch (action)
        {
            case null:
                diagnostics.Add(Diagnostic(policy, "automation.action_null", "An action entry is null."));
                break;

            case RestartInstanceAction restart:
                if (restart.BackoffBaseSeconds < 1)
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "The restart backoff base must be at least 1 second."));
                if (restart.BackoffMaxSeconds < restart.BackoffBaseSeconds)
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "The restart backoff maximum cannot be below its base."));
                break;

            case StopInstanceAction:
                break;

            case MaintenanceStateAction maintenance:
                ValidateSuppressionDuration(policy, maintenance.DurationSeconds, diagnostics);
                break;

            case RestartSuppressionAction restartSuppression:
                ValidateSuppressionDuration(policy, restartSuppression.DurationSeconds, diagnostics);
                break;

            case AuditRecordAction auditRecord:
                if (string.IsNullOrWhiteSpace(auditRecord.Message))
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "An audit record needs a message."));
                if (auditRecord.Message.Length > MaximumAuditMessageLength)
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", $"An audit message cannot exceed {MaximumAuditMessageLength} characters."));
                if (!KnownSeverities.Contains(auditRecord.Severity, StringComparer.Ordinal))
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", $"Unknown audit severity '{auditRecord.Severity}'."));
                break;

            case NotificationAction notification:
                if (string.IsNullOrWhiteSpace(notification.Title) || string.IsNullOrWhiteSpace(notification.Message))
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "A notification needs a title and a message."));
                if (!KnownSeverities.Contains(notification.Severity, StringComparer.Ordinal))
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", $"Unknown notification severity '{notification.Severity}'."));
                break;

            case ConfirmationPlanAction confirmation:
                if (!allowConfirmation)
                {
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "A confirmation plan cannot defer another confirmation plan."));
                    break;
                }

                if (string.IsNullOrWhiteSpace(confirmation.Summary))
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "A confirmation plan needs a summary."));
                if (confirmation.Deferred is null)
                    diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "A confirmation plan needs a deferred action."));
                else
                    ValidateAction(policy, confirmation.Deferred, diagnostics, allowConfirmation: false);
                break;

            default:
                diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "Unknown action type."));
                break;
        }
    }

    /// <summary>
    /// A suppression holds an instance out of automated hands and nothing expires it early, so a
    /// mistyped duration is refused rather than left to hold the instance indefinitely.
    /// </summary>
    private static void ValidateSuppressionDuration(
        AutomationPolicy policy,
        int durationSeconds,
        ImmutableArray<AutomationPolicyDiagnostic>.Builder diagnostics)
    {
        if (durationSeconds < 1)
            diagnostics.Add(Diagnostic(policy, "automation.action_invalid", "The suppression duration must be at least 1 second."));
        if (durationSeconds > AutomationEvaluator.MaximumSuppression.TotalSeconds)
            diagnostics.Add(Diagnostic(policy, "automation.action_invalid", $"The suppression duration cannot exceed {AutomationEvaluator.MaximumSuppression.TotalDays} days."));
    }

    private static AutomationPolicyDiagnostic Diagnostic(AutomationPolicy policy, string code, string message) =>
        new(policy.Id, code, message);
}
