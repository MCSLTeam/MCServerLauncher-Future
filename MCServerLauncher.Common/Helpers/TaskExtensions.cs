namespace MCServerLauncher.Common.Helpers;

public static class TaskExtensions
{
    public static Task<TResult> MapResult<T, TResult>(this Task<T> task, Func<T, TResult> map)
    {
        return task.ContinueWith(t => map(t.Result));
    }
}