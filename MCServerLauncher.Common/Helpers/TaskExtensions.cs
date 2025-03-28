namespace MCServerLauncher.Common.Helpers;

public static class TaskExtensions
{
    public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource();

        var token = timeoutCancellationTokenSource.Token;
        if (ct != CancellationToken.None)
            token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCancellationTokenSource.Token).Token;

        var completedTask =
            await Task.WhenAny(task, Task.Delay(timeout, token));
        if (completedTask != task)
        {
            if (ct == CancellationToken.None) throw new TimeoutException("Task has timed out.");
            await completedTask; // 抛出Task.Delay中的异常
        }

        timeoutCancellationTokenSource.Cancel();
        return await task; // 传递异常
    }

    public static Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout,
        CancellationToken ct = default)
    {
        return task.TimeoutAfter(TimeSpan.FromMilliseconds(millisecondsTimeout), ct);
    }

    public static async Task TimeoutAfter(this Task task, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource();

        var token = timeoutCancellationTokenSource.Token;
        if (ct != CancellationToken.None)
            token = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCancellationTokenSource.Token).Token;

        var completedTask =
            await Task.WhenAny(task, Task.Delay(timeout, token));
        if (completedTask != task)
        {
            if (ct == CancellationToken.None) throw new TimeoutException("Task has timed out.");
            await completedTask; // 抛出Task.Delay中的异常
        }

        timeoutCancellationTokenSource.Cancel();
        await task; // 传递异常
    }

    public static Task TimeoutAfter(this Task task, int millisecondsTimeout, CancellationToken ct = default)
    {
        return task.TimeoutAfter(TimeSpan.FromMilliseconds(millisecondsTimeout), ct);
    }

    public static Task<TResult> MapResult<T, TResult>(this Task<T> task, Func<T, TResult> map)
    {
        return task.ContinueWith(t => map(t.Result));
    }
}