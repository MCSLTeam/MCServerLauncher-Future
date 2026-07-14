namespace MCServerLauncher.Daemon.API.Events;

public readonly record struct DaemonEventFilter<TMeta>
{
    private readonly DaemonEventFieldKind _kind;
    private readonly TMeta? _value;

    private DaemonEventFilter(DaemonEventFieldKind kind, TMeta? value = default)
    {
        _kind = kind;
        _value = value;
    }

    public static DaemonEventFilter<TMeta> Wildcard => default;

    public static DaemonEventFilter<TMeta> ExplicitNull =>
        new(DaemonEventFieldKind.ExplicitNull);

    public DaemonEventFieldKind Kind => _kind;

    public TMeta Value => Kind == DaemonEventFieldKind.Value
        ? _value!
        : throw new InvalidOperationException("Only an exact event filter exposes a value.");

    public override string ToString() => Kind switch
    {
        DaemonEventFieldKind.Missing => "DaemonEventFilter { Kind = Missing }",
        DaemonEventFieldKind.ExplicitNull => "DaemonEventFilter { Kind = ExplicitNull }",
        DaemonEventFieldKind.Value => $"DaemonEventFilter {{ Kind = Value, Value = {_value} }}",
        _ => $"DaemonEventFilter {{ Kind = {Kind} }}"
    };

    public static DaemonEventFilter<TMeta> Exact(TMeta value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DaemonEventFilter<TMeta>(DaemonEventFieldKind.Value, value);
    }
}
