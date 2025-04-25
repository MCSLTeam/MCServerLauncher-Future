namespace MCServerLauncher.Daemon.Utils.LazyCell;

/// <summary>
///     用于缓存的接口, 具有过期时间
/// </summary>
/// <typeparam name="T">T是协变的</typeparam>
public interface ITimedLazyCell<out T> : ILazyCell<T>
{
    DateTime LastUpdated { get; }
    TimeSpan CacheDuration { get; }
    bool IsExpired();
    void Update();
}

public interface IAsyncTimedLazyCell<T> : IAsyncLazyCell<T>
{
    DateTime LastUpdated { get; }
    TimeSpan CacheDuration { get; }
    bool IsExpired();
    Task Update();
}