using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Audit;

/// <summary>
/// The daemon audit sink and its bounded query surface. Recording is fail-open: an audit write
/// failure never fails the mutation being recorded, but every drop is counted and surfaced.
/// </summary>
internal sealed class AuditLog : IAuditSink
{
    internal const int MaximumQueryRecords = 500;

    private readonly BoundedJsonlLog<AuditRecord> _log;
    private readonly TimeProvider _timeProvider;

    public AuditLog(
        DaemonAuditConfig config,
        TimeProvider? timeProvider = null,
        string? rootDirectory = null,
        ILogger<AuditLog>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _log = new BoundedJsonlLog<AuditRecord>(
            Path.GetFullPath(rootDirectory ?? Path.Combine(FileManager.Root, "audit")),
            "audit",
            ApplicationContractJsonContext.Default.AuditRecord,
            static record => record.Timestamp,
            config.MaximumBytes,
            TimeSpan.FromDays(config.RetentionDays),
            timeProvider,
            logger);
    }

    internal long DroppedRecords => _log.DroppedRecords;

    public void Record(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _log.Append(new AuditRecord(
            _timeProvider.GetUtcNow(),
            auditEvent.Principal,
            auditEvent.PluginId,
            auditEvent.Method,
            auditEvent.Permission,
            auditEvent.Target,
            auditEvent.PlanId,
            auditEvent.PlanHash,
            auditEvent.OperationId,
            auditEvent.Succeeded,
            auditEvent.ErrorCode,
            auditEvent.ConfirmedBy,
            auditEvent.Detail));
    }

    internal AuditQueryResult Query(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // OwnerPrincipal is the authenticated caller subject. Trusted admin marker "*" (main
        // token / full grant) sees the full history; everyone else sees only their own records.
        var owner = query.OwnerPrincipal;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return new AuditQueryResult(ImmutableArray<AuditRecord>.Empty, _log.DroppedRecords);
        }

        var seesAll = string.Equals(owner, "*", StringComparison.Ordinal);
        var cap = Math.Clamp(query.MaximumRecords ?? 100, 1, MaximumQueryRecords);
        var records = _log.ReadNewestFirst(
            cap,
            query.NotBefore,
            query.NotAfter,
            record =>
                (seesAll || string.Equals(record.Principal, owner, StringComparison.Ordinal)) &&
                (query.Principal is null || string.Equals(record.Principal, query.Principal, StringComparison.Ordinal)) &&
                (query.Method is null || string.Equals(record.Method, query.Method, StringComparison.Ordinal)) &&
                (query.Target is null || string.Equals(record.Target, query.Target, StringComparison.Ordinal)));
        return new AuditQueryResult(records.ToImmutableArray(), _log.DroppedRecords);
    }
}

internal sealed class LocalAuditApplication(AuditLog auditLog) : IAuditApplication
{
    public Task<Result<AuditQueryResult, DaemonError>> QueryAsync(
        AuditQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok<AuditQueryResult, DaemonError>(auditLog.Query(request)));
    }
}
