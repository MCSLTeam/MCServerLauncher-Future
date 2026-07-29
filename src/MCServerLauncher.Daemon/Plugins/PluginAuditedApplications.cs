using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Monitoring;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

/// <summary>
/// Fail-open audit recording for the plugin-reachable authorized-application surface.
/// V2RpcDispatcher.RecordAudit covers the RPC path but never routes through
/// PluginApplicationAuthorizer, so a plugin calling an audited mutation through Host or
/// ForPrincipal would otherwise leave no record. This helper reuses RpcAuditPolicy exclusively -
/// the same classification the RPC dispatcher consults - so the two paths cannot drift apart, and
/// a request the policy has not marked audited is never recorded here either.
/// </summary>
internal static class PluginAuditRecorder
{
    // Some plugin-facing methods take no request payload (e.g. GetSystemInfoAsync). None of the
    // methods that reach this marker are ever classified as audited, so it is never rendered.
    private static readonly object NoRequest = new();

    internal static void RecordResult<TResult>(
        IAuditSink? sink,
        string method,
        ICallerContext caller,
        string? pluginId,
        object request,
        Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        if (sink is null || !RpcAuditPolicy.IsAudited(method))
        {
            return;
        }

        RecordCore(sink, method, caller, pluginId, request, result);
    }

    internal static void RecordResult<TResult>(
        IAuditSink? sink,
        string method,
        ICallerContext caller,
        string? pluginId,
        Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        if (sink is null || !RpcAuditPolicy.IsAudited(method))
        {
            return;
        }

        RecordCore(sink, method, caller, pluginId, NoRequest, result);
    }

    /// <summary>
    /// Session-scoped calls (upload/download close, cancel) take a bare <see cref="Guid"/> with no
    /// request DTO of their own; DescribeTarget renders a <see cref="FileSessionReference"/>
    /// instead. Building it is deferred until after the audited-method gate and wrapped by the
    /// same fail-open try/catch as recording itself, so a malformed session id can never surface
    /// as an exception thrown into a read-only (never-audited) call.
    /// </summary>
    internal static void RecordSessionResult<TResult>(
        IAuditSink? sink,
        string method,
        ICallerContext caller,
        string? pluginId,
        Guid sessionId,
        Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        if (sink is null || !RpcAuditPolicy.IsAudited(method))
        {
            return;
        }

        try
        {
            RecordCore(sink, method, caller, pluginId, new FileSessionReference(sessionId), result);
        }
        catch (Exception)
        {
            // Fail-open: see the class remark. A malformed session id must not surface here.
        }
    }

    private static void RecordCore<TResult>(
        IAuditSink sink,
        string method,
        ICallerContext caller,
        string? pluginId,
        object request,
        Result<TResult, DaemonError> result)
        where TResult : notnull
    {
        try
        {
            if (result.IsErr(out var error))
            {
                sink.Record(RpcAuditPolicy.CreateEvent(
                    method, method, caller.Subject, pluginId, request, null, false, error!.Code));
            }
            else
            {
                sink.Record(RpcAuditPolicy.CreateEvent(
                    method, method, caller.Subject, pluginId, request, result.Unwrap(), true, null));
            }
        }
        catch (Exception)
        {
            // Audit is fail-open by contract (see IAuditSink): a recording defect at the plugin
            // boundary must never rewrite the outcome the plugin call already produced.
        }
    }
}

internal sealed class AuditedInstanceCatalog(
    IInstanceSnapshotSource inner) : IInstanceSnapshotSource
{
    // mcsl.instance.catalog.get is permanently read-only (RpcAuditPolicy.ReadOnlyMethods), and this
    // surface is a live published-state view rather than a request/response call, so there is
    // nothing to audit here; the wrapper exists only for uniform coverage of every
    // IPluginAuthorizedApplications member.
    public PublishedState<InstanceCatalogSnapshot> Current => inner.Current;

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) =>
        inner.TryGet(instanceId, out snapshot);
}

internal sealed class AuditedInstanceQueryApplication(
    IInstanceQueryApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IInstanceQueryApplication
{
    public async Task<Result<InstanceReport, DaemonError>> GetInstanceReportAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetInstanceReportAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.report.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
        CancellationToken cancellationToken)
    {
        var result = await inner.ListInstanceReportsAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.report.list", caller, pluginId, result);
        return result;
    }

    public async Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
        InstanceLogQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetInstanceLogAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.log.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetInstanceSettingsAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.settings.get", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedSystemQueryApplication(
    ISystemQueryApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : ISystemQueryApplication
{
    public async Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var result = await inner.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.system.info.get", caller, pluginId, result);
        return result;
    }

    public async Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken)
    {
        var result = await inner.ListJavaRuntimesAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.java.list", caller, pluginId, result);
        return result;
    }
}

internal sealed class AuditedInstanceManagementApplication(
    IInstanceManagementApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IInstanceManagementApplication
{
    public async Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
        CreateInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.create", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> RemoveInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.RemoveInstanceAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.remove", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> StartInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.StartInstanceAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.start", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> StopInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.StopInstanceAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.stop", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> HaltInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.HaltInstanceAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.halt", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> SendCommandAsync(
        InstanceCommandRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.SendCommandAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.command.send", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.UpdateInstanceSettingsAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.settings.update", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedOperationQueryApplication(
    IOperationQueryApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IOperationQueryApplication
{
    public async Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
        OperationListQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ListOperationsAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.operation.list", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
        OperationReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetOperationAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.operation.get", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedOperationControlApplication(
    IOperationControlApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IOperationControlApplication
{
    public async Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
        OperationCancelRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CancelOperationAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.operation.cancel", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedProvisioningApplication(
    IProvisioningApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IProvisioningApplication
{
    public async Task<Result<ProvisioningPlanSnapshot, DaemonError>> ResolveAsync(
        ProvisioningResolveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.provisioning.resolve", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
        ProvisioningPlanReference request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetPlanAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.provisioning.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
        ProvisioningExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.provisioning.execute", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedBackupApplication(
    IBackupApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IBackupApplication
{
    public async Task<Result<BackupListResult, DaemonError>> ListAsync(
        BackupListQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ListAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.list", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<BackupCreateResult, DaemonError>> CreateAsync(
        BackupCreateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.create", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<BackupPruneResult, DaemonError>> PruneAsync(
        BackupPruneRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.PruneAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.prune", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<ProvisioningPlanSnapshot, DaemonError>> PlanRestoreAsync(
        BackupRestorePlanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.PlanRestoreAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.restore.plan", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmRestoreAsync(
        BackupRestoreConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ConfirmRestoreAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.restore.confirm", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<BackupRestoreExecuteResult, DaemonError>> ExecuteRestoreAsync(
        BackupRestoreExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ExecuteRestoreAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.backup.restore.execute", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedAuditApplication(
    IAuditApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IAuditApplication
{
    public async Task<Result<AuditQueryResult, DaemonError>> QueryAsync(
        AuditQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.audit.query", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedMonitoringApplication(
    IMonitoringApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IMonitoringApplication
{
    public async Task<Result<MonitoringCurrentResult, DaemonError>> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var result = await inner.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.monitoring.current.get", caller, pluginId, result);
        return result;
    }

    public async Task<Result<MonitoringQueryResult, DaemonError>> QueryAsync(
        MonitoringQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.monitoring.query", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedAutomationApplication(
    IAutomationApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IAutomationApplication
{
    public async Task<Result<AutomationGetResult, DaemonError>> GetAsync(CancellationToken cancellationToken)
    {
        var result = await inner.GetAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.get", caller, pluginId, result);
        return result;
    }

    public async Task<Result<AutomationValidateResult, DaemonError>> ValidateAsync(
        AutomationValidateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.validate", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<AutomationTestResult, DaemonError>> TestAsync(CancellationToken cancellationToken)
    {
        var result = await inner.TestAsync(cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.test", caller, pluginId, result);
        return result;
    }

    public async Task<Result<AutomationApplyResult, DaemonError>> ApplyAsync(
        AutomationApplyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.apply", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<AutomationApplyResult, DaemonError>> EnableAsync(
        AutomationEnableRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.EnableAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.enable", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmIntentAsync(
        AutomationIntentConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ConfirmIntentAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.intent.confirm", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<AutomationIntentExecuteResult, DaemonError>> ExecuteIntentAsync(
        AutomationIntentExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ExecuteIntentAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.automation.intent.execute", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedEventRuleApplication(
    IEventRuleApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IEventRuleApplication
{
    public async Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(
        EventRuleQuery request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetEventRulesAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.event-rules.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(
        EventRuleUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.UpdateEventRulesAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.instance.event-rules.update", caller, pluginId, request, result);
        return result;
    }
}

internal sealed class AuditedFileReadApplication(
    IFileReadApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IFileReadApplication
{
    public async Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetDirectoryInfoAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.info.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
        PathRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetFileInfoAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.info.get", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
        DownloadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.OpenDownloadAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.download.open", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
        DownloadChunkRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ReadDownloadChunkAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.download.read", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> CloseDownloadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await inner.CloseDownloadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordSessionResult(sink, "mcsl.file.download.close", caller, pluginId, sessionId, result);
        return result;
    }
}

internal sealed class AuditedFileWriteApplication(
    IFileWriteApplication inner,
    ICallerContext caller,
    string? pluginId,
    IAuditSink sink) : IFileWriteApplication
{
    public async Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
        PathRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CreateDirectoryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.create", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> DeleteFileAsync(
        PathRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.DeleteFileAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.delete", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
        DeleteDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.DeleteDirectoryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.delete", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> RenameFileAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.RenameFileAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.rename", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
        PathRenameRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.RenameDirectoryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.rename", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> MoveFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.MoveFileAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.move", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.MoveDirectoryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.move", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> CopyFileAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CopyFileAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.copy", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
        PathTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.CopyDirectoryAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.directory.copy", caller, pluginId, request, result);
        return result;
    }

    public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
        UploadOpenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.OpenUploadAsync(request, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordResult(sink, "mcsl.file.upload.open", caller, pluginId, request, result);
        return result;
    }

    /// <summary>
    /// Chunk bytes have no RPC method of their own on the wire either - they travel as a binary
    /// frame authorized by the lease mcsl.file.upload.open created, and V2FileSessionConnection
    /// never calls RecordAudit for them. Mirroring that, this is a plain delegate with no audited
    /// method name to classify; auditing the open and close is what bounds the write.
    /// </summary>
    public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
        UploadChunkRequest request,
        CancellationToken cancellationToken) =>
        inner.WriteUploadChunkAsync(request, cancellationToken);

    public async Task<Result<Unit, DaemonError>> CloseUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await inner.CloseUploadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordSessionResult(sink, "mcsl.file.upload.close", caller, pluginId, sessionId, result);
        return result;
    }

    public async Task<Result<Unit, DaemonError>> CancelUploadAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await inner.CancelUploadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        PluginAuditRecorder.RecordSessionResult(sink, "mcsl.file.upload.cancel", caller, pluginId, sessionId, result);
        return result;
    }
}
