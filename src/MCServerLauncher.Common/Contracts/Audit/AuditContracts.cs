using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Audit;

/// <summary>
/// One recorded authorized daemon mutation. Records carry typed metadata only — request payloads,
/// console content, tokens, and resolved paths are never serialized, which is what keeps the log
/// redaction-safe by construction.
/// </summary>
public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string Principal,
    string? PluginId,
    string Method,
    string Permission,
    string? Target,
    Guid? PlanId,
    string? PlanHash,
    Guid? OperationId,
    bool Succeeded,
    string? ErrorCode,
    string? ConfirmedBy);

public sealed record AuditQuery(
    int? MaximumRecords = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? NotAfter = null,
    string? Principal = null,
    string? Method = null,
    string? Target = null,
    string? OwnerPrincipal = null);

/// <summary>
/// Bounded query result. <see cref="DroppedRecords" /> exposes the count of history records lost to
/// write failures, so an audit hole is observable instead of silent.
/// </summary>
public sealed record AuditQueryResult(
    ImmutableArray<AuditRecord> Records,
    long DroppedRecords);
