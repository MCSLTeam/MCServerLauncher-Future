using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MCServerLauncher.DaemonClient;

public static class TaskExtensions
{
    public static async Task<TResult> WaitAsync<TResult>(
        this Task<TResult> task,
        TimeSpan timeout,
        CancellationToken ct = default
    )
    {
        using var timeoutCts = new CancellationTokenSource();

        var token = timeoutCts.Token;

        if (ct != CancellationToken.None)
            token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token).Token;

        var timeoutTask = Task.Delay(timeout, token);

        var selectTask = await Task.WhenAny(task, timeoutTask);
        if (selectTask != task)
        {
            if (token.IsCancellationRequested) await timeoutTask; // 抛出OperationCanceledException
            else throw new TimeoutException("Task has timed out.");
        }

        timeoutCts.Cancel();
        return await task; // 传递异常
    }

    public static Task<TResult> WaitAsync<TResult>(
        this Task<TResult> task,
        int millisecondsTimeout,
        CancellationToken ct = default
    )
    {
        return task.WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout), ct);
    }

    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource();

        var token = timeoutCts.Token;
        if (ct != CancellationToken.None)
            token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token).Token;

        var timeoutTask = Task.Delay(timeout, token);
        var selectTask = await Task.WhenAny(task, timeoutTask);
        if (selectTask != task)
        {
            if (token.IsCancellationRequested) await timeoutTask; // 抛出OperationCanceledException
            else throw new TimeoutException("Task has timed out.");
        }

        timeoutCts.Cancel();
        await task; // 传递异常
    }

    public static Task WaitAsync(this Task task, int millisecondsTimeout, CancellationToken ct = default)
    {
        return task.WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout), ct);
    }
}