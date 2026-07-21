using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

public interface IOperationQueryApplication
{
    Task<Result<OperationListResult, DaemonError>> ListOperationsAsync(
        OperationListQuery request,
        CancellationToken cancellationToken);

    Task<Result<OperationSnapshot, DaemonError>> GetOperationAsync(
        OperationReference request,
        CancellationToken cancellationToken);
}

public interface IOperationControlApplication
{
    Task<Result<OperationCancelResult, DaemonError>> CancelOperationAsync(
        OperationCancelRequest request,
        CancellationToken cancellationToken);
}

public interface IOperationApplication : IOperationQueryApplication, IOperationControlApplication;
