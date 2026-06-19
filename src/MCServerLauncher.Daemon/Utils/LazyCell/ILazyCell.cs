namespace MCServerLauncher.Daemon.Utils.LazyCell;

/// <summary>
///     用于缓存的接口
/// </summary>
/// <typeparam name="T">T是协变的</typeparam>
public interface ILazyCell<out T>
{
    public T Value { get; }
}

public interface IAsyncLazyCell<T>
{
    public ValueTask<T> Value { get; }
}