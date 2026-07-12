using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Daemon.API.Application;
using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal sealed class DaemonReportPublisher(
    IDaemonApplication application,
    IDomainEventPort domainEvents,
    ILogger<DaemonReportPublisher> logger) : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is { IsCompleted: false })
                return;

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(_runCancellation.Token);
        }
    }

    internal void RequestStop()
    {
        lock (_gate)
            _runCancellation?.Cancel();
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        lock (_gate)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is not null)
            await runTask.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _runCancellation?.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await StopAsync();
        lock (_gate)
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ReportInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var result = await application.System.GetSystemInfoAsync(cancellationToken);
                if (result.IsErr(out var error))
                {
                    logger.LogDebug(
                        "Failed to refresh daemon report: {ErrorCode}",
                        error?.Code ?? "unknown");
                    continue;
                }

                await domainEvents.PublishAsync(
                    new DaemonReportDomainEvent(
                        result.Unwrap(),
                        Application.StartTime.ToUnixTimeMilliSeconds()),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Daemon report publisher failed");
        }
    }
}
