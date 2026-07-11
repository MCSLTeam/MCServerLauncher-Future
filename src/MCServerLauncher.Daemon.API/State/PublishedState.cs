namespace MCServerLauncher.Daemon.API.State;

/// <summary>
/// Represents one immutable value published by a <see cref="StatePublisher{T}" />.
/// </summary>
public sealed class PublishedState<T> where T : class
{
    internal PublishedState(long version, T value)
    {
        Version = version;
        Value = value;
    }

    public long Version { get; }

    public T Value { get; }
}
