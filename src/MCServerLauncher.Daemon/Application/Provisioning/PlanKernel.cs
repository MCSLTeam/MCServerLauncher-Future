using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Provisioning;

/// <summary>
/// Metadata-driven immutable plan store. Plans remain readable after restart until expiry;
/// execute always revalidates and uses single-use CAS.
/// </summary>
internal sealed class PlanKernel
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, PlanRecord> _plans = new();
    private readonly ConcurrentDictionary<string, Guid> _idempotency = new(StringComparer.Ordinal);
    private readonly object _persistGate = new();
    private readonly string _root;
    private readonly string _indexPath;
    private readonly TimeProvider _timeProvider;

    public PlanKernel(TimeProvider? timeProvider = null, string? rootDirectory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "plans"));
        Directory.CreateDirectory(_root);
        _indexPath = Path.Combine(_root, "index.json");
        Load();
        PurgeExpired();
    }

    public Result<ProvisioningPlanSnapshot, DaemonError> Put(
        string kind,
        PlanRiskClass riskClass,
        ImmutableArray<string> requiredPermissions,
        bool requiresConfirmation,
        string creatorPrincipal,
        ImmutableArray<ProvisioningUnresolvedFact> unresolved,
        string? idempotencyKey,
        TimeSpan? expiry,
        Func<Guid, string, DateTimeOffset, DateTimeOffset, JsonElement, ProvisioningPlanSnapshot> materialize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorPrincipal);
        ArgumentNullException.ThrowIfNull(materialize);

        // Idempotency claim is atomic: first writer wins, losers reuse the stored plan.
        Guid claimedId = Guid.Empty;
        var hasIdempotency = !string.IsNullOrWhiteSpace(idempotencyKey);
        if (hasIdempotency)
        {
            claimedId = Guid.NewGuid();
            var mapped = _idempotency.GetOrAdd(idempotencyKey!, claimedId);
            if (mapped != claimedId)
            {
                if (_plans.TryGetValue(mapped, out var existing) && !IsTerminalOrExpired(existing.Snapshot))
                    return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(existing.Snapshot);

                // Stale mapping: try to reclaim once.
                if (_idempotency.TryUpdate(idempotencyKey!, claimedId, mapped) is false)
                {
                    if (_idempotency.TryGetValue(idempotencyKey!, out var latest) &&
                        _plans.TryGetValue(latest, out var latestPlan) &&
                        !IsTerminalOrExpired(latestPlan.Snapshot))
                    {
                        return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(latestPlan.Snapshot);
                    }
                }
            }
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now + (expiry is { } ttl && ttl > TimeSpan.Zero ? ttl : DefaultExpiry);
        var planId = hasIdempotency ? claimedId : Guid.NewGuid();
        // Content hash over immutable plan inputs (not random ticks).
        var unresolvedCodes = string.Join(',', (unresolved.IsDefault ? ImmutableArray<ProvisioningUnresolvedFact>.Empty : unresolved).Select(static f => f.Code));
        var planHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{kind}|{riskClass}|{creatorPrincipal}|{requiresConfirmation}|{unresolvedCodes}|{idempotencyKey}|{expiresAt.UtcTicks}"))).ToLowerInvariant();
        var status = unresolved.IsDefaultOrEmpty ? PlanStatus.Ready : PlanStatus.Blocked;
        using var payloadDoc = JsonDocument.Parse(
            $"{{\"kind\":{"\"" + kind.Replace("\\","\\\\").Replace("\"","\\\"") + "\""},\"plan_id\":{"\"" + planId.ToString("D") + "\""}}}");
        var payload = payloadDoc.RootElement.Clone();
        var snapshot = materialize(planId, planHash, now, expiresAt, payload) with
        {
            Status = status,
            RiskClass = riskClass,
            RequiredPermissions = requiredPermissions,
            RequiresConfirmation = requiresConfirmation,
            CreatorPrincipal = creatorPrincipal,
            Unresolved = unresolved.IsDefault ? ImmutableArray<ProvisioningUnresolvedFact>.Empty : unresolved,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
        };

        var record = new PlanRecord(snapshot);
        if (!_plans.TryAdd(planId, record))
        {
            if (_plans.TryGetValue(planId, out var raced) && !IsTerminalOrExpired(raced.Snapshot))
                return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(raced.Snapshot);

            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new InternalDaemonError("plan.create_failed", "The plan could not be created."));
        }

        Persist();
        return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot);
    }

    public Result<ProvisioningPlanSnapshot, DaemonError> Get(Guid planId)
    {
        PurgeExpired();
        if (!_plans.TryGetValue(planId, out var record))
        {
            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new NotFoundDaemonError("plan.not_found", "The plan was not found."));
        }

        if (IsExpired(record.Snapshot))
        {
            Mutate(record, current => current with { Status = PlanStatus.Expired });
            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new ConflictDaemonError("plan.expired", "The plan has expired."));
        }

        return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(record.Snapshot);
    }

    public Result<ProvisioningPlanSnapshot, DaemonError> TryBeginExecute(Guid planId, string executorPrincipal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executorPrincipal);
        PurgeExpired();
        if (!_plans.TryGetValue(planId, out var record))
        {
            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new NotFoundDaemonError("plan.not_found", "The plan was not found."));
        }

        lock (record.Gate)
        {
            var snapshot = record.Snapshot;
            if (IsExpired(snapshot))
            {
                record.Snapshot = snapshot with { Status = PlanStatus.Expired };
                Persist();
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.expired", "The plan has expired."));
            }

            if (snapshot.Status is PlanStatus.Consumed or PlanStatus.Executing)
            {
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.single_flight", "The plan is already executing or consumed."));
            }

            if (snapshot.Status != PlanStatus.Ready)
            {
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.not_ready", "Only ready plans can be executed."));
            }

            // Match OperationCoordinator: do not trust client-supplied admin wildcards.
            if (!string.Equals(snapshot.CreatorPrincipal, executorPrincipal, StringComparison.Ordinal))
            {
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new PermissionDaemonError("plan.forbidden", "The caller cannot execute this plan."));
            }

            // Durable begin: persist Executing before side effects so restart cannot re-open Ready.
            record.Snapshot = snapshot with { Status = PlanStatus.Executing };
            Persist();
            return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot);
        }
    }

    public void CompleteExecute(Guid planId, bool success)
    {
        if (!_plans.TryGetValue(planId, out var record))
            return;

        lock (record.Gate)
        {
            if (success)
            {
                record.Snapshot = record.Snapshot with { Status = PlanStatus.Consumed };
            }
            else if (record.Snapshot.Status == PlanStatus.Executing)
            {
                // Failed execute returns the plan to Ready for a single-flight retry while still valid.
                record.Snapshot = record.Snapshot with { Status = PlanStatus.Ready };
            }

            Persist();
        }
    }

    private void Mutate(PlanRecord record, Func<ProvisioningPlanSnapshot, ProvisioningPlanSnapshot> mutator)
    {
        lock (record.Gate)
        {
            record.Snapshot = mutator(record.Snapshot);
        }
        Persist();
    }

    private void PurgeExpired()
    {
        var now = _timeProvider.GetUtcNow();
        var changed = false;
        foreach (var (id, record) in _plans)
        {
            if (!IsExpired(record.Snapshot) || record.Snapshot.Status == PlanStatus.Expired)
                continue;
            lock (record.Gate)
            {
                record.Snapshot = record.Snapshot with { Status = PlanStatus.Expired };
            }
            changed = true;
            _ = id;
        }

        if (changed)
            Persist();
    }

    private bool IsExpired(ProvisioningPlanSnapshot snapshot) =>
        snapshot.ExpiresAt <= _timeProvider.GetUtcNow();

    private bool IsTerminalOrExpired(ProvisioningPlanSnapshot snapshot) =>
        snapshot.Status is PlanStatus.Consumed or PlanStatus.Expired || IsExpired(snapshot);

    private void Persist()
    {
        lock (_persistGate)
        {
            var snapshots = _plans.Values.Select(static record => record.Snapshot).ToArray();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshots, PlanKernelJsonContext.Default.ProvisioningPlanSnapshotArray);
            var temp = _indexPath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, _indexPath, overwrite: true);
        }
    }

    private void Load()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            var bytes = File.ReadAllBytes(_indexPath);
            var snapshots = JsonSerializer.Deserialize(bytes, PlanKernelJsonContext.Default.ProvisioningPlanSnapshotArray);
            if (snapshots is null)
                return;

            foreach (var snapshot in snapshots)
            {
                _plans[snapshot.PlanId] = new PlanRecord(snapshot);
                if (!string.IsNullOrWhiteSpace(snapshot.IdempotencyKey))
                    _idempotency[snapshot.IdempotencyKey!] = snapshot.PlanId;
            }
        }
        catch (JsonException)
        {
        }
    }

    private sealed class PlanRecord(ProvisioningPlanSnapshot snapshot)
    {
        public object Gate { get; } = new();
        public ProvisioningPlanSnapshot Snapshot { get; set; } = snapshot;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    Converters =
    [
        typeof(JsonStringEnumConverter<PlanStatus>),
        typeof(JsonStringEnumConverter<PlanRiskClass>),
        typeof(JsonStringEnumConverter<ProvisioningProviderKind>),
        typeof(JsonStringEnumConverter<MCServerLauncher.Common.ProtoType.Instance.InstanceFactoryMirror>),
    ])]
[JsonSerializable(typeof(ProvisioningPlanSnapshot[]))]
[JsonSerializable(typeof(object))]
internal partial class PlanKernelJsonContext : JsonSerializerContext;
