namespace MCServerLauncher.Daemon.API.Events;

public enum DaemonEventFieldKind
{
    Missing = 0,
    ExplicitNull = 1,
    Value = 2
}

public readonly record struct DaemonEventField<T>
{
    private readonly DaemonEventFieldKind _kind;
    private readonly T? _value;

    private DaemonEventField(DaemonEventFieldKind kind, T? value = default)
    {
        _kind = kind;
        _value = value;
    }

    public static DaemonEventField<T> Missing => default;

    public static DaemonEventField<T> ExplicitNull =>
        new(DaemonEventFieldKind.ExplicitNull);

    public DaemonEventFieldKind Kind => _kind;

    public T Value => Kind == DaemonEventFieldKind.Value
        ? _value!
        : throw new InvalidOperationException("Only a value event field exposes a value.");

    public override string ToString() => Kind switch
    {
        DaemonEventFieldKind.Missing => "DaemonEventField { Kind = Missing }",
        DaemonEventFieldKind.ExplicitNull => "DaemonEventField { Kind = ExplicitNull }",
        DaemonEventFieldKind.Value => $"DaemonEventField {{ Kind = Value, Value = {_value} }}",
        _ => $"DaemonEventField {{ Kind = {Kind} }}"
    };

    public static DaemonEventField<T> FromValue(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DaemonEventField<T>(DaemonEventFieldKind.Value, value);
    }
}
