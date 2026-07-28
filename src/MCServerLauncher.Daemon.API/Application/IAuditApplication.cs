using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Bounded query over the daemon audit history. Records carry typed metadata only; the caller sees
/// records of its own mutations unless it is the global owner, which sees the full history.
/// </summary>
public interface IAuditApplication
{
    Task<Result<AuditQueryResult, DaemonError>> QueryAsync(
        AuditQuery request,
        CancellationToken cancellationToken);
}
