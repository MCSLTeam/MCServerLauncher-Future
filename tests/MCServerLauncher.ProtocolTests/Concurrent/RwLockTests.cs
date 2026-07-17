using MCServerLauncher.Common.Concurrent;

namespace MCServerLauncher.ProtocolTests.Concurrent;

public sealed class RwLockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CanceledFirstAsyncReaderDoesNotPermitReaderWriterOverlap()
    {
        var rwLock = new RwLock();
        var writer = await rwLock.AcquireWriterLockAsync();

        try
        {
            using var cancellation = new CancellationTokenSource();
            var canceledReader = rwLock.AcquireReaderLockAsync(cancellation.Token);
            Assert.False(canceledReader.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledReader);

            var laterReader = rwLock.AcquireReaderLockAsync();
            Assert.False(laterReader.IsCompleted);

            writer.Dispose();
            using var reader = await laterReader.WaitAsync(Timeout);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentAsyncReadersReleaseWriterGateBeforeAllowingLateReaders()
    {
        const int readerCount = 64;
        var rwLock = new RwLock();
        var allReadersAcquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReaders = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquiredReaders = 0;

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => HoldReaderAsync(rwLock, readerCount, allReadersAcquired, releaseReaders, () => Interlocked.Increment(ref acquiredReaders)))
            .ToArray();

        try
        {
            await allReadersAcquired.Task.WaitAsync(Timeout);

            var writerTask = rwLock.AcquireWriterLockAsync();
            Assert.False(writerTask.IsCompleted);

            releaseReaders.TrySetResult(true);
            await Task.WhenAll(readers).WaitAsync(Timeout);

            var writer = await writerTask.WaitAsync(Timeout);
            try
            {
                var lateReader = rwLock.AcquireReaderLockAsync();
                Assert.False(lateReader.IsCompleted);

                writer.Dispose();
                using var reader = await lateReader.WaitAsync(Timeout);
            }
            finally
            {
                writer.Dispose();
            }
        }
        finally
        {
            releaseReaders.TrySetResult(true);
        }
    }

    private static async Task HoldReaderAsync(
        RwLock rwLock,
        int readerCount,
        TaskCompletionSource<bool> allReadersAcquired,
        TaskCompletionSource<bool> releaseReaders,
        Func<int> incrementAcquiredReaders)
    {
        using var reader = await rwLock.AcquireReaderLockAsync();
        if (incrementAcquiredReaders() == readerCount)
            allReadersAcquired.TrySetResult(true);

        await releaseReaders.Task;
    }
}
