namespace MCServerLauncher.Daemon.Utils.LazyCell;

/// <summary>
/// Timed async cache with a lock-free hit path and single-flight refresh.
/// When a cached value exists and the TTL expires, readers receive the stale value immediately
/// while one background refresh updates the cell (stale-while-revalidate). Force update and the
/// first load still await the factory.
/// </summary>
public class AsyncTimedLazyCell<T> : IAsyncTimedLazyCell<T>
{
    private readonly Func<Task<T>> _valueFactory;
    private readonly object _gate = new();
    private readonly TimeSpan _cacheDuration;
    private T? _value;
    private long _lastUpdatedUtcTicks; // 0 = never populated
    private Task? _refreshTask;
    private Exception? _lastFailure;

    public AsyncTimedLazyCell(Func<Task<T>> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        if (cacheDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheDuration));
        }

        _cacheDuration = cacheDuration;
    }

    public DateTime LastUpdated
    {
        get
        {
            var ticks = Volatile.Read(ref _lastUpdatedUtcTicks);
            return ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        }
    }

    public TimeSpan CacheDuration => _cacheDuration;

    public ValueTask<T> Value => GetValueAsync(forceUpdate: false);

    public bool IsExpired()
    {
        var ticks = Volatile.Read(ref _lastUpdatedUtcTicks);
        if (ticks == 0)
        {
            return true;
        }

        return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > _cacheDuration;
    }

    public Task Update() => GetValueAsync(forceUpdate: true).AsTask();

    private ValueTask<T> GetValueAsync(bool forceUpdate)
    {
        var nowUtc = DateTime.UtcNow;
        var lastTicks = Volatile.Read(ref _lastUpdatedUtcTicks);
        if (!forceUpdate && lastTicks != 0)
        {
            var age = nowUtc - new DateTime(lastTicks, DateTimeKind.Utc);
            if (age <= _cacheDuration)
            {
                // Fast path: hot read without locks or allocations.
                return new ValueTask<T>(_value!);
            }
        }

        return new ValueTask<T>(GetValueSlowAsync(forceUpdate, nowUtc));
    }

    private async Task<T> GetValueSlowAsync(bool forceUpdate, DateTime nowUtc)
    {
        Task refresh;
        var serveStale = false;
        T? stale = default;

        lock (_gate)
        {
            var lastTicks = _lastUpdatedUtcTicks;
            var hasValue = lastTicks != 0;
            var expired = !hasValue ||
                          forceUpdate ||
                          nowUtc - new DateTime(lastTicks, DateTimeKind.Utc) > _cacheDuration;

            if (!expired)
            {
                return _value!;
            }

            if (_refreshTask is { IsCompleted: false })
            {
                refresh = _refreshTask;
            }
            else
            {
                refresh = RefreshCoreAsync();
                _refreshTask = refresh;
            }

            // Stale-while-revalidate: concurrent readers keep the previous sample instead of
            // serializing behind a 300ms+ factory or a full Java scan.
            if (hasValue && !forceUpdate)
            {
                serveStale = true;
                stale = _value;
            }
        }

        if (serveStale)
        {
            return stale!;
        }

        try
        {
            await refresh.ConfigureAwait(false);
        }
        catch
        {
            // Surface failure only when no usable sample exists (first load / force).
            lock (_gate)
            {
                if (_lastUpdatedUtcTicks != 0 && !forceUpdate)
                {
                    return _value!;
                }
            }

            throw;
        }

        lock (_gate)
        {
            if (_lastUpdatedUtcTicks != 0)
            {
                return _value!;
            }

            if (_lastFailure is not null)
            {
                throw _lastFailure;
            }

            throw new InvalidOperationException("Timed lazy cell refresh completed without a value.");
        }
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            var newValue = await _valueFactory().ConfigureAwait(false);
            lock (_gate)
            {
                _value = newValue;
                _lastUpdatedUtcTicks = DateTime.UtcNow.Ticks;
                _lastFailure = null;
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                // Keep any prior good sample; only surface the failure when no value exists.
                _lastFailure = exception;
            }

            throw;
        }
    }
}
