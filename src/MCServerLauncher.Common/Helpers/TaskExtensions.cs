namespace MCServerLauncher.Common.Helpers;

public static class TaskExtensions
{
    public static async Task<TResult?> Suppress<TResult>(this Task<TResult> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return default;
        }
    }

    public static async Task Suppress(this Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public static async Task<TResult?> Suppress<TResult>(this Task<TResult> task, params Type[] exceptionsTypes)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!exceptionsTypes.Any(t => e.GetType().IsSubclassOf(t)))
                throw;
            return default;
        }
    }

    public static async Task Suppress(this Task task, params Type[] exceptionsTypes)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!exceptionsTypes.Any(t => e.GetType().IsSubclassOf(t)))
                throw;
        }
    }

    public static Task<TResult> MapTask<T, TResult>(this Task<T> task, Func<T, TResult> map)
    {
        return task.ContinueWith(t => map(t.Result));
    }
}
