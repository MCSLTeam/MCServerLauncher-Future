using MCServerLauncher.Daemon;
using System.Reflection;

namespace MCServerLauncher.ProtocolTests;

public class GracefulShutdownTests
{
    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task ApplicationLifecycleCallbacks_AreAwaitedSerially()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> callbacks = async () =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
        };
        callbacks += () =>
        {
            secondEntered.TrySetResult();
            return Task.CompletedTask;
        };
        var invokeAsync = typeof(Application).GetMethod(
            "InvokeAsync",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(Application).FullName, "InvokeAsync");

        var invocation = Assert.IsAssignableFrom<Task>(invokeAsync.Invoke(null, [callbacks]));
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.TrySetResult();
        await invocation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(secondEntered.Task.IsCompleted);
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task WaitForShutdownAsync_CompletesAfterBlockedShutdownCallbacksFinish()
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

        var waitForShutdownTask = gracefulShutdown.WaitForShutdownAsync();
        Assert.False(waitForShutdownTask.IsCompleted);

        callbackCanFinish.SetResult();
        await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2));
        await waitForShutdownTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task WaitForShutdownAsync_PropagatesShutdownCallbackFailure()
    {
        using var gracefulShutdown = new GracefulShutdown();
        gracefulShutdown.OnShutdown += () => Task.FromException(
            new InvalidOperationException("shutdown callback failed"));

        await Assert.ThrowsAsync<AggregateException>(() => gracefulShutdown.Shutdown());
        await Assert.ThrowsAsync<AggregateException>(() =>
            gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task Shutdown_CancellationCallbackFailureStillSignalsAndRunsShutdownCallbacks()
    {
        using var gracefulShutdown = new GracefulShutdown();
        using var registration = gracefulShutdown.CancellationToken.Register(
            () => throw new InvalidOperationException("cancel callback failed"));
        var callbackRan = false;
        gracefulShutdown.OnShutdown += () =>
        {
            callbackRan = true;
            return Task.CompletedTask;
        };

        var failure = await Assert.ThrowsAsync<AggregateException>(() => gracefulShutdown.Shutdown());

        Assert.Contains(failure.InnerExceptions, exception =>
            exception is AggregateException aggregate &&
            aggregate.InnerExceptions.Any(inner => inner.Message == "cancel callback failed"));
        Assert.True(callbackRan);
        await Assert.ThrowsAsync<AggregateException>(() =>
            gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task Shutdown_FirstCallbackFailureStillRunsLaterCallback()
    {
        using var gracefulShutdown = new GracefulShutdown();
        var laterCallbackRan = false;
        gracefulShutdown.OnShutdown += () => Task.FromException(new InvalidOperationException("first failed"));
        gracefulShutdown.OnShutdown += () =>
        {
            laterCallbackRan = true;
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<AggregateException>(() => gracefulShutdown.Shutdown());

        Assert.True(laterCallbackRan);
        await Assert.ThrowsAsync<AggregateException>(() =>
            gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task WaitForShutdownAsync_WaitsForEveryMulticastCallback()
    {
        using var gracefulShutdown = new GracefulShutdown();
        var firstCallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstCallbackToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastCallbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gracefulShutdown.OnShutdown += async () =>
        {
            firstCallbackStarted.TrySetResult();
            await allowFirstCallbackToFinish.Task;
        };
        gracefulShutdown.OnShutdown += () =>
        {
            lastCallbackCompleted.TrySetResult();
            return Task.CompletedTask;
        };

        var shutdownTask = gracefulShutdown.Shutdown();
        await firstCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Yield();

        Assert.False(lastCallbackCompleted.Task.IsCompleted);
        Assert.False(gracefulShutdown.WaitForShutdownAsync().IsCompleted);

        allowFirstCallbackToFinish.TrySetResult();
        await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2));
        await lastCallbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task Shutdown_RepeatedCallsReturnSameTaskAndRunCallbacksOnce()
    {
        using var gracefulShutdown = new GracefulShutdown();
        var callbackCalls = 0;
        gracefulShutdown.OnShutdown += () =>
        {
            Interlocked.Increment(ref callbackCalls);
            return Task.CompletedTask;
        };

        var first = gracefulShutdown.Shutdown();
        var second = gracefulShutdown.Shutdown();

        Assert.Same(first, second);
        await first;
        Assert.Equal(1, callbackCalls);
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task Shutdown_CallbackReentryReturnsStableTaskWithoutRecursion()
    {
        using var gracefulShutdown = new GracefulShutdown();
        Task? reenteredTask = null;
        var callbackCalls = 0;
        gracefulShutdown.OnShutdown += () =>
        {
            Interlocked.Increment(ref callbackCalls);
            reenteredTask = gracefulShutdown.Shutdown();
            return Task.CompletedTask;
        };

        var shutdownTask = gracefulShutdown.Shutdown();
        await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(shutdownTask, reenteredTask);
        Assert.Equal(1, callbackCalls);
    }

    [Fact]
    [Trait("Category", "Daemon")]
    [Trait("Category", "Shutdown")]
    public async Task ConsoleBoundary_ObservesCallbackFaultAndWaitPropagatesStableFailure()
    {
        using var gracefulShutdown = new GracefulShutdown();
        gracefulShutdown.OnShutdown += () =>
            Task.FromException(new InvalidOperationException("console callback failed"));

        gracefulShutdown.StartShutdownFromConsole();
        await gracefulShutdown.ConsoleShutdownObservation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<AggregateException>(() =>
            gracefulShutdown.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<AggregateException>(() => gracefulShutdown.Shutdown());
    }
}
