using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal interface IRemoteApplicationInvoker
{
    Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken)
        where TResult : notnull;

    Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
        RpcDescriptor<TRequest, UnitResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken);
}
