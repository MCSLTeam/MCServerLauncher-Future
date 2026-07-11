using System.Threading;

namespace MCServerLauncher.Daemon.API.State;

/// <summary>
/// Publishes complete immutable state values with serialized copy-on-write updates.
/// </summary>
public sealed class StatePublisher<T> where T : class
{
    private readonly Lock _writerLock = new();
    private bool _writerActive;
    private PublishedState<T> _current;

    public StatePublisher(T initialValue)
    {
        ArgumentNullException.ThrowIfNull(initialValue);
        _current = new PublishedState<T>(0, initialValue);
    }

    public PublishedState<T> Current => Volatile.Read(ref _current);

    /// <summary>
    /// Computes and publishes the next immutable state while holding the writer lock.
    /// </summary>
    /// <remarks>
    /// The updater must be synchronous, non-blocking, and free of callbacks into this
    /// publisher. Reentrant <see cref="Update"/> or <see cref="Publish"/> calls are
    /// rejected so an inner publication cannot be overwritten by an older version.
    /// </remarks>
    public PublishedState<T> Update(Func<T, T> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_writerLock)
        {
            ThrowIfWriterReentered();
            _writerActive = true;
            try
            {
                var current = _current;
                var nextValue = update(current.Value);
                ArgumentNullException.ThrowIfNull(nextValue);

                var next = new PublishedState<T>(checked(current.Version + 1), nextValue);
                Volatile.Write(ref _current, next);
                return next;
            }
            finally
            {
                _writerActive = false;
            }
        }
    }

    /// <summary>
    /// Publishes an immutable state received from an authoritative source.
    /// </summary>
    /// <remarks>
    /// The supplied version may skip values, which lets a remote mirror publish a
    /// full resynchronization without replaying every missing delta. It must still
    /// be strictly newer than the current version.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="version"/> is not strictly newer than the
    /// current published version. The current state is unchanged.
    /// </exception>
    public PublishedState<T> Publish(long version, T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_writerLock)
        {
            ThrowIfWriterReentered();
            _writerActive = true;
            try
            {
                var current = _current;
                if (version <= current.Version)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(version),
                        version,
                        "An authoritative state version must be strictly newer than the current version.");
                }

                var next = new PublishedState<T>(version, value);
                Volatile.Write(ref _current, next);
                return next;
            }
            finally
            {
                _writerActive = false;
            }
        }
    }

    private void ThrowIfWriterReentered()
    {
        if (_writerActive)
        {
            throw new InvalidOperationException("State publisher writes cannot reenter the same publisher.");
        }
    }
}
