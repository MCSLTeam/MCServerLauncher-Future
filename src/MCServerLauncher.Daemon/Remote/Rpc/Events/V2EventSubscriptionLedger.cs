using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Rpc.Events;

internal enum V2EventMetaValueKind
{
    Omitted,
    ExplicitNull,
    Object
}

internal sealed class V2CanonicalEventMeta
{
    private V2CanonicalEventMeta(
        FrozenEventBinding binding,
        V2EventMetaValueKind kind,
        ImmutableArray<byte> canonicalUtf8)
    {
        Binding = binding;
        Kind = kind;
        CanonicalUtf8 = canonicalUtf8;
    }

    internal FrozenEventBinding Binding { get; }

    internal V2EventMetaValueKind Kind { get; }

    internal ImmutableArray<byte> CanonicalUtf8 { get; }

    internal static V2CanonicalEventMeta Omitted(FrozenEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Descriptor.MetaPresence == OpenRpcEventFieldPresence.Required)
            throw new ArgumentException("The event descriptor requires metadata.", nameof(binding));

        return new V2CanonicalEventMeta(binding, V2EventMetaValueKind.Omitted, []);
    }

    internal static V2CanonicalEventMeta ExplicitNull(FrozenEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Descriptor.MetaPresence != OpenRpcEventFieldPresence.Optional)
            throw new ArgumentException("The event descriptor does not allow null metadata.", nameof(binding));

        return new V2CanonicalEventMeta(binding, V2EventMetaValueKind.ExplicitNull, []);
    }

    internal static V2CanonicalEventMeta FromTypedObject(FrozenEventBinding binding, object value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(value);

        var typeInfo = binding.Descriptor.MetaTypeInfo;
        if (binding.Descriptor.MetaPresence == OpenRpcEventFieldPresence.Omitted || typeInfo is null)
            throw new ArgumentException("The event descriptor omits metadata.", nameof(binding));
        if (value.GetType() != typeInfo.Type)
            throw new ArgumentException("The event metadata does not exactly match its descriptor.", nameof(value));

        return new V2CanonicalEventMeta(
            binding,
            V2EventMetaValueKind.Object,
            JsonSerializer.SerializeToUtf8Bytes(value, typeInfo).ToImmutableArray());
    }
}

internal sealed class V2EventSubscriptionLedger : IProtocolSubscriptionOperations
{
    private readonly object _gate = new();
    private readonly FrozenProtocolCatalog _catalog;
    private readonly Permissions _permissions;
    private readonly IV2EventMetaCanonicalizer _canonicalizer;
    private ImmutableHashSet<SubscriptionKey> _subscriptions =
        ImmutableHashSet.Create<SubscriptionKey>(SubscriptionKeyComparer.Instance);
    private bool _closed;

    internal V2EventSubscriptionLedger(FrozenProtocolCatalog catalog, V2ConnectionOwner owner)
        : this(catalog, owner, V2EventMetaCanonicalizer.Instance)
    {
    }

    internal V2EventSubscriptionLedger(
        FrozenProtocolCatalog catalog,
        V2ConnectionOwner owner,
        IV2EventMetaCanonicalizer canonicalizer)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentNullException.ThrowIfNull(owner);
        _canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
        _permissions = owner.Permissions.IsDefaultOrEmpty
            ? Permissions.Never
            : new Permissions(owner.Permissions.ToArray());
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _subscriptions.Count;
        }
    }

    public Result<Unit, DaemonError> Subscribe(EventSubscriptionRequest request)
    {
        lock (_gate)
        {
            if (_closed)
                return Closed();
        }

        var prepared = Prepare(request);
        if (prepared.IsErr(out _))
            return Result.Err<Unit, DaemonError>(prepared.UnwrapErr());

        lock (_gate)
        {
            if (_closed)
                return Closed();

            _subscriptions = _subscriptions.Add(prepared.Unwrap());
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
    }

    public Result<Unit, DaemonError> Unsubscribe(EventSubscriptionRequest request)
    {
        lock (_gate)
        {
            if (_closed)
                return Closed();
        }

        var prepared = Prepare(request);
        if (prepared.IsErr(out _))
            return Result.Err<Unit, DaemonError>(prepared.UnwrapErr());

        lock (_gate)
        {
            if (_closed)
                return Closed();

            _subscriptions = _subscriptions.Remove(prepared.Unwrap());
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
    }

    internal bool Matches(FrozenEventBinding binding, V2CanonicalEventMeta actualMeta)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(actualMeta);

        if (!_catalog.TryGetEvent(binding.Descriptor.Name, out var catalogBinding) ||
            !ReferenceEquals(catalogBinding, binding) ||
            !ReferenceEquals(actualMeta.Binding, binding))
        {
            return false;
        }

        var exact = new SubscriptionKey(
            binding,
            ToFilterKind(actualMeta.Kind),
            actualMeta.CanonicalUtf8);
        var wildcard = new SubscriptionKey(
            binding,
            EventMetaFilterKind.Missing,
            []);

        lock (_gate)
            return !_closed && (_subscriptions.Contains(wildcard) || _subscriptions.Contains(exact));
    }

    internal void Close()
    {
        lock (_gate)
        {
            if (_closed)
                return;

            _closed = true;
            _subscriptions = ImmutableHashSet.Create<SubscriptionKey>(SubscriptionKeyComparer.Instance);
        }
    }

    private Result<SubscriptionKey, DaemonError> Prepare(EventSubscriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        EventName name;
        try
        {
            name = new EventName(request.Event);
        }
        catch (ArgumentException)
        {
            return Result.Err<SubscriptionKey, DaemonError>(InvalidEvent());
        }

        if (!_catalog.TryGetEvent(name, out var binding))
            return Result.Err<SubscriptionKey, DaemonError>(UnknownEvent());

        if (!_permissions.Matches(Permission.Of(binding.Descriptor.Permission.Value)))
        {
            return Result.Err<SubscriptionKey, DaemonError>(
                new PermissionDaemonError("permission.denied", "Permission denied."));
        }

        return PrepareFilter(binding, request.Meta);
    }

    private Result<SubscriptionKey, DaemonError> PrepareFilter(
        FrozenEventBinding binding,
        EventMetaFilter filter)
    {
        var prepared = _canonicalizer.Prepare(binding, filter);
        if (prepared.IsErr(out _))
            return Result.Err<SubscriptionKey, DaemonError>(prepared.UnwrapErr());

        var value = prepared.Unwrap();
        if (!ReferenceEquals(value.Binding, binding))
        {
            return Result.Err<SubscriptionKey, DaemonError>(
                new InternalDaemonError("event.meta.binding_mismatch", "The event metadata binding is invalid."));
        }

        return Result.Ok<SubscriptionKey, DaemonError>(
            new SubscriptionKey(value.Binding, value.Kind, value.CanonicalUtf8));
    }

    private static EventMetaFilterKind ToFilterKind(V2EventMetaValueKind kind) => kind switch
    {
        V2EventMetaValueKind.Omitted => EventMetaFilterKind.Missing,
        V2EventMetaValueKind.ExplicitNull => EventMetaFilterKind.ExplicitNull,
        V2EventMetaValueKind.Object => EventMetaFilterKind.Object,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static Result<Unit, DaemonError> Closed() =>
        Result.Err<Unit, DaemonError>(
            new TransportDaemonError("connection.closed", "The connection is closed."));

    private static ValidationDaemonError InvalidEvent() =>
        new("event.invalid", "The event name is invalid.");

    private static NotFoundDaemonError UnknownEvent() =>
        new("event.not_found", "The event is not available.");

    private sealed record SubscriptionKey(
        FrozenEventBinding Binding,
        EventMetaFilterKind FilterKind,
        ImmutableArray<byte> CanonicalUtf8);

    private sealed class SubscriptionKeyComparer : IEqualityComparer<SubscriptionKey>
    {
        internal static SubscriptionKeyComparer Instance { get; } = new();

        public bool Equals(SubscriptionKey? left, SubscriptionKey? right) =>
            ReferenceEquals(left, right) ||
            (left is not null &&
             right is not null &&
             ReferenceEquals(left.Binding, right.Binding) &&
             left.FilterKind == right.FilterKind &&
             left.CanonicalUtf8.AsSpan().SequenceEqual(right.CanonicalUtf8.AsSpan()));

        public int GetHashCode(SubscriptionKey value)
        {
            var hash = new HashCode();
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Binding));
            hash.Add(value.FilterKind);
            foreach (var item in value.CanonicalUtf8)
                hash.Add(item);
            return hash.ToHashCode();
        }
    }
}

internal sealed record V2PreparedEventMetaFilter(
    FrozenEventBinding Binding,
    EventMetaFilterKind Kind,
    ImmutableArray<byte> CanonicalUtf8);

internal sealed record V2PreparedEventMetaValue(
    EventMetaFilterKind Kind,
    ImmutableArray<byte> CanonicalUtf8);

internal static class V2EventMetaMatcher
{
    internal static bool Matches(
        OpenRpcEventFieldPresence presence,
        V2PreparedEventMetaValue filter,
        V2EventMetaValueKind actualKind,
        ImmutableArray<byte> actualCanonicalUtf8)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (actualCanonicalUtf8.IsDefault)
            throw new ArgumentException("Actual canonical metadata bytes cannot be default.", nameof(actualCanonicalUtf8));

        var actualAllowed = actualKind switch
        {
            V2EventMetaValueKind.Omitted => presence is OpenRpcEventFieldPresence.Omitted or OpenRpcEventFieldPresence.Optional,
            V2EventMetaValueKind.ExplicitNull => presence == OpenRpcEventFieldPresence.Optional,
            V2EventMetaValueKind.Object => presence is OpenRpcEventFieldPresence.Required or OpenRpcEventFieldPresence.Optional,
            _ => false
        };
        if (!actualAllowed)
            return false;
        if (filter.Kind == EventMetaFilterKind.Missing)
            return true;

        return (filter.Kind, actualKind) switch
        {
            (EventMetaFilterKind.ExplicitNull, V2EventMetaValueKind.ExplicitNull) => true,
            (EventMetaFilterKind.Object, V2EventMetaValueKind.Object) =>
                filter.CanonicalUtf8.AsSpan().SequenceEqual(actualCanonicalUtf8.AsSpan()),
            _ => false
        };
    }
}

internal interface IV2EventMetaCanonicalizer
{
    Result<V2PreparedEventMetaFilter, DaemonError> Prepare(
        FrozenEventBinding binding,
        EventMetaFilter filter);
}

internal sealed class V2EventMetaCanonicalizer : IV2EventMetaCanonicalizer
{
    internal static V2EventMetaCanonicalizer Instance { get; } = new();

    private V2EventMetaCanonicalizer()
    {
    }

    public Result<V2PreparedEventMetaFilter, DaemonError> Prepare(
        FrozenEventBinding binding,
        EventMetaFilter filter)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(filter);

        var prepared = PrepareValue(
            binding.Descriptor.MetaPresence,
            binding.Descriptor.MetaTypeInfo,
            filter);
        return prepared.IsOk(out var value)
            ? Result.Ok<V2PreparedEventMetaFilter, DaemonError>(
                new V2PreparedEventMetaFilter(binding, value.Kind, value.CanonicalUtf8))
            : Result.Err<V2PreparedEventMetaFilter, DaemonError>(prepared.UnwrapErr());
    }

    internal static Result<V2PreparedEventMetaValue, DaemonError> PrepareValue(
        OpenRpcEventFieldPresence presence,
        JsonTypeInfo? typeInfo,
        EventMetaFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        switch (filter.Kind)
        {
            case EventMetaFilterKind.Missing:
                return Result.Ok<V2PreparedEventMetaValue, DaemonError>(
                    new V2PreparedEventMetaValue(filter.Kind, []));
            case EventMetaFilterKind.ExplicitNull when presence == OpenRpcEventFieldPresence.Optional:
                return Result.Ok<V2PreparedEventMetaValue, DaemonError>(
                    new V2PreparedEventMetaValue(filter.Kind, []));
            case EventMetaFilterKind.ExplicitNull:
                return Result.Err<V2PreparedEventMetaValue, DaemonError>(InvalidMeta());
            case EventMetaFilterKind.Object when
                presence != OpenRpcEventFieldPresence.Omitted &&
                typeInfo is not null:
                try
                {
                    var typed = JsonSerializer.Deserialize(filter.ObjectUtf8Json.AsSpan(), typeInfo);
                    if (typed is null || typed.GetType() != typeInfo.Type)
                        return Result.Err<V2PreparedEventMetaValue, DaemonError>(InvalidMeta());

                    var canonical = JsonSerializer.SerializeToUtf8Bytes(typed, typeInfo);
                    return Result.Ok<V2PreparedEventMetaValue, DaemonError>(
                        new V2PreparedEventMetaValue(filter.Kind, canonical.ToImmutableArray()));
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    return Result.Err<V2PreparedEventMetaValue, DaemonError>(InvalidMeta());
                }
            case EventMetaFilterKind.Object:
                return Result.Err<V2PreparedEventMetaValue, DaemonError>(InvalidMeta());
            default:
                return Result.Err<V2PreparedEventMetaValue, DaemonError>(InvalidMeta());
        }
    }

    private static ValidationDaemonError InvalidMeta() =>
        new("event.meta.invalid", "The event metadata filter is invalid.");
}
