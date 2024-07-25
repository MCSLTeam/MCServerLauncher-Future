namespace MCServerLauncher.Daemon.Utils.Cache;

public class TimedCache<T> : ITimedCacheable<T>
{
    public DateTime LastUpdated { get; private set; }
    public TimeSpan CacheDuration { get; }
    public T Value => GetValue();

    private T _value;
    private readonly Func<T> _valueFactory;

    public TimedCache(Func<T> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory;
        CacheDuration = cacheDuration;
    }

    private T GetValue()
    {
        if (IsExpired()) Update();
        return _value;
    }

    public bool IsExpired() => (DateTime.Now - LastUpdated) > CacheDuration;

    public void Update()
    {
        _value = _valueFactory();
        LastUpdated = DateTime.Now;
    }
}

public class AsyncTimedCache<T> : IAsyncTimedCacheable<T>
{
    public DateTime LastUpdated { get; private set; }
    public TimeSpan CacheDuration { get; }
    public Task<T> Value => GetValue();

    private T _value;
    private readonly Func<Task<T>> _valueFactory;

    public AsyncTimedCache(Func<Task<T>> valueFactory, TimeSpan cacheDuration)
    {
        _valueFactory = valueFactory;
        CacheDuration = cacheDuration;
    }

    private async Task<T> GetValue()
    {
        if (IsExpired()) await Update();
        return _value;
    }

    public bool IsExpired() => (DateTime.Now - LastUpdated) > CacheDuration;

    public async Task Update()
    {
        _value = await _valueFactory();
        LastUpdated = DateTime.Now;
    }
}