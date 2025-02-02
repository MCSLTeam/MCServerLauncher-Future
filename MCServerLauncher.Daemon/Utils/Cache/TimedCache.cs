using MCServerLauncher.Common.Concurrent;

namespace MCServerLauncher.Daemon.Utils.Cache;

public class TimedCache<T> : ITimedCacheable<T>
{
    private readonly Func<T> _valueFactory;

    private T? _value;

    public TimedCache(Func<T> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory;
        CacheDuration = cacheDuration;
    }

    public DateTime LastUpdated { get; private set; }
    public TimeSpan CacheDuration { get; }
    public T Value => GetValue();

    public bool IsExpired()
    {
        return DateTime.Now - LastUpdated > CacheDuration;
    }

    public void Update()
    {
        _value = _valueFactory();
        LastUpdated = DateTime.Now;
    }

    private T GetValue()
    {
        if (IsExpired()) Update();
        return _value;
    }
}

public class AsyncTimedCache<T> : IAsyncTimedCacheable<T>
{
    private readonly AsyncRwLock _lock = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly Func<Task<T>> _valueFactory;
    private volatile Task? _currentUpdateTask;
    private DateTime _lastUpdated;
    private T? _value;

    public AsyncTimedCache(Func<Task<T>> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        CacheDuration = cacheDuration;
    }

    public DateTime LastUpdated
    {
        get
        {
            using var readLock = _lock.AcquireReaderLockAsync().Result;
            return _lastUpdated;
        }
    }

    public TimeSpan CacheDuration { get; }

    public ValueTask<T> Value => GetValue();

    public bool IsExpired()
    {
        using var readLock = _lock.AcquireReaderLockAsync().Result;
        return DateTime.Now - _lastUpdated > CacheDuration;
    }

    public Task Update()
    {
        return GetValue(true).AsTask();
    }

    private async ValueTask<T> GetValue(bool forceUpdate = false)
    {
        // 首先检查是否需要更新
        bool needsUpdate;
        using (await _lock.AcquireReaderLockAsync())
        {
            needsUpdate = forceUpdate || DateTime.Now - _lastUpdated > CacheDuration;
        }

        if (needsUpdate)
        {
            await _updateLock.WaitAsync();
            try
            {
                // 双重检查，避免在等待锁的过程中其他线程已经完成更新
                using (await _lock.AcquireReaderLockAsync())
                {
                    needsUpdate = forceUpdate || DateTime.Now - _lastUpdated > CacheDuration;
                }

                if (needsUpdate)
                {
                    // 如果已经有更新任务在进行，直接等待其完成
                    var existingTask = _currentUpdateTask;
                    if (existingTask != null)
                    {
                        await existingTask;
                        return _value!;
                    }

                    // 创建新的更新任务
                    var updateTask = UpdateValueAsync();
                    _currentUpdateTask = updateTask;
                    try
                    {
                        await updateTask;
                    }
                    finally
                    {
                        _currentUpdateTask = null;
                    }
                }
            }
            finally
            {
                _updateLock.Release();
            }
        }

        // 返回最新值
        using (await _lock.AcquireReaderLockAsync())
        {
            return _value!;
        }
    }

    private async Task UpdateValueAsync()
    {
        var newValue = await _valueFactory();
        using var writeLock = await _lock.AcquireWriterLockAsync();
        _value = newValue;
        _lastUpdated = DateTime.Now;
    }
}

// // LazyAsync 的通用实现，可以单独使用
// public class LazyAsync<T>
// {
//     private readonly SemaphoreSlim _lock = new(1, 1);
//     private readonly Func<Task<T>> _valueFactory;
//     private Task<T> _value;
//
//     public LazyAsync(Func<Task<T>> valueFactory)
//     {
//         _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
//     }
//
//     public async Task<T> GetValue()
//     {
//         if (_value == null)
//         {
//             await _lock.WaitAsync();
//             try
//             {
//                 if (_value == null)
//                 {
//                     _value = _valueFactory();
//                 }
//             }
//             finally
//             {
//                 _lock.Release();
//             }
//         }
//
//         return await _value;
//     }
//
//     public async Task Reset()
//     {
//         await _lock.WaitAsync();
//         try
//         {
//             _value = null;
//         }
//         finally
//         {
//             _lock.Release();
//         }
//     }
// }