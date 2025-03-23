namespace MCServerLauncher.Common;

public static class TaskExtensions
{
    public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource();
        var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
        if (completedTask != task) throw new TimeoutException("Task has timed out.");

        timeoutCancellationTokenSource.Cancel();
        return await task; // 传递异常
    }

    public static Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int millisecondsTimeout)
    {
        return task.TimeoutAfter(TimeSpan.FromMilliseconds(millisecondsTimeout));
    }

    public static async Task TimeoutAfter(this Task task, TimeSpan timeout)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource();
        var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
        if (completedTask != task) throw new TimeoutException("Task has timed out.");

        timeoutCancellationTokenSource.Cancel();
        await task; // 传递异常
    }

    public static Task TimeoutAfter(this Task task, int millisecondsTimeout)
    {
        return task.TimeoutAfter(TimeSpan.FromMilliseconds(millisecondsTimeout));
    }

    public static Task<TResult> MapResult<T, TResult>(this Task<T> task, Func<T, TResult> map)
    {
        return task.ContinueWith(t => map(t.Result));
    }
}