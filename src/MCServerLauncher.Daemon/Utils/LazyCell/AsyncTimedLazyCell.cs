using Serilog;

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
    private readonly Action<Exception> _backgroundRefreshFailureObserver;
    private CacheEntry? _entry;
    private Task<T>? _refreshTask;

    public AsyncTimedLazyCell(Func<Task<T>> valueFactory, TimeSpan cacheDuration)
        : this(valueFactory, cacheDuration, LogBackgroundRefreshFailure)
    {
    }

    internal AsyncTimedLazyCell(
        Func<Task<T>> valueFactory,
        TimeSpan cacheDuration,
        Action<Exception> backgroundRefreshFailureObserver)
    {
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _backgroundRefreshFailureObserver = backgroundRefreshFailureObserver ??
                                            throw new ArgumentNullException(nameof(backgroundRefreshFailureObserver));
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
            var entry = Volatile.Read(ref _entry);
            return entry is null
                ? DateTime.MinValue
                : new DateTime(entry.LastUpdatedUtcTicks, DateTimeKind.Utc).ToLocalTime();
        }
    }

    public TimeSpan CacheDuration => _cacheDuration;

    public ValueTask<T> Value => GetValue();

    public bool IsExpired()
    {
        var entry = Volatile.Read(ref _entry);
        if (entry is null)
        {
            return true;
        }

        return DateTime.UtcNow - new DateTime(entry.LastUpdatedUtcTicks, DateTimeKind.Utc) > _cacheDuration;
    }

    public Task Update()
    {
        lock (_gate)
        {
            return GetOrStartRefreshLocked(out _);
        }
    }

    private ValueTask<T> GetValue()
    {
        var nowUtc = DateTime.UtcNow;
        var entry = Volatile.Read(ref _entry);
        if (entry is not null)
        {
            var age = nowUtc - new DateTime(entry.LastUpdatedUtcTicks, DateTimeKind.Utc);
            if (age <= _cacheDuration)
            {
                // Fast path: hot read without locks or allocations.
                return new ValueTask<T>(entry.Value);
            }
        }

        return GetValueSlow(nowUtc);
    }

    private ValueTask<T> GetValueSlow(DateTime nowUtc)
    {
        Task<T> refresh;
        CacheEntry? staleEntry;
        bool startedRefresh;

        lock (_gate)
        {
            var entry = _entry;
            if (entry is not null &&
                nowUtc - new DateTime(entry.LastUpdatedUtcTicks, DateTimeKind.Utc) <= _cacheDuration)
            {
                return new ValueTask<T>(entry.Value);
            }

            staleEntry = entry;
            refresh = GetOrStartRefreshLocked(out startedRefresh);
        }

        if (staleEntry is not null)
        {
            if (startedRefresh)
            {
                _ = ObserveBackgroundRefreshAsync(refresh);
            }

            // Stale-while-revalidate: readers do not allocate or wait behind the refresh.
            return new ValueTask<T>(staleEntry.Value);
        }

        // Every first-load caller receives its own ValueTask wrapper over the same reusable Task<T>.
        return new ValueTask<T>(refresh);
    }

    private Task<T> GetOrStartRefreshLocked(out bool startedRefresh)
    {
        if (_refreshTask is { IsCompleted: false })
        {
            startedRefresh = false;
            return _refreshTask;
        }

        startedRefresh = true;
        _refreshTask = RefreshCoreAsync();
        return _refreshTask;
    }

    private async Task<T> RefreshCoreAsync()
    {
        var newValue = await _valueFactory().ConfigureAwait(false);
        var newEntry = new CacheEntry(newValue, DateTime.UtcNow.Ticks);
        lock (_gate)
        {
            Volatile.Write(ref _entry, newEntry);
        }

        return newValue;
    }

    private async Task ObserveBackgroundRefreshAsync(Task<T> refresh)
    {
        try
        {
            await refresh.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                _backgroundRefreshFailureObserver(exception);
            }
            catch (Exception observerException)
            {
                Log.Error(
                    observerException,
                    "[AsyncTimedLazyCell] Background refresh failure observer failed for {ValueType}",
                    typeof(T).FullName ?? typeof(T).Name);
            }
        }
    }

    private static void LogBackgroundRefreshFailure(Exception exception) =>
        Log.Warning(
            exception,
            "[AsyncTimedLazyCell] Background refresh failed for {ValueType}",
            typeof(T).FullName ?? typeof(T).Name);

    private sealed class CacheEntry(T value, long lastUpdatedUtcTicks)
    {
        public T Value { get; } = value;

        public long LastUpdatedUtcTicks { get; } = lastUpdatedUtcTicks;
    }
}
