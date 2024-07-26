namespace MCServerLauncher.Daemon.Utils.Cache;

/// <summary>
///     用于缓存的接口
/// </summary>
/// <typeparam name="T">T是协变的</typeparam>
public interface ICacheable<out T>
{
    public T Value { get; }
}

public interface IAsyncCacheable<T>
{
    public Task<T> Value { get; }
}