namespace MCServerLauncher.Common.Utils;

public struct Option<T> : IEquatable<Option<T>> where T : class
{
    private T? _content;

    public static Option<T> Some(T content) => new() { _content = content };
    public static Option<T> None => new();

    public Option<TResult> Map<TResult>(Func<T, TResult> func) where TResult : class =>
        new() { _content = _content == null ? null : func(_content) };

    public TResult MapOr<TResult>(Func<T, TResult> func, TResult defaultValue) where TResult : class =>
        _content != null ? func(_content) : defaultValue;

    public TResult MapOrElse<TResult>(Func<T, TResult> func, Func<TResult> defaultValue) where TResult : class =>
        _content != null ? func(_content) : defaultValue();

    public bool IsSome() => _content != null;
    public bool IsNone() => _content == null;

    public T Unwarp()
    {
        if (_content == null)
            throw new NullReferenceException();
        return _content;
    }

    public T UnwarpOr(T defaultValue)
    {
        return _content ?? defaultValue;
    }

    public T UnwarpOrElse(Func<T> func)
    {
        return _content ?? func();
    }

    public bool Equals(Option<T> other) => _content?.Equals(other._content) ?? false;

    public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

    public override int GetHashCode() => _content?.GetHashCode() ?? 0;
}