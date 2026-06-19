using MCServerLauncher.Daemon;

namespace MCServerLauncher.ProtocolTests;

public class GracefulShutdownTests
{
    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task WaitForShutdownAsync_CompletesBeforeBlockedShutdownCallbacksFinish()
    {
        using var gracefulShutdown = new GracefulShutdown();
        var callbackCanFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        gracefulShutdown.OnShutdown += async () =>
        {
            callbackStarted.SetResult();
            await callbackCanFinish.Task;
        };

        var shutdownTask = gracefulShutdown.Shutdown();
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromMilliseconds(200));

        callbackCanFinish.SetResult();
        await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task Shutdown_CalledMoreThanOnce_DoesNotThrow()
    {
        using var gracefulShutdown = new GracefulShutdown();

        await gracefulShutdown.Shutdown();
        await gracefulShutdown.Shutdown();

        await gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromMilliseconds(200));
    }
}
