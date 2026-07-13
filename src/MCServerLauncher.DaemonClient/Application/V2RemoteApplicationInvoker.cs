using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class V2RemoteApplicationInvoker(V2ClientConnectionOwner owner) : IRemoteApplicationInvoker
{
    private readonly V2ClientConnectionOwner _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        return _owner.TryGetReadyCore(out var core)
            ? core.InvokeAsync(descriptor, request, cancellationToken)
            : Task.FromResult(Result.Err<TResult, DaemonError>(NotReadyError()));
    }

    public Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
        RpcDescriptor<TRequest, UnitResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        return _owner.TryGetReadyCore(out var core)
            ? core.InvokeUnitAsync(descriptor, request, cancellationToken)
            : Task.FromResult(Result.Err<Unit, DaemonError>(NotReadyError()));
    }

    private static TransportDaemonError NotReadyError() =>
        new("client.not_ready", "The daemon client is not connected and ready.");
}
