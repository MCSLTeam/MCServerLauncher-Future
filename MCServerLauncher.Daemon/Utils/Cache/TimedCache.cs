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