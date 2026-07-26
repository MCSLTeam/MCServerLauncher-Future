using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Backup;

/// <summary>
/// One archived file inside a cold backup, relative to the instance working directory.
/// </summary>
public sealed record BackupFileEntry(string RelativePath, long SizeBytes);

/// <summary>
/// Durable description of one cold backup archive. <see cref="Sha256" /> is the lowercase hex
/// SHA-256 of the final zip bytes; <see cref="ConfigSha256" /> fingerprints the instance
/// configuration file as archived (null when the instance had none).
/// </summary>
public sealed record BackupArchiveManifest(
    Guid ArchiveId,
    Guid InstanceId,
    string InstanceName,
    int ManifestVersion,
    string InstanceVersion,
    string? ConfigSha256,
    DateTimeOffset CreatedAt,
    ImmutableArray<BackupFileEntry> Files,
    long TotalSizeBytes,
    long CompressedSizeBytes,
    string CompressionMethod,
    string Sha256);

public sealed record BackupListQuery(Guid? InstanceId = null, string? OwnerPrincipal = null);

public sealed record BackupListResult(ImmutableArray<BackupArchiveManifest> Archives);

public sealed record BackupCreateRequest(Guid InstanceId, bool Maintenance, string OwnerPrincipal);

public sealed record BackupCreateResult(Guid OperationId);

public sealed record BackupPruneRequest(string OwnerPrincipal);

public sealed record BackupPruneResult(ImmutableArray<Guid> RemovedArchiveIds);

public sealed record BackupRestorePlanRequest(
    Guid ArchiveId,
    Guid InstanceId,
    string CreatorPrincipal,
    string? IdempotencyKey = null,
    TimeSpan? Expiry = null);

public sealed record BackupRestoreConfirmRequest(Guid PlanId, string PlanHash, string ConfirmerPrincipal);

public sealed record BackupRestoreExecuteRequest(Guid PlanId, string ExecutorPrincipal);

public sealed record BackupRestoreExecuteResult(Guid PlanId, Guid OperationId);
