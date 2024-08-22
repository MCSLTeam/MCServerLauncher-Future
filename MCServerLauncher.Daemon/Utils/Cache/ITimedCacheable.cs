namespace MCServerLauncher.Daemon.Utils.Cache;

/// <summary>
///    用于缓存的接口, 具有过期时间
/// </summary>
/// <typeparam name="T">T是协变的</typeparam>
public interface ITimedCacheable<out T> : ICacheable<T>
{
    DateTime LastUpdated { get; }
    TimeSpan CacheDuration { get; }
    bool IsExpired();
    void Update();
}

public interface IAsyncTimedCacheable<T> : IAsyncCacheable<T>
{
    DateTime LastUpdated { get; }
    TimeSpan CacheDuration { get; }
    bool IsExpired();
    Task Update();
}