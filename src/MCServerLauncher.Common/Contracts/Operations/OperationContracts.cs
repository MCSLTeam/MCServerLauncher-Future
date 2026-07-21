using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Operations;

public enum OperationStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public enum OperationStage
{
    Queued,
    Resolving,
    Downloading,
    Verifying,
    Extracting,
    Installing,
    Configuring,
    Finalizing,
    Succeeded,
    Failed,
    Cancelled,
    Interrupted,
}

public sealed record OperationReference(Guid OperationId, string? OwnerPrincipal = null);

public sealed record OperationListQuery(string? OwnerPrincipal = null);

public sealed record OperationProgress(
    bool Indeterminate,
    double? Completed,
    double? Total,
    string? Unit,
    long? BytesTransferred,
    long? BytesTotal,
    double? Rate);

public sealed record OperationSnapshot(
    Guid OperationId,
    string Kind,
    string? Target,
    string OwnerPrincipal,
    OperationStatus Status,
    OperationStage Stage,
    OperationProgress Progress,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    bool Cancellable,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResultReference);

public sealed record OperationListResult(ImmutableArray<OperationSnapshot> Operations);

public sealed record OperationCancelRequest(Guid OperationId, string? OwnerPrincipal = null);

public sealed record OperationCancelResult(Guid OperationId, bool CancelRequested);
