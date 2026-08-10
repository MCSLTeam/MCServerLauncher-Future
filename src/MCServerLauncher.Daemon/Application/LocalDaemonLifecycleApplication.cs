using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalDaemonLifecycleApplication(
    GracefulShutdown shutdown,
    TimeSpan? responseFlushDelay = null) : IDaemonLifecycleApplication
{
    private readonly TimeSpan _responseFlushDelay = responseFlushDelay ?? TimeSpan.FromMilliseconds(250);
    private int _shutdownRequested;

    public Task<Result<DaemonShutdownResult, DaemonError>> RequestShutdownAsync(
        DaemonShutdownRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (request.Reason.Length > 256)
        {
            return Task.FromResult(Result.Err<DaemonShutdownResult, DaemonError>(new ValidationDaemonError(
                "daemon.shutdown.reason_invalid",
                "The daemon shutdown reason must be 256 characters or fewer.")));
        }

        if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) != 0 ||
            shutdown.CancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Result.Ok<DaemonShutdownResult, DaemonError>(new DaemonShutdownResult(
                "already_requested",
                "Daemon shutdown has already been requested.")));
        }

        _ = ScheduleShutdownAsync(request.Reason);
        return Task.FromResult(Result.Ok<DaemonShutdownResult, DaemonError>(new DaemonShutdownResult(
            "shutdown_requested",
            "Daemon shutdown has been requested. Restart it through its service manager or launcher.")));
    }

    private async Task ScheduleShutdownAsync(string reason)
    {
        try
        {
            Log.Information("[DaemonLifecycle] Remote shutdown requested: {Reason}", reason);
            if (_responseFlushDelay > TimeSpan.Zero)
                await Task.Delay(_responseFlushDelay).ConfigureAwait(false);
            await shutdown.Shutdown().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[DaemonLifecycle] Remote shutdown request failed.");
        }
    }
}
