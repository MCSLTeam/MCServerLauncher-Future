using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteBackupApplication(IRemoteApplicationInvoker invoker) : IBackupApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<BackupListResult, DaemonError>> ListAsync(
        BackupListQuery request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ListBackups, request, cancellationToken);

    public Task<Result<BackupCreateResult, DaemonError>> CreateAsync(
        BackupCreateRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.CreateBackup, request, cancellationToken);

    public Task<Result<BackupPruneResult, DaemonError>> PruneAsync(
        BackupPruneRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.PruneBackups, request, cancellationToken);

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> PlanRestoreAsync(
        BackupRestorePlanRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.PlanBackupRestore, request, cancellationToken);

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmRestoreAsync(
        BackupRestoreConfirmRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ConfirmBackupRestore, request, cancellationToken);

    public Task<Result<BackupRestoreExecuteResult, DaemonError>> ExecuteRestoreAsync(
        BackupRestoreExecuteRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ExecuteBackupRestore, request, cancellationToken);
}
