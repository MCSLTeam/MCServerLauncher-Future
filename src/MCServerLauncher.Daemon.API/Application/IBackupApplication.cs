using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Daemon-owned cold backup and confirmed restore. Direct backups and restore execution accept
/// stopped instances only; restore always goes through an immutable destructive plan with
/// two-phase confirmation.
/// </summary>
public interface IBackupApplication
{
    Task<Result<BackupListResult, DaemonError>> ListAsync(
        BackupListQuery request,
        CancellationToken cancellationToken);

    Task<Result<BackupCreateResult, DaemonError>> CreateAsync(
        BackupCreateRequest request,
        CancellationToken cancellationToken);

    Task<Result<BackupPruneResult, DaemonError>> PruneAsync(
        BackupPruneRequest request,
        CancellationToken cancellationToken);

    Task<Result<ProvisioningPlanSnapshot, DaemonError>> PlanRestoreAsync(
        BackupRestorePlanRequest request,
        CancellationToken cancellationToken);

    Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmRestoreAsync(
        BackupRestoreConfirmRequest request,
        CancellationToken cancellationToken);

    Task<Result<BackupRestoreExecuteResult, DaemonError>> ExecuteRestoreAsync(
        BackupRestoreExecuteRequest request,
        CancellationToken cancellationToken);
}
