namespace MCServerLauncher.Daemon.ApplicationCore.Audit;

/// <summary>
/// One observed authorized mutation, described by typed metadata only. Request payloads, command
/// text, console content, and token material are never part of an audit event by construction.
/// </summary>
internal sealed record AuditEvent(
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

/// <summary>
/// Fail-open audit recording: implementations must never throw into the mutation path they observe.
/// </summary>
internal interface IAuditSink
{
    void Record(AuditEvent auditEvent);
}
