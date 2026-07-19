using MCServerLauncher.Daemon.Utils.LazyCell;

namespace MCServerLauncher.ProtocolTests;

public sealed class AsyncTimedLazyCellTests
{
    [Fact]
    public void Constructor_RejectsNegativeCacheDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AsyncTimedLazyCell<object>(() => Task.FromResult(new object()), TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public async Task ConcurrentFirstLoad_UsesOneFactoryTaskAndPublishesOneValue()
    {
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var expected = new CacheValue(1);
        var cell = new AsyncTimedLazyCell<CacheValue>(async () =>
        {
            Interlocked.Increment(ref invocationCount);
            factoryStarted.TrySetResult();
            return await releaseFactory.Task.ConfigureAwait(false);
        }, TimeSpan.FromMinutes(1));

        var reads = Enumerable.Range(0, 64)
            .Select(_ => cell.Value.AsTask())
            .ToArray();

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref invocationCount));

        releaseFactory.SetResult(expected);
        var results = await Task.WhenAll(reads);

        Assert.All(results, result => Assert.Same(expected, result));
        Assert.Equal(1, invocationCount);
        Assert.NotEqual(DateTime.MinValue, cell.LastUpdated);
    }

    [Fact]
    public async Task FreshHit_CompletesSynchronouslyWithoutCallingFactoryAgain()
    {
        var invocationCount = 0;
        var expected = new CacheValue(1);
        var cell = new AsyncTimedLazyCell<CacheValue>(() =>
        {
            Interlocked.Increment(ref invocationCount);
            return Task.FromResult(expected);
        }, TimeSpan.FromMinutes(1));

        Assert.Same(expected, await cell.Value);

        var hit = cell.Value;

        Assert.True(hit.IsCompletedSuccessfully);
        Assert.Same(expected, await hit);
        Assert.Equal(1, invocationCount);
        Assert.False(cell.IsExpired());
    }

    [Fact]
    public async Task ExpiredReaders_ReceiveStaleValueWhileOneRefreshRuns()
    {
        var initial = new CacheValue(1);
        var refreshed = new CacheValue(2);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var cell = new AsyncTimedLazyCell<CacheValue>(() =>
        {
            return Interlocked.Increment(ref invocationCount) switch
            {
                1 => Task.FromResult(initial),
                2 => StartRefreshAsync(),
                _ => Task.FromException<CacheValue>(new InvalidOperationException("Unexpected extra refresh."))
            };

            async Task<CacheValue> StartRefreshAsync()
            {
                refreshStarted.TrySetResult();
                return await releaseRefresh.Task.ConfigureAwait(false);
            }
        }, TimeSpan.FromMilliseconds(10));

        Assert.Same(initial, await cell.Value);
        await WaitUntilAsync(cell.IsExpired);

        var staleReads = Enumerable.Range(0, 64)
            .Select(_ => cell.Value)
            .ToArray();

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var read in staleReads)
        {
            Assert.True(read.IsCompletedSuccessfully);
            Assert.Same(initial, await read);
        }
        Assert.Equal(2, invocationCount);

        var forcedUpdate = cell.Update();
        releaseRefresh.SetResult(refreshed);
        await forcedUpdate;

        var hit = cell.Value;
        Assert.True(hit.IsCompletedSuccessfully);
        Assert.Same(refreshed, await hit);
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task FailedRefresh_KeepsStaleValueAndAllowsLaterRecovery()
    {
        var initial = new CacheValue(1);
        var recovered = new CacheValue(2);
        var failedRefreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedRefresh = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var expectedFailure = new InvalidOperationException("Refresh failed.");
        var cell = new AsyncTimedLazyCell<CacheValue>(() =>
        {
            return Interlocked.Increment(ref invocationCount) switch
            {
                1 => Task.FromResult(initial),
                2 => StartFailedRefreshAsync(),
                3 => StartRecoveryAsync(),
                _ => Task.FromException<CacheValue>(new InvalidOperationException("Unexpected extra refresh."))
            };

            async Task<CacheValue> StartFailedRefreshAsync()
            {
                failedRefreshStarted.TrySetResult();
                return await failedRefresh.Task.ConfigureAwait(false);
            }

            async Task<CacheValue> StartRecoveryAsync()
            {
                recoveryStarted.TrySetResult();
                return await recovery.Task.ConfigureAwait(false);
            }
        }, TimeSpan.FromMilliseconds(10));

        Assert.Same(initial, await cell.Value);
        await WaitUntilAsync(cell.IsExpired);

        var staleRead = cell.Value;
        await failedRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(staleRead.IsCompletedSuccessfully);
        Assert.Same(initial, await staleRead);

        var observingUpdate = cell.Update();
        failedRefresh.SetException(expectedFailure);
        var actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => observingUpdate);
        Assert.Same(expectedFailure, actualFailure);

        var staleAfterFailure = cell.Value;
        await recoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(staleAfterFailure.IsCompletedSuccessfully);
        Assert.Same(initial, await staleAfterFailure);

        var recoveryUpdate = cell.Update();
        recovery.SetResult(recovered);
        await recoveryUpdate;

        var recoveredHit = cell.Value;
        Assert.True(recoveredHit.IsCompletedSuccessfully);
        Assert.Same(recovered, await recoveredHit);
        Assert.Equal(3, invocationCount);
    }

    [Fact]
    public async Task ConcurrentValueTypePublication_DoesNotExposeTornValues()
    {
        var sequence = 0L;
        var stopReaders = 0;
        var cell = new AsyncTimedLazyCell<WideValue>(() =>
        {
            var value = Interlocked.Increment(ref sequence);
            return Task.FromResult(new WideValue(value, value, value, value));
        }, TimeSpan.FromDays(1));

        _ = await cell.Value;
        var readers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () =>
            {
                while (Volatile.Read(ref stopReaders) == 0)
                {
                    var read = cell.Value;
                    if (!read.IsCompletedSuccessfully)
                    {
                        throw new InvalidOperationException("A fresh cache read unexpectedly became asynchronous.");
                    }

                    var value = await read;
                    if (value.A != value.B || value.A != value.C || value.A != value.D)
                    {
                        throw new InvalidOperationException("Observed a torn timed-cache value publication.");
                    }
                }
            }))
            .ToArray();

        try
        {
            for (var index = 0; index < 5_000; index++)
            {
                await cell.Update();
                if ((index & 63) == 0)
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            Volatile.Write(ref stopReaders, 1);
        }

        await Task.WhenAll(readers);
    }

    [Fact]
    public async Task ConcurrentUpdates_ShareOneRefreshTaskPerFailedGeneration()
    {
        var firstRefresh = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRefresh = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var activeFactories = 0;
        var maximumActiveFactories = 0;
        var cell = new AsyncTimedLazyCell<CacheValue>(() =>
        {
            var generation = Interlocked.Increment(ref invocationCount);
            var completion = generation switch
            {
                1 => firstRefresh.Task,
                2 => secondRefresh.Task,
                _ => Task.FromException<CacheValue>(new InvalidOperationException("Unexpected refresh generation."))
            };
            return RunFactoryAsync(completion);
        }, TimeSpan.FromMinutes(1), _ => { });

        var firstWave = Enumerable.Range(0, 1_000).Select(_ => cell.Update()).ToArray();
        Assert.All(firstWave, task => Assert.Same(firstWave[0], task));
        Assert.Equal(1, Volatile.Read(ref invocationCount));
        Assert.Equal(1, Volatile.Read(ref maximumActiveFactories));

        var firstFailure = new InvalidOperationException("First refresh failed.");
        firstRefresh.SetException(firstFailure);
        var firstObserved = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(firstWave));
        Assert.Same(firstFailure, firstObserved);

        var secondWave = Enumerable.Range(0, 1_000).Select(_ => cell.Update()).ToArray();
        Assert.NotSame(firstWave[0], secondWave[0]);
        Assert.All(secondWave, task => Assert.Same(secondWave[0], task));
        Assert.Equal(2, Volatile.Read(ref invocationCount));
        Assert.Equal(1, Volatile.Read(ref maximumActiveFactories));

        var secondFailure = new InvalidOperationException("Second refresh failed.");
        secondRefresh.SetException(secondFailure);
        var secondObserved = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(secondWave));
        Assert.Same(secondFailure, secondObserved);
        Assert.Equal(0, Volatile.Read(ref activeFactories));

        async Task<CacheValue> RunFactoryAsync(Task<CacheValue> completion)
        {
            var active = Interlocked.Increment(ref activeFactories);
            UpdateMaximum(ref maximumActiveFactories, active);
            try
            {
                return await completion.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref activeFactories);
            }
        }
    }

    [Fact]
    public async Task FailedBackgroundRefresh_IsObservedOnceAndKeepsServingStaleValue()
    {
        var initial = new CacheValue(1);
        var refresh = new TaskCompletionSource<CacheValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var observationCount = 0;
        var cell = new AsyncTimedLazyCell<CacheValue>(() =>
        {
            if (Interlocked.Increment(ref invocationCount) == 1)
            {
                return Task.FromResult(initial);
            }

            refreshStarted.TrySetResult();
            return refresh.Task;
        }, TimeSpan.FromMilliseconds(10), exception =>
        {
            Interlocked.Increment(ref observationCount);
            observedFailure.TrySetResult(exception);
        });

        Assert.Same(initial, await cell.Value);
        await WaitUntilAsync(cell.IsExpired);

        var staleReads = Enumerable.Range(0, 1_000).Select(_ => cell.Value).ToArray();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(staleReads, read => Assert.True(read.IsCompletedSuccessfully));
        foreach (var read in staleReads)
        {
            Assert.Same(initial, await read);
        }

        var expectedFailure = new InvalidOperationException("Background refresh failed.");
        refresh.SetException(expectedFailure);

        Assert.Same(expectedFailure, await observedFailure.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref observationCount));
        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static void UpdateMaximum(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private sealed record CacheValue(int Version);

    private readonly record struct WideValue(long A, long B, long C, long D);
}
