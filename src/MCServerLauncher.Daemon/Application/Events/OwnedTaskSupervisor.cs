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
            await EnsureStopTask().WaitAsync(cancellationToken);
            var stopDriver = Volatile.Read(ref _stopDriverTask);
            if (stopDriver is not null)
                await stopDriver.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

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

            await Task.WhenAll(pending).WaitAsync(cancellationToken);
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
            await DrainAsync();
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
            Volatile.Write(ref _stopDriverTask, CompleteStopAsync(candidate));
        return completion.Task;
    }

    private Task CompleteStopAsync(TaskCompletionSource completion)
    {
        try
        {
            // Prefer Cancel() over CancelAsync(): callback exceptions are aggregated into
            // AggregateException and thrown synchronously, which DrainAsync/DisposeAsync must observe.
            // Linked-token registrations (owned work) cancel through parent callbacks and are included.
            _lifetimeCancellation.Cancel();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Canceling owned tasks for '{Owner}' failed", owner);
            completion.TrySetException(exception);
        }

        return Task.CompletedTask;
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
