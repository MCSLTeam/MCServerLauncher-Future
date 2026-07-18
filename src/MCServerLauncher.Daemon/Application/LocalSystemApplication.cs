using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalSystemApplication(
    IAsyncTimedLazyCell<SystemInfo> systemInfoCell,
    IAsyncTimedLazyCell<JavaRuntime[]> javaRuntimeCell) : ISystemApplication
{
    private JavaRuntime[]? _lastJavaArray;
    private JavaRuntimeList? _lastJavaList;

    public async Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var systemInfo = await systemInfoCell.Value.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Ok<SystemInfo, DaemonError>(systemInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<SystemInfo, DaemonError>(
                new InternalDaemonError("system.info_unavailable", "System information is unavailable."));
        }
    }

    public async Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var runtimes = await javaRuntimeCell.Value.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(runtimes, _lastJavaArray) || _lastJavaList is null)
            {
                _lastJavaArray = runtimes;
                _lastJavaList = new JavaRuntimeList(runtimes.ToImmutableArray());
            }

            return Result.Ok<JavaRuntimeList, DaemonError>(_lastJavaList);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<JavaRuntimeList, DaemonError>(
                new InternalDaemonError("system.java_unavailable", "Java runtime information is unavailable."));
        }
    }
}
