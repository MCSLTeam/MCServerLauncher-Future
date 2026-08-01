using System.Collections.Immutable;

namespace MCServerLauncher.Common.Contracts.Audit;

/// <summary>
/// One recorded authorized daemon mutation. Records carry typed metadata only — request payloads,
/// console content, tokens, and resolved paths are never serialized, which is what keeps the log
/// redaction-safe by construction.
/// </summary>
/// <param name="ErrorCode">
/// A short structured outcome code from the daemon's own closed vocabulary, and null whenever
/// <paramref name="Succeeded" /> is true.
/// </param>
/// <param name="Detail">
/// The one free-text field, and the only one an operator authors: the annotation an automation
/// audit.record action asked to leave. Nothing else populates it, and it is never derived from a
/// request payload — the policy validator bounds it to 1024 characters before it can be stored.
/// </param>
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
    string? ConfirmedBy,
    string? Detail = null);

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
