using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ConcurrentDictionary<IdempotencyScope, IdempotencyClaim> _idempotency = new();
    private readonly object _persistGate = new();
    private readonly string _root;
    private readonly string _indexPath;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlanKernel> _logger;

    public PlanKernel(
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        ILogger<PlanKernel>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PlanKernel>.Instance;
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

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey;
        lock (_persistGate)
        {
            Guid planId;
            do
            {
                planId = Guid.NewGuid();
            } while (_plans.ContainsKey(planId));

            var now = _timeProvider.GetUtcNow();
            var expiresAt = now + (expiry is { } ttl && ttl > TimeSpan.Zero ? ttl : DefaultExpiry);
            var normalizedUnresolved = unresolved.IsDefault
                ? ImmutableArray<ProvisioningUnresolvedFact>.Empty
                : unresolved;
            var status = RequiresBlockedState(riskClass, requiresConfirmation, normalizedUnresolved)
                ? PlanStatus.Blocked
                : PlanStatus.Ready;
            var candidate = materialize(
                planId,
                string.Empty,
                now,
                expiresAt,
                CreateDefaultPayload(kind)) with
            {
                PlanId = planId,
                Kind = kind,
                Status = status,
                RiskClass = riskClass,
                RequiredPermissions = requiredPermissions,
                RequiresConfirmation = requiresConfirmation,
                CreatorPrincipal = creatorPrincipal,
                Unresolved = normalizedUnresolved,
                IdempotencyKey = normalizedIdempotencyKey,
            };
            var planHash = ComputePlanHash(candidate);
            var snapshot = candidate with { PlanHash = planHash };
            ValidateSnapshot(snapshot);

            if (normalizedIdempotencyKey is not null)
            {
                var scope = new IdempotencyScope(creatorPrincipal, normalizedIdempotencyKey);
                if (_idempotency.TryGetValue(scope, out var claim))
                {
                    if (_plans.TryGetValue(claim.PlanId, out var existing) && !IsTerminalOrExpired(existing.Snapshot))
                    {
                        if (!string.Equals(claim.IntentHash, planHash, StringComparison.Ordinal))
                        {
                            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                                new ConflictDaemonError(
                                    "plan.idempotency_conflict",
                                    "The idempotency key is already bound to a different provisioning intent."));
                        }

                        return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(existing.Snapshot);
                    }

                    _idempotency.TryRemove(scope, out _);
                }
            }

            var snapshots = _plans.Values
                .Select(static record => record.Snapshot)
                .Append(snapshot)
                .ToArray();
            PersistSnapshotsUnderLock(snapshots);

            var record = new PlanRecord(snapshot);
            if (!_plans.TryAdd(planId, record))
                throw new InvalidOperationException("A durable plan id collided during publication.");
            if (normalizedIdempotencyKey is not null)
            {
                _idempotency[new IdempotencyScope(creatorPrincipal, normalizedIdempotencyKey)] =
                    new IdempotencyClaim(planId, planHash);
            }

            return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot);
        }
    }

    public Result<ProvisioningPlanSnapshot, DaemonError> Get(Guid planId)
    {
        PurgeExpired();
        if (!_plans.TryGetValue(planId, out var record))
        {
            return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new NotFoundDaemonError("plan.not_found", "The plan was not found."));
        }

        lock (record.Gate)
        {
            var snapshot = record.Snapshot;
            if (snapshot.Status == PlanStatus.Expired)
            {
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.expired", "The plan has expired."));
            }

            if (IsExpired(snapshot))
            {
                PersistAndPublish(record, snapshot with { Status = PlanStatus.Expired });
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.expired", "The plan has expired."));
            }

            return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot);
        }
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
            if (snapshot.Status == PlanStatus.Expired)
            {
                return Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                    new ConflictDaemonError("plan.expired", "The plan has expired."));
            }

            if (IsExpired(snapshot))
            {
                PersistAndPublish(record, snapshot with { Status = PlanStatus.Expired });
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

            // Durable begin distinguishes an abandoned admission claim from an accepted operation
            // during restart reconciliation.
            PersistAndPublish(record, snapshot with { Status = PlanStatus.Executing });
            return Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot);
        }
    }

    public void AbortExecuteAdmission(Guid planId)
    {
        if (!_plans.TryGetValue(planId, out var record))
            return;

        lock (record.Gate)
        {
            if (record.Snapshot.Status != PlanStatus.Executing)
                return;

            PersistAndPublish(record, record.Snapshot with { Status = PlanStatus.Ready });
        }
    }

    public Result<Unit, DaemonError> CompleteAcceptedExecute(Guid planId)
    {
        if (!_plans.TryGetValue(planId, out var record))
        {
            return Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("plan.not_found", "The plan was not found."));
        }

        lock (record.Gate)
        {
            if (record.Snapshot.Status != PlanStatus.Executing)
            {
                return Result.Err<Unit, DaemonError>(
                    new ConflictDaemonError(
                        "plan.invalid_state",
                        "Only an executing plan can commit an accepted execution."));
            }

            try
            {
                PersistAndPublish(record, record.Snapshot with { Status = PlanStatus.Consumed });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Retry one transient filesystem failure. Continuous failure leaves Executing
                // fail-closed; restart reconciliation will consume it from the accepted operation.
                _logger.LogWarning(
                    exception,
                    "Failed to persist accepted plan {PlanId} completion; retrying once.",
                    planId);
                try
                {
                    PersistAndPublish(record, record.Snapshot with { Status = PlanStatus.Consumed });
                }
                catch (Exception retryException) when (retryException is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(
                        retryException,
                        "Failed to persist accepted plan {PlanId} completion after retry.",
                        planId);
                    return Result.Err<Unit, DaemonError>(
                        new InternalDaemonError(
                            "plan.persist_failed",
                            "The accepted plan completion could not be persisted."));
                }
            }

            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
    }

    internal void ReconcileExecutingPlans(OperationCoordinator operations)
    {
        foreach (var (planId, record) in _plans)
        {
            lock (record.Gate)
            {
                if (record.Snapshot.Status != PlanStatus.Executing)
                    continue;

                var accepted = operations.HasAcceptedOperation(
                    "provisioning.execute",
                    planId.ToString("D"));
                PersistAndPublish(
                    record,
                    record.Snapshot with { Status = accepted ? PlanStatus.Consumed : PlanStatus.Ready });
            }
        }
    }

    private void PurgeExpired()
    {
        foreach (var record in _plans.Values)
        {
            lock (record.Gate)
            {
                var snapshot = record.Snapshot;
                if (snapshot.Status == PlanStatus.Expired || !IsExpired(snapshot))
                    continue;

                PersistAndPublish(record, snapshot with { Status = PlanStatus.Expired });
            }
        }
    }

    private bool IsExpired(ProvisioningPlanSnapshot snapshot) =>
        snapshot.Status != PlanStatus.Executing &&
        snapshot.ExpiresAt <= _timeProvider.GetUtcNow();

    private bool IsTerminalOrExpired(ProvisioningPlanSnapshot snapshot) =>
        snapshot.Status is PlanStatus.Consumed or PlanStatus.Expired || IsExpired(snapshot);

    private void PersistAndPublish(PlanRecord record, ProvisioningPlanSnapshot candidate)
    {
        lock (_persistGate)
        {
            PersistUnderLock(record, candidate);
            record.Snapshot = candidate;
        }
    }

    private void PersistUnderLock(
        PlanRecord? overrideRecord,
        ProvisioningPlanSnapshot? overrideSnapshot)
    {
        var snapshots = _plans.Values
            .Select(record => ReferenceEquals(record, overrideRecord) ? overrideSnapshot! : record.Snapshot)
            .ToArray();
        PersistSnapshotsUnderLock(snapshots);
    }

    private void PersistSnapshotsUnderLock(ProvisioningPlanSnapshot[] snapshots)
    {
        foreach (var snapshot in snapshots)
            ValidateSnapshot(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshots, PlanKernelJsonContext.Default.ProvisioningPlanSnapshotArray);
        var temp = _indexPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _indexPath, overwrite: true);
    }

    private void Load()
    {
        if (!File.Exists(_indexPath))
            return;

        ProvisioningPlanSnapshot[] snapshots;
        try
        {
            var bytes = File.ReadAllBytes(_indexPath);
            snapshots = JsonSerializer.Deserialize(bytes, PlanKernelJsonContext.Default.ProvisioningPlanSnapshotArray)
                ?? throw new InvalidDataException("The plan index JSON root cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The plan index contains invalid JSON.", exception);
        }

        var planIds = new HashSet<Guid>();
        var activeIdempotencyScopes = new HashSet<IdempotencyScope>();
        foreach (var snapshot in snapshots)
        {
            ValidateSnapshot(snapshot);
            if (!planIds.Add(snapshot.PlanId))
                throw new InvalidDataException($"The plan index contains duplicate id '{snapshot.PlanId:D}'.");
            if (!string.IsNullOrWhiteSpace(snapshot.IdempotencyKey) &&
                !IsTerminalOrExpired(snapshot) &&
                !activeIdempotencyScopes.Add(new IdempotencyScope(snapshot.CreatorPrincipal, snapshot.IdempotencyKey)))
            {
                throw new InvalidDataException(
                    $"The plan index contains duplicate idempotency key '{snapshot.IdempotencyKey}' for principal '{snapshot.CreatorPrincipal}'.");
            }
        }

        foreach (var snapshot in snapshots)
        {
            _plans.TryAdd(snapshot.PlanId, new PlanRecord(snapshot));
            if (!string.IsNullOrWhiteSpace(snapshot.IdempotencyKey) && !IsTerminalOrExpired(snapshot))
            {
                _idempotency.TryAdd(
                    new IdempotencyScope(snapshot.CreatorPrincipal, snapshot.IdempotencyKey),
                    new IdempotencyClaim(snapshot.PlanId, snapshot.PlanHash));
            }
        }
    }

    private static JsonElement CreateDefaultPayload(string kind)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            writer.WriteEndObject();
            writer.Flush();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static string ComputePlanHash(ProvisioningPlanSnapshot snapshot)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", snapshot.Kind);
            writer.WriteString("risk_class", snapshot.RiskClass.ToString());
            writer.WriteStartArray("required_permissions");
            foreach (var permission in snapshot.RequiredPermissions.OrderBy(static value => value, StringComparer.Ordinal))
                writer.WriteStringValue(permission);
            writer.WriteEndArray();
            writer.WriteBoolean("requires_confirmation", snapshot.RequiresConfirmation);
            writer.WriteStartArray("unresolved");
            foreach (var fact in snapshot.Unresolved
                .OrderBy(static value => value.Code, StringComparer.Ordinal)
                .ThenBy(static value => value.Field, StringComparer.Ordinal)
                .ThenBy(static value => value.Message, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("code", fact.Code);
                writer.WriteString("message", fact.Message);
                writer.WriteString("field", fact.Field);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("provider", snapshot.Provider.ToString());
            writer.WriteString("instance_name", snapshot.InstanceName);
            writer.WriteString("minecraft_version", snapshot.MinecraftVersion);
            writer.WriteString("source", snapshot.Source);
            writer.WriteString("mirror", snapshot.Mirror.ToString());
            writer.WriteString("java_path", snapshot.JavaPath);
            writer.WritePropertyName("payload");
            WriteCanonicalJson(writer, snapshot.Payload);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                value.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("Plan payload contains an undefined JSON value.");
        }
    }

    private static void ValidateSnapshot(ProvisioningPlanSnapshot snapshot)
    {
        if (snapshot is null ||
            snapshot.PlanId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshot.PlanHash) ||
            snapshot.PlanHash.Length != 64 ||
            snapshot.PlanHash.Any(static character => !Uri.IsHexDigit(character)) ||
            string.IsNullOrWhiteSpace(snapshot.Kind) ||
            string.IsNullOrWhiteSpace(snapshot.CreatorPrincipal) ||
            !Enum.IsDefined(snapshot.Status) ||
            !Enum.IsDefined(snapshot.RiskClass) ||
            !Enum.IsDefined(snapshot.Provider) ||
            !Enum.IsDefined(snapshot.Mirror) ||
            snapshot.RequiredPermissions.IsDefault ||
            snapshot.RequiredPermissions.Any(string.IsNullOrWhiteSpace) ||
            snapshot.Unresolved.IsDefault ||
            snapshot.Unresolved.Any(static fact =>
                fact is null ||
                string.IsNullOrWhiteSpace(fact.Code) ||
                string.IsNullOrWhiteSpace(fact.Message)) ||
            snapshot.CreatedAt == default ||
            snapshot.ExpiresAt <= snapshot.CreatedAt ||
            snapshot.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The plan index contains an invalid plan record.");
        }

        var missingInstanceName = string.IsNullOrWhiteSpace(snapshot.InstanceName);
        var hasMissingInstanceNameFact = HasUnresolvedFact(
            snapshot,
            "provisioning.instance_name.required",
            "instance_name");
        var missingMinecraftVersion = string.IsNullOrWhiteSpace(snapshot.MinecraftVersion);
        var hasMissingMinecraftVersionFact = HasUnresolvedFact(
            snapshot,
            "provisioning.minecraft_version.required",
            "minecraft_version");
        if (missingInstanceName != hasMissingInstanceNameFact ||
            missingMinecraftVersion != hasMissingMinecraftVersionFact)
        {
            throw new InvalidDataException(
                $"The plan '{snapshot.PlanId:D}' target facts are inconsistent with its unresolved facts.");
        }

        var requiresBlockedState = RequiresBlockedState(
            snapshot.RiskClass,
            snapshot.RequiresConfirmation,
            snapshot.Unresolved);
        var hasValidStatusShape = snapshot.Status switch
        {
            PlanStatus.Ready or PlanStatus.Executing or PlanStatus.Consumed => !requiresBlockedState,
            PlanStatus.Blocked => requiresBlockedState,
            PlanStatus.Expired => true,
            _ => false,
        };
        if (!hasValidStatusShape)
        {
            throw new InvalidDataException(
                $"The plan '{snapshot.PlanId:D}' status is inconsistent with its execution blockers.");
        }
    }

    private static bool RequiresBlockedState(
        PlanRiskClass riskClass,
        bool requiresConfirmation,
        ImmutableArray<ProvisioningUnresolvedFact> unresolved) =>
        riskClass is not PlanRiskClass.Routine ||
        requiresConfirmation ||
        !unresolved.IsDefaultOrEmpty;

    private static bool HasUnresolvedFact(
        ProvisioningPlanSnapshot snapshot,
        string code,
        string field) =>
        snapshot.Unresolved.Any(fact =>
            string.Equals(fact.Code, code, StringComparison.Ordinal) &&
            string.Equals(fact.Field, field, StringComparison.Ordinal));

    private sealed class PlanRecord(ProvisioningPlanSnapshot snapshot)
    {
        public object Gate { get; } = new();
        public ProvisioningPlanSnapshot Snapshot { get; set; } = snapshot;
    }

    private readonly record struct IdempotencyScope(string CreatorPrincipal, string Key);

    private readonly record struct IdempotencyClaim(Guid PlanId, string IntentHash);
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
