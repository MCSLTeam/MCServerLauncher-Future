namespace MCServerLauncher.Daemon.Utils.Cache;

public class TimedCache<T> : ITimedCacheable<T>
{
    private readonly Func<T> _valueFactory;

    private T _value;

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
    private readonly Func<Task<T>> _valueFactory;

    private T _value;

    public AsyncTimedCache(Func<Task<T>> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory;
        CacheDuration = cacheDuration;
    }

    public DateTime LastUpdated { get; private set; }
    public TimeSpan CacheDuration { get; }
    public Task<T> Value => GetValue();

    public bool IsExpired()
    {
        return DateTime.Now - LastUpdated > CacheDuration;
    }

    public async Task Update()
    {
        _value = await _valueFactory();
        LastUpdated = DateTime.Now;
    }

    private async Task<T> GetValue()
    {
        if (IsExpired()) await Update();
        return _value;
    }
}