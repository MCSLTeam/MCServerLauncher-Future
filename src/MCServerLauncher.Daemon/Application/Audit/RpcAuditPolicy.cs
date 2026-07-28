using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;

namespace MCServerLauncher.Daemon.ApplicationCore.Audit;

/// <summary>
/// Frozen classification of every built-in RPC as audited mutation or read, plus the typed
/// metadata extraction for audited calls. A new built-in RPC must be added to exactly one set;
/// the coverage test fails otherwise, so audit-worthiness cannot drift silently.
/// </summary>
internal static class RpcAuditPolicy
{
    /// <summary>
    /// Built-in methods recorded in the audit history. Upload close is audited alongside open
    /// because close is the moment the uploaded bytes are committed to the target path; console
    /// open is audited because it grants an interactive input session.
    /// </summary>
    internal static readonly ImmutableHashSet<string> AuditedMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "mcsl.auth.token.issue",
        "mcsl.backup.create",
        "mcsl.backup.prune",
        "mcsl.backup.restore.plan",
        "mcsl.backup.restore.confirm",
        "mcsl.backup.restore.execute",
        "mcsl.directory.copy",
        "mcsl.directory.create",
        "mcsl.directory.delete",
        "mcsl.directory.move",
        "mcsl.directory.rename",
        "mcsl.file.copy",
        "mcsl.file.delete",
        "mcsl.file.move",
        "mcsl.file.rename",
        "mcsl.file.upload.open",
        "mcsl.file.upload.close",
        "mcsl.instance.command.send",
        "mcsl.instance.console.open",
        "mcsl.instance.create",
        "mcsl.instance.event-rules.update",
        "mcsl.instance.halt",
        "mcsl.instance.remove",
        "mcsl.instance.settings.update",
        "mcsl.instance.start",
        "mcsl.instance.stop",
        "mcsl.operation.cancel",
        "mcsl.provisioning.resolve",
        "mcsl.provisioning.execute");

    /// <summary>
    /// Built-in methods deliberately not recorded: pure reads plus session bookkeeping that
    /// mutates nothing durable (upload cancel abandons, console resize/close, event
    /// subscriptions, download sessions).
    /// </summary>
    internal static readonly ImmutableHashSet<string> ReadOnlyMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "mcsl.audit.query",
        "mcsl.auth.permissions.get",
        "mcsl.backup.list",
        "mcsl.daemon.ping",
        "mcsl.directory.info.get",
        "mcsl.event.subscribe",
        "mcsl.event.unsubscribe",
        "mcsl.file.download.close",
        "mcsl.file.download.open",
        "mcsl.file.download.read",
        "mcsl.file.info.get",
        "mcsl.file.upload.cancel",
        "mcsl.instance.catalog.get",
        "mcsl.instance.console.close",
        "mcsl.instance.console.resize",
        "mcsl.instance.event-rules.get",
        "mcsl.instance.log.get",
        "mcsl.instance.report.get",
        "mcsl.instance.report.list",
        "mcsl.instance.settings.get",
        "mcsl.java.list",
        "mcsl.monitoring.current.get",
        "mcsl.monitoring.query",
        "mcsl.operation.get",
        "mcsl.operation.list",
        "mcsl.provisioning.get",
        "mcsl.system.info.get",
        "rpc.discover");

    internal static bool IsAudited(string method) => AuditedMethods.Contains(method);

    /// <summary>
    /// Builds the audit event for one completed audited invocation. Extraction reads identifying
    /// metadata only — command text, rule bodies, file bytes, and issued tokens are never touched.
    /// Result-side identifiers (created plan/operation ids) refine request-side ones on success.
    /// </summary>
    internal static AuditEvent CreateEvent(
        string method,
        string permission,
        string principal,
        string? pluginId,
        object request,
        object? result,
        bool succeeded,
        string? errorCode)
    {
        var target = DescribeTarget(request);
        Guid? planId = request switch
        {
            BackupRestoreConfirmRequest confirm => confirm.PlanId,
            BackupRestoreExecuteRequest execute => execute.PlanId,
            ProvisioningExecuteRequest execute => execute.PlanId,
            _ => null
        };
        var planHash = request is BackupRestoreConfirmRequest confirmRequest ? confirmRequest.PlanHash : null;
        Guid? operationId = request is OperationCancelRequest cancel ? cancel.OperationId : null;
        string? confirmedBy = null;

        switch (result)
        {
            case ProvisioningPlanSnapshot snapshot:
                planId = snapshot.PlanId;
                planHash = snapshot.PlanHash;
                confirmedBy = snapshot.ConfirmedBy;
                break;
            case ProvisioningExecuteResult executed:
                planId = executed.PlanId;
                operationId = executed.OperationId;
                break;
            case BackupCreateResult backupCreated:
                operationId = backupCreated.OperationId;
                break;
            case BackupRestoreExecuteResult restoreExecuted:
                planId = restoreExecuted.PlanId;
                operationId = restoreExecuted.OperationId;
                break;
            case OperationCancelResult cancelled:
                operationId = cancelled.OperationId;
                break;
            case CreateInstanceResult created:
                target = created.Config.InstanceId.ToString("D");
                break;
        }

        return new AuditEvent(
            principal,
            pluginId,
            method,
            permission,
            target,
            planId,
            planHash,
            operationId,
            succeeded,
            errorCode,
            confirmedBy);
    }

    private static string? DescribeTarget(object request) => request switch
    {
        InstanceReference reference => reference.InstanceId.ToString("D"),
        InstanceCommandRequest command => command.InstanceId.ToString("D"),
        ConsoleOpenRequest console => console.InstanceId.ToString("D"),
        UpdateInstanceSettingsRequest settings => settings.InstanceId.ToString("D"),
        EventRuleUpdateRequest rules => rules.InstanceId.ToString("D"),
        PathRequest path => path.Path,
        DeleteDirectoryRequest delete => delete.Path,
        PathRenameRequest rename => rename.Path,
        PathTransferRequest transfer => transfer.SourcePath + " -> " + transfer.DestinationPath,
        UploadOpenRequest upload => upload.Path,
        FileSessionReference session => session.SessionId.ToString("D"),
        TokenIssueRequest token => token.Subject,
        BackupCreateRequest backup => backup.InstanceId.ToString("D"),
        BackupRestorePlanRequest restorePlan => restorePlan.InstanceId.ToString("D"),
        ProvisioningResolveRequest resolve => resolve.InstanceName,
        _ => null
    };
}
