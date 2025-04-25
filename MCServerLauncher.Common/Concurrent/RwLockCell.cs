namespace MCServerLauncher.Common.Concurrent;

public class RwLockCell<T>
{
    private readonly RwLock _lock = new();
    private T? _value;

    public RwLockCell()
    {
        _value = default;
    }

    public RwLockCell(T value)
    {
        _value = value;
    }

    public ReadGuard GetRead()
    {
        var releaser = _lock.AcquireReaderLock();
        return new ReadGuard(this, releaser);
    }

    public WriteGuard GetWrite()
    {
        var releaser = _lock.AcquireWriterLock();
        return new WriteGuard(this, releaser);
    }

    public async Task<ReadGuard> GetReadAsync()
    {
        var releaser = await _lock.AcquireReaderLockAsync();
        return new ReadGuard(this, releaser);
    }

    public async Task<WriteGuard> GetWriteAsync()
    {
        var releaser = await _lock.AcquireWriterLockAsync();
        return new WriteGuard(this, releaser);
    }

    public class ReadGuard : IDisposable
    {
        private bool _disposed;
        private readonly RwLockCell<T> _cell;
        private readonly IDisposable _releaser;

        public T? Value => _cell._value;

        internal ReadGuard(RwLockCell<T> cell, IDisposable releaser)
        {
            _cell = cell;
            _releaser = releaser;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _releaser.Dispose();
            }
        }
    }

    public class WriteGuard : IDisposable
    {
        private bool _disposed;
        private readonly RwLockCell<T> _cell;
        private readonly IDisposable _releaser;

        public T? Value
        {
            get => _cell._value;
            set => _cell._value = value;
        }

        internal WriteGuard(RwLockCell<T> cell, IDisposable releaser)
        {
            _cell = cell;
            _releaser = releaser;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _releaser.Dispose();
            }
        }
    }
}