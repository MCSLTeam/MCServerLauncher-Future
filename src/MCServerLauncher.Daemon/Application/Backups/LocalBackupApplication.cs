using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Backups;

internal sealed class LocalBackupApplication(
    BackupArchiveStore store,
    PlanKernel planKernel,
    IInstanceManager instanceManager,
    IInstanceApplication instances,
    IOperationApplication operations,
    TimeSpan? stopExitDeadline = null) : IBackupApplication
{
    internal const string RestorePlanKind = "backup.restore";
    internal const string RestoreOperationKind = "backup.restore.execute";
    private readonly TimeSpan _stopExitDeadline = stopExitDeadline ?? TimeSpan.FromSeconds(30);

    private static readonly ImmutableArray<string> RestorePermissions =
        ["mcsl.backup.restore.plan", "mcsl.backup.restore.confirm", "mcsl.backup.restore.execute"];

    private OperationCoordinator Coordinator =>
        operations as OperationCoordinator
        ?? throw new InvalidOperationException("Backups require OperationCoordinator as IOperationApplication.");

    public Task<Result<BackupListResult, DaemonError>> ListAsync(
        BackupListQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Result.Ok<BackupListResult, DaemonError>(new BackupListResult(store.List(request.InstanceId))));
    }

    public async Task<Result<BackupCreateResult, DaemonError>> CreateAsync(
        BackupCreateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerPrincipal);
        if (!instanceManager.Instances.TryGetValue(request.InstanceId, out var instance))
        {
            return Result.Err<BackupCreateResult, DaemonError>(
                new NotFoundDaemonError("instance.not_found", "The instance was not found."));
        }

        if (!request.Maintenance &&
            instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return Result.Err<BackupCreateResult, DaemonError>(
                new ConflictDaemonError("instance.running", "The instance must be stopped for a direct backup."));
        }

        var start = await Coordinator.StartAsync(
            kind: "backup.create",
            target: request.InstanceId.ToString("D"),
            ownerPrincipal: request.OwnerPrincipal,
            executor: (_, context, ct) => request.Maintenance
                ? RunMaintenanceBackupAsync(instance, context, ct)
                : RunDirectBackupAsync(instance, context, ct),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (start.IsErr(out var startError))
            return Result.Err<BackupCreateResult, DaemonError>(startError!);

        return Result.Ok<BackupCreateResult, DaemonError>(new BackupCreateResult(start.Unwrap().OperationId));
    }

    public Task<Result<BackupPruneResult, DaemonError>> PruneAsync(
        BackupPruneRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pruned = store.Prune(CollectPinnedArchiveIds());
        return Task.FromResult(pruned.IsErr(out var error)
            ? Result.Err<BackupPruneResult, DaemonError>(error!)
            : Result.Ok<BackupPruneResult, DaemonError>(new BackupPruneResult(pruned.Unwrap())));
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> PlanRestoreAsync(
        BackupRestorePlanRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatorPrincipal);
        var archive = store.Get(request.ArchiveId);
        if (archive.IsErr(out var archiveError))
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(archiveError!));

        var manifest = archive.Unwrap();
        if (manifest.InstanceId != request.InstanceId)
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new ValidationDaemonError(
                    "backup.instance_mismatch",
                    "The archive belongs to a different instance.")));
        }

        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new NotFoundDaemonError("instance.not_found", "The restore target instance was not found.")));
        }

        var payload = SerializeRestorePayload(new BackupRestorePayload(manifest.ArchiveId, manifest.Sha256));
        var put = planKernel.Put(
            kind: RestorePlanKind,
            riskClass: PlanRiskClass.Destructive,
            requiredPermissions: RestorePermissions,
            requiresConfirmation: true,
            creatorPrincipal: request.CreatorPrincipal,
            unresolved: [],
            idempotencyKey: string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : $"backup.restore:{request.IdempotencyKey}",
            expiry: request.Expiry,
            materialize: (planId, planHash, createdAt, expiresAt, _) => new ProvisioningPlanSnapshot(
                PlanId: planId,
                PlanHash: planHash,
                Kind: RestorePlanKind,
                Status: PlanStatus.Blocked,
                RiskClass: PlanRiskClass.Destructive,
                RequiredPermissions: RestorePermissions,
                RequiresConfirmation: true,
                CreatorPrincipal: request.CreatorPrincipal,
                CreatedAt: createdAt,
                ExpiresAt: expiresAt,
                Unresolved: [],
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: request.InstanceId.ToString("D"),
                MinecraftVersion: manifest.InstanceVersion,
                Source: manifest.ArchiveId.ToString("D"),
                Mirror: InstanceFactoryMirror.None,
                JavaPath: null,
                IdempotencyKey: null,
                Payload: payload));
        return Task.FromResult(put);
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmRestoreAsync(
        BackupRestoreConfirmRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = planKernel.Get(request.PlanId);
        if (current.IsOk(out var snapshot) &&
            !string.Equals(snapshot!.Kind, RestorePlanKind, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new ValidationDaemonError("plan.kind_mismatch", "The plan is not a backup restore plan.")));
        }

        return Task.FromResult(planKernel.Confirm(request.PlanId, request.PlanHash, request.ConfirmerPrincipal));
    }

    public async Task<Result<BackupRestoreExecuteResult, DaemonError>> ExecuteRestoreAsync(
        BackupRestoreExecuteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var begin = planKernel.TryBeginExecute(request.PlanId, request.ExecutorPrincipal);
        if (begin.IsErr(out var beginError))
            return Result.Err<BackupRestoreExecuteResult, DaemonError>(beginError!);

        var plan = begin.Unwrap();
        if (!string.Equals(plan.Kind, RestorePlanKind, StringComparison.Ordinal))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<BackupRestoreExecuteResult, DaemonError>(
                new ValidationDaemonError("plan.kind_mismatch", "The plan is not a backup restore plan."));
        }

        if (!TryParseRestorePayload(plan.Payload, out var payload))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<BackupRestoreExecuteResult, DaemonError>(
                new ValidationDaemonError("backup.plan_payload_invalid", "The restore plan payload is invalid."));
        }

        Result<OperationSnapshot, DaemonError> execute;
        try
        {
            execute = await Coordinator.StartAsync(
                kind: RestoreOperationKind,
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: request.ExecutorPrincipal,
                executor: (_, context, ct) => RunRestoreAsync(payload, context, ct),
                cancellationToken: cancellationToken,
                terminalCommit: _ => planKernel.CompleteAcceptedExecute(plan.PlanId)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StartAsync throws only before durable operation acceptance. Re-open the plan because
            // no executor can now consume the Executing claim.
            planKernel.AbortExecuteAdmission(plan.PlanId);
            throw;
        }

        if (execute.IsErr(out var executeError))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<BackupRestoreExecuteResult, DaemonError>(executeError!);
        }

        return Result.Ok<BackupRestoreExecuteResult, DaemonError>(
            new BackupRestoreExecuteResult(plan.PlanId, execute.Unwrap().OperationId));
    }

    private async Task<Result<string, DaemonError>> RunDirectBackupAsync(
        IInstance instance,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        context.SetStage(OperationStage.Resolving);
        return await ArchiveStoppedInstanceAsync(instance, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<string, DaemonError>> RunMaintenanceBackupAsync(
        IInstance instance,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        // All children exist before the first step runs: weights normalize against the current
        // children, so late creation would make reported progress jump backwards.
        var stopStep = context.CreateChild("stop", 0.15);
        var archiveStep = context.CreateChild("archive", 0.7);
        var restartStep = context.CreateChild("restart", 0.15);

        context.SetStage(OperationStage.Resolving);
        stopStep.ReportProgress(new OperationProgress(false, 0, 1, "steps", null, null, null));
        // Decide inside the executor: the admission-time state may be stale by the time the
        // supervisor dispatches this. RunningInstances is the predicate start/stop themselves use.
        var weStopped = false;
        if (instanceManager.RunningInstances.ContainsKey(instance.Config.Uuid))
        {
            var stopped = await instances.StopInstanceAsync(
                new InstanceReference(instance.Config.Uuid),
                cancellationToken).ConfigureAwait(false);
            if (stopped.IsErr(out var stopError))
                return Result.Err<string, DaemonError>(stopError!);

            weStopped = true;
            var landed = await WaitForStoppedAsync(instance, cancellationToken).ConfigureAwait(false);
            if (landed.IsErr(out var landError))
            {
                await TryRestartAsync(instance, restartStep).ConfigureAwait(false);
                return Result.Err<string, DaemonError>(landError!);
            }
        }

        stopStep.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));

        Result<string, DaemonError> archived;
        try
        {
            archived = await ArchiveStoppedInstanceAsync(instance, archiveStep, cancellationToken)
                .ConfigureAwait(false);
        }
        catch when (weStopped)
        {
            // The restart is compensating, not sequential: a failed or cancelled archive must not
            // leave a server down that this operation itself stopped.
            await TryRestartAsync(instance, restartStep).ConfigureAwait(false);
            throw;
        }

        restartStep.ReportProgress(new OperationProgress(false, 0, 1, "steps", null, null, null));
        if (weStopped)
        {
            var restarted = await TryRestartAsync(instance, restartStep).ConfigureAwait(false);
            if (archived.IsOk(out _) && restarted.IsErr(out var restartError))
                return Result.Err<string, DaemonError>(restartError!);
        }

        if (archived.IsErr(out var archiveError))
            return Result.Err<string, DaemonError>(archiveError!);

        restartStep.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
        return archived;
    }

    /// <summary>
    /// Best-effort compensating restart, deliberately not bound to the operation token so a
    /// cancelled backup still brings the server back.
    /// </summary>
    private async Task<Result<Unit, DaemonError>> TryRestartAsync(IInstance instance, IOperationContext restartStep)
    {
        restartStep.ReportProgress(new OperationProgress(false, 0, 1, "steps", null, null, null));
        var restarted = await instances.StartInstanceAsync(
            new InstanceReference(instance.Config.Uuid),
            CancellationToken.None).ConfigureAwait(false);
        if (restarted.IsOk(out _))
            restartStep.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
        return restarted;
    }

    private async Task<Result<Unit, DaemonError>> WaitForStoppedAsync(
        IInstance instance,
        CancellationToken cancellationToken)
    {
        // Stop commits Stopping only; archiving a live directory would capture a torn world. The
        // process handle can also be detached before the terminal status lands, so poll the status
        // against the deadline instead of failing the moment the handle is gone.
        var deadline = DateTimeOffset.UtcNow + _stopExitDeadline;
        while (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            if (instance.Process is { } process)
            {
                try
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    await process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    break;
                }

                continue;
            }

            if (DateTimeOffset.UtcNow >= deadline)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return Result.Err<Unit, DaemonError>(
                new ConflictDaemonError(
                    "backup.stop_timeout",
                    "The instance did not reach a stopped state before the maintenance deadline."));
        }

        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    private async Task<Result<string, DaemonError>> ArchiveStoppedInstanceAsync(
        IInstance instance,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        // The mutation lease keeps concurrent start/remove/settings mutations out of the working
        // directory while it is being read. Stop/start application calls acquire the same
        // non-reentrant gate internally, so this scope must never contain them.
        using var mutation = await instanceManager.AcquireInstanceMutationAsync(instance.Config.Uuid, cancellationToken)
            .ConfigureAwait(false);
        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return Result.Err<string, DaemonError>(
                new ConflictDaemonError("instance.running", "The instance must be stopped for a cold backup."));
        }

        context.ReportProgress(new OperationProgress(false, 0, 1, "steps", null, null, null));
        var created = await store.CreateArchiveAsync(
            instance.Config.Uuid,
            instance.Config.Name,
            instance.Config.Version,
            instance.Config.GetWorkingDirectory(),
            InstanceConfig.FileName,
            cancellationToken).ConfigureAwait(false);
        if (created.IsErr(out var createError))
            return Result.Err<string, DaemonError>(createError!);

        // Retention runs on every create so the caps hold without a scheduler; pinned archives
        // (active restore plans) always survive.
        _ = store.Prune(CollectPinnedArchiveIds());
        context.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
        return Result.Ok<string, DaemonError>(created.Unwrap().ArchiveId.ToString("D"));
    }

    private async Task<Result<string, DaemonError>> RunRestoreAsync(
        BackupRestorePayload payload,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        context.SetStage(OperationStage.Verifying);
        var verified = await store.VerifyArchiveAsync(payload.ArchiveId, cancellationToken).ConfigureAwait(false);
        if (verified.IsErr(out var verifyError))
            return Result.Err<string, DaemonError>(verifyError!);

        var manifest = verified.Unwrap();
        if (!string.Equals(manifest.Sha256, payload.ArchiveSha256, StringComparison.Ordinal))
        {
            return Result.Err<string, DaemonError>(
                new ConflictDaemonError(
                    "backup.checksum_mismatch",
                    "The archive content no longer matches the confirmed restore plan."));
        }

        if (!instanceManager.Instances.TryGetValue(manifest.InstanceId, out var instance))
        {
            return Result.Err<string, DaemonError>(
                new NotFoundDaemonError("instance.not_found", "The restore target instance was not found."));
        }

        // Held through the swap: without it a concurrent instance.start could launch the server
        // between the stopped check and the directory replacement.
        using var mutation = await instanceManager.AcquireInstanceMutationAsync(manifest.InstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return Result.Err<string, DaemonError>(
                new ConflictDaemonError("instance.running", "The instance must be stopped before a restore."));
        }

        if (!string.Equals(instance.Config.Version, manifest.InstanceVersion, StringComparison.Ordinal))
        {
            return Result.Err<string, DaemonError>(
                new ConflictDaemonError(
                    "backup.version_mismatch",
                    "The instance version changed since the archive was taken."));
        }

        context.SetStage(OperationStage.Extracting);
        var workingDirectory = Path.GetFullPath(instance.Config.GetWorkingDirectory());
        var instancesRoot = Path.GetDirectoryName(workingDirectory)!;
        var stagingDirectory = Path.Combine(instancesRoot, $".restore-{Guid.NewGuid():N}");
        var retiredDirectory = Path.Combine(instancesRoot, $".restore-old-{Guid.NewGuid():N}");
        try
        {
            var extracted = store.ExtractToStaging(payload.ArchiveId, stagingDirectory, cancellationToken);
            if (extracted.IsErr(out var extractError))
                return Result.Err<string, DaemonError>(extractError!);

            context.SetStage(OperationStage.Installing);
            // Last cooperative observation point. Past the mark the working directory is replaced
            // and the original tree deleted, so a cancellation arriving later must not report
            // "cancelled" over a restore that actually happened.
            cancellationToken.ThrowIfCancellationRequested();
            ((IOperationCommitPoint)context).MarkCommitted();
            MoveDirectoryWithRetry(workingDirectory, retiredDirectory);
            try
            {
                MoveDirectoryWithRetry(stagingDirectory, workingDirectory);
            }
            catch (Exception swapException) when (swapException is IOException or UnauthorizedAccessException)
            {
                try
                {
                    MoveDirectoryWithRetry(retiredDirectory, workingDirectory);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                {
                    return Result.Err<string, DaemonError>(
                        new InternalDaemonError(
                            "backup.restore_manual_recovery_required",
                            $"The restore swap failed and rollback did not complete; the original data remains at '{retiredDirectory}'."));
                }

                return Result.Err<string, DaemonError>(
                    new InternalDaemonError(
                        "backup.restore_swap_failed",
                        "The restore swap failed; the original instance directory was restored."));
            }

            // Past the point of no return: the restore has committed, so cleanup failures (an
            // indexer or scanner briefly holding the retired tree) must not rewrite the outcome.
            TryDeleteDirectory(retiredDirectory);
            context.SetStage(OperationStage.Finalizing);
            return Result.Ok<string, DaemonError>(payload.ArchiveId.ToString("D"));
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover staging/retired trees are swept by name prefix on later runs; a cleanup
            // failure never changes the operation outcome.
        }
    }

    private static void MoveDirectoryWithRetry(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            // One retry absorbs a transient sharing violation (indexer/AV briefly touching the tree).
            Directory.Move(source, destination);
        }
    }

    private IReadOnlySet<Guid> CollectPinnedArchiveIds()
    {
        var pinned = new HashSet<Guid>();
        foreach (var plan in planKernel.ListActive(RestorePlanKind))
        {
            if (TryParseRestorePayload(plan.Payload, out var payload))
                pinned.Add(payload.ArchiveId);
        }

        return pinned;
    }

    private static JsonElement SerializeRestorePayload(BackupRestorePayload payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, payload, BackupPlanPayloadJsonContext.Default.BackupRestorePayload);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool TryParseRestorePayload(JsonElement payload, out BackupRestorePayload parsed)
    {
        try
        {
            var value = payload.Deserialize(BackupPlanPayloadJsonContext.Default.BackupRestorePayload);
            if (value is { ArchiveId: var archiveId } &&
                archiveId != Guid.Empty &&
                !string.IsNullOrWhiteSpace(value.ArchiveSha256))
            {
                parsed = value;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        parsed = default!;
        return false;
    }
}

/// <summary>
/// Typed restore-plan payload; the plan hash canonically covers it, so confirmation binds the
/// exact archive content that will be restored.
/// </summary>
internal sealed record BackupRestorePayload(Guid ArchiveId, string ArchiveSha256);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BackupRestorePayload))]
internal partial class BackupPlanPayloadJsonContext : JsonSerializerContext;
