namespace MCServerLauncher.Common.Concurrent;

public class RwLock
{
    private readonly SemaphoreSlim _readerLock = new(1, 1);
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private volatile int _readCount;

    public async Task<IDisposable> AcquireReaderLockAsync(CancellationToken cancellationToken = default)
    {
        // 通过turnstile实现公平排队
        await _turnstile.WaitAsync(cancellationToken);
        _turnstile.Release();

        await _readerLock.WaitAsync(cancellationToken);
        try
        {
            _readCount++;
            if (_readCount == 1)
                // 第一个读者需要获取写锁来阻止写操作
                await _writerLock.WaitAsync(cancellationToken);
        }
        finally
        {
            _readerLock.Release();
        }

        return new ReaderLockReleaser(this);
    }

    public async Task<IDisposable> AcquireWriterLockAsync(CancellationToken cancellationToken = default)
    {
        // 通过turnstile实现公平排队
        await _turnstile.WaitAsync(cancellationToken);
        try
        {
            // 获取写锁
            await _writerLock.WaitAsync(cancellationToken);
        }
        finally
        {
            _turnstile.Release();
        }

        return new WriterLockReleaser(this);
    }

    private async Task ReleaseReaderLockAsync()
    {
        await _readerLock.WaitAsync();
        try
        {
            _readCount--;
            if (_readCount == 0)
                // 最后一个读者释放写锁
                _writerLock.Release();
        }
        finally
        {
            _readerLock.Release();
        }
    }

    private void ReleaseWriterLock()
    {
        _writerLock.Release();
    }

    public IDisposable AcquireReaderLock(CancellationToken cancellationToken = default)
    {
        // 通过turnstile实现公平排队
        _turnstile.Wait(cancellationToken);
        _turnstile.Release();

        _readerLock.Wait(cancellationToken);
        try
        {
            _readCount++;
            if (_readCount == 1)
                // 第一个读者需要获取写锁来阻止写操作
                _writerLock.Wait(cancellationToken);
        }
        finally
        {
            _readerLock.Release();
        }

        return new ReaderLockReleaser(this);
    }

    public IDisposable AcquireWriterLock(CancellationToken cancellationToken = default)
    {
        // 通过turnstile实现公平排队
        _turnstile.Wait(cancellationToken);
        try
        {
            // 获取写锁
            _writerLock.Wait(cancellationToken);
        }
        finally
        {
            _turnstile.Release();
        }

        return new WriterLockReleaser(this);
    }

    private void ReleaseReaderLock()
    {
        _readerLock.Wait();
        try
        {
            _readCount--;
            if (_readCount == 0)
                // 最后一个读者释放写锁
                _writerLock.Release();
        }
        finally
        {
            _readerLock.Release();
        }
    }

    private class ReaderLockReleaser : IDisposable
    {
        private readonly RwLock _lock;
        private bool _disposed;

        public ReaderLockReleaser(RwLock @lock)
        {
            _lock = @lock;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Task.Run(async () => await _lock.ReleaseReaderLockAsync()).Wait();
            }
        }
    }

    private class WriterLockReleaser : IDisposable
    {
        private readonly RwLock _lock;
        private bool _disposed;

        public WriterLockReleaser(RwLock @lock)
        {
            _lock = @lock;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _lock.ReleaseWriterLock();
            }
        }
    }
}