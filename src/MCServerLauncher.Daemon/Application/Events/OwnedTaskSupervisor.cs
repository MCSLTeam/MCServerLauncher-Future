using Microsoft.Extensions.Logging;

namespace MCServerLauncher.Daemon.ApplicationCore.Events;

internal sealed class OwnedTaskSupervisor(
    string owner,
    ILogger logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<Task> _tasks = [];
    private TaskCompletionSource? _stopCompletion;
    private Task? _stopDriverTask;
    private Exception? _cancelFailure;
    private int _accepting = 1;
    private bool _disposed;

    internal int PendingTaskCount
    {
        get
        {
            lock (_gate)
            {
                RemoveCompletedTasks();
                return _tasks.Count;
            }
        }
    }

    internal void Schedule(
        string operation,
        Func<CancellationToken, Task> work,
        CancellationToken callerCancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(work);
        callerCancellation.ThrowIfCancellationRequested();

        Task task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Volatile.Read(ref _accepting) == 0)
                return;

            RemoveCompletedTasks();
            task = ExecuteAsync(operation, work, callerCancellation);
            if (!task.IsCompleted)
                _tasks.Add(task);
        }
    }

    internal void RequestStop()
    {
        _ = EnsureStopTask();
    }

    internal async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        List<Exception>? failures = null;
        try
        {
            await EnsureStopTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            var stopDriver = Volatile.Read(ref _stopDriverTask);
            if (stopDriver is not null)
                await stopDriver.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        var cancelFailure = Volatile.Read(ref _cancelFailure);
        if (cancelFailure is not null)
            (failures ??= []).Add(cancelFailure);

        while (true)
        {
            Task[] pending;
            lock (_gate)
            {
                RemoveCompletedTasks();
                if (_tasks.Count == 0)
                    break;
                pending = [.. _tasks];
            }

            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (failures is not null)
            throw new AggregateException($"Stopping owned tasks for '{owner}' failed.", failures);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try
        {
            await DrainAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }

    private Task EnsureStopTask()
    {
        Interlocked.Exchange(ref _accepting, 0);
        var completion = Volatile.Read(ref _stopCompletion);
        if (completion is not null)
            return completion.Task;

        var candidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = Interlocked.CompareExchange(ref _stopCompletion, candidate, null) ?? candidate;
        if (ReferenceEquals(completion, candidate))
            Volatile.Write(ref _stopDriverTask, DriveStopAsync(candidate));
        return completion.Task;
    }

    private async Task DriveStopAsync(TaskCompletionSource completion)
    {
        // Yield so Schedule()'d work that already signalled "started" can finish installing
        // CancellationToken registrations before Cancel runs under a loaded thread pool.
        await Task.Yield();

        try
        {
            // Cancel() aggregates registration callback exceptions synchronously. Keep that path
            // as the primary observation mechanism for Dispose/Drain.
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (Exception syncException)
            {
                ObserveCancelFailure(syncException);
                completion.TrySetException(syncException);
                return;
            }

            // Residual path: if Cancel() returned without throwing, still surface any deferred
            // CancelAsync observation (should already be canceled and complete immediately).
            try
            {
                await _lifetimeCancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception asyncException)
            {
                ObserveCancelFailure(asyncException);
                completion.TrySetException(asyncException);
                return;
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ObserveCancelFailure(exception);
            completion.TrySetException(exception);
        }
    }

    private void ObserveCancelFailure(Exception exception)
    {
        // Keep the first failure; concurrent stop drivers should not overwrite it.
        Interlocked.CompareExchange(ref _cancelFailure, exception, null);
        logger.LogError(exception, "Canceling owned tasks for '{Owner}' failed", owner);
    }

    private async Task ExecuteAsync(
        string operation,
        Func<CancellationToken, Task> work,
        CancellationToken callerCancellation)
    {
        // When the caller does not supply a token, pass the lifetime token directly so
        // CancellationToken.Register callbacks are observed by Cancel() without relying on
        // linked-source parent/child exception propagation quirks under load.
        if (!callerCancellation.CanBeCanceled)
        {
            try
            {
                await work(_lifetimeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Owned task '{Operation}' for '{Owner}' failed",
                    operation,
                    owner);
            }

            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            callerCancellation);
        try
        {
            await work(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Owned task '{Operation}' for '{Owner}' failed",
                operation,
                owner);
        }
    }

    private void RemoveCompletedTasks()
    {
        _tasks.RemoveWhere(static task => task.IsCompleted);
    }
}
