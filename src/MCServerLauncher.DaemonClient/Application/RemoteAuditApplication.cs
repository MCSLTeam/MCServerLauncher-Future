using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteAuditApplication(IRemoteApplicationInvoker invoker) : IAuditApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<AuditQueryResult, DaemonError>> QueryAsync(
        AuditQuery request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.QueryAudit, request, cancellationToken);
}
