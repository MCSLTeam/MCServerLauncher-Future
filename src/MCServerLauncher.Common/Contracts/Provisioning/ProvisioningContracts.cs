using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Provisioning;

public enum PlanStatus
{
    Ready,
    Blocked,
    Executing,
    Consumed,
    Expired,
}

public enum PlanRiskClass
{
    Routine,
    Sensitive,
    Destructive,
}

public enum ProvisioningProviderKind
{
    Vanilla,
    Paper,
    Fabric,
    Forge,
    NeoForge,
    Quilt,
}

public sealed record ProvisioningUnresolvedFact(
    string Code,
    string Message,
    string? Field = null);

public sealed record ProvisioningPlanSnapshot(
    Guid PlanId,
    string PlanHash,
    string Kind,
    PlanStatus Status,
    PlanRiskClass RiskClass,
    ImmutableArray<string> RequiredPermissions,
    bool RequiresConfirmation,
    string CreatorPrincipal,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    ImmutableArray<ProvisioningUnresolvedFact> Unresolved,
    ProvisioningProviderKind Provider,
    string InstanceName,
    string MinecraftVersion,
    string? Source,
    InstanceFactoryMirror Mirror,
    string? JavaPath,
    string? IdempotencyKey,
    JsonElement Payload);

public sealed record ProvisioningResolveRequest(
    ProvisioningProviderKind Provider,
    string InstanceName,
    string MinecraftVersion,
    string? Source,
    InstanceFactoryMirror Mirror,
    string? JavaPath,
    string CreatorPrincipal,
    string? IdempotencyKey = null,
    TimeSpan? Expiry = null);

public sealed record ProvisioningPlanReference(Guid PlanId, string? OwnerPrincipal = null);

public sealed record ProvisioningExecuteRequest(Guid PlanId, string ExecutorPrincipal);

public sealed record ProvisioningExecuteResult(Guid PlanId, Guid OperationId, Guid? InstanceId);
