using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace MCServerLauncher.DaemonClient;

public static class TaskExtensions
{
    public static Task<TResult?> Suppress<TResult>(this Task<TResult> task)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    return await task;
                }
                catch (Exception e)
                {
                    Log.Information(e, "Suppress Exception");
                    return default;
                }
            });
    }

    public static Task Suppress(this Task task)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    await task;
                }
                catch (Exception e)
                {
                    Log.Information(e, "Suppress Exception");
                }
            });
    }

    public static Task<TResult?> Suppress<TResult>(this Task<TResult> task, params Type[] exceptionsTypes)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    return await task;
                }
                catch (Exception e)
                {
                    if (!exceptionsTypes.Any(t => e.GetType().IsSubclassOf(t)))
                        throw;
                    Log.Information(e, "Suppress Exception");
                    return default;
                }
            });
    }

    public static Task Suppress(this Task task, params Type[] exceptionsTypes)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    await task;
                }
                catch (Exception e)
                {
                    if (!exceptionsTypes.Any(t => e.GetType().IsSubclassOf(t)))
                        throw;
                    Log.Information(e, "Suppress Exception");
                }
            });
    }
}