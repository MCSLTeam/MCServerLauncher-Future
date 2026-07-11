using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Remote.Rpc.Catalog;

/// <summary>
/// Identifies the host-controlled execution owner of a catalog definition.
/// This is daemon-internal on purpose: public protocol descriptors never expose host lifetime state.
/// </summary>
internal sealed class ProtocolExecutionOwner : IEquatable<ProtocolExecutionOwner>
{
    private ProtocolExecutionOwner(ProtocolExecutionOwnerKind kind, ProtocolOwnerIdentity? plugin)
    {
        Kind = kind;
        Plugin = plugin;
    }

    public static ProtocolExecutionOwner BuiltIn { get; } = new(ProtocolExecutionOwnerKind.BuiltIn, null);

    public ProtocolExecutionOwnerKind Kind { get; }

    public ProtocolOwnerIdentity? Plugin { get; }

    public static ProtocolExecutionOwner ForPlugin(ProtocolOwnerIdentity plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var normalizedId = NormalizePluginId(plugin.Id);
        return new ProtocolExecutionOwner(
            ProtocolExecutionOwnerKind.Plugin,
            new ProtocolOwnerIdentity(normalizedId, plugin.Version));
    }

    public bool Equals(ProtocolExecutionOwner? other) =>
        other is not null &&
        Kind == other.Kind &&
        StringComparer.Ordinal.Equals(Plugin?.Id, other.Plugin?.Id) &&
        StringComparer.Ordinal.Equals(Plugin?.Version, other.Plugin?.Version);

    public override bool Equals(object? obj) => Equals(obj as ProtocolExecutionOwner);

    public override int GetHashCode() =>
        HashCode.Combine(
            Kind,
            Plugin is null ? 0 : StringComparer.Ordinal.GetHashCode(Plugin.Id),
            Plugin is null ? 0 : StringComparer.Ordinal.GetHashCode(Plugin.Version));

    internal string GetOwnedNamespace(ProtocolCatalogEntryKind entryKind)
    {
        if (Kind != ProtocolExecutionOwnerKind.Plugin || Plugin is null)
        {
            throw new InvalidOperationException("Only plugin owners have a plugin namespace.");
        }

        var suffix = entryKind == ProtocolCatalogEntryKind.Rpc ? "rpc" : "event";
        return $"plugin.{Plugin.Id}.{suffix}.";
    }

    private static string NormalizePluginId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.ToLowerInvariant();
        var segmentStart = true;

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            var isLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';

            if (character == '.')
            {
                if (segmentStart || index == normalized.Length - 1 || normalized[index - 1] == '-')
                {
                    throw new ArgumentException("A plugin id must use dot-separated lowercase segments.", nameof(value));
                }

                segmentStart = true;
                continue;
            }

            if ((segmentStart && !isLetter && !isDigit) || (!isLetter && !isDigit && character != '-'))
            {
                throw new ArgumentException("A plugin id must use lowercase ASCII letters, digits, dots, or hyphens.", nameof(value));
            }

            if (index == normalized.Length - 1 && character == '-')
            {
                throw new ArgumentException("A plugin id segment must end with a lowercase ASCII letter or digit.", nameof(value));
            }

            segmentStart = false;
        }

        return normalized;
    }
}

internal enum ProtocolExecutionOwnerKind
{
    BuiltIn,
    Plugin
}

internal enum ProtocolCatalogEntryKind
{
    Rpc,
    Event
}

/// <summary>
/// Application-neutral RPC invocation context. Phase 4 supplies JSON-RPC and connection details
/// outside this seam; catalog handlers only receive typed request values and cancellation.
/// </summary>
internal sealed class ProtocolInvocationContext(ProtocolExecutionOwner executionOwner)
{
    public ProtocolExecutionOwner ExecutionOwner { get; } = executionOwner ?? throw new ArgumentNullException(nameof(executionOwner));
}

internal delegate ValueTask<TResult> ProtocolRpcHandler<TRequest, TResult>(
    ProtocolInvocationContext context,
    TRequest request,
    CancellationToken cancellationToken);

internal abstract class RpcBinding
{
    protected RpcBinding(ProtocolExecutionOwner owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public ProtocolExecutionOwner Owner { get; }

    public abstract Type RequestType { get; }

    public abstract Type ResultType { get; }

    internal abstract void ValidateTypes(RpcDescriptor descriptor);
}

internal sealed class RpcBinding<TRequest, TResult> : RpcBinding
{
    public RpcBinding(
        ProtocolExecutionOwner owner,
        ProtocolRpcHandler<TRequest, TResult> handler)
        : base(owner)
    {
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public ProtocolRpcHandler<TRequest, TResult> Handler { get; }

    public override Type RequestType => typeof(TRequest);

    public override Type ResultType => typeof(TResult);

    internal override void ValidateTypes(RpcDescriptor descriptor)
    {
        if (descriptor.RequestTypeInfo.Type != typeof(TRequest) || descriptor.ResultTypeInfo.Type != typeof(TResult))
        {
            throw new ArgumentException(
                $"RPC binding '{descriptor.Method.Value}' must use '{descriptor.RequestTypeInfo.Type}' -> '{descriptor.ResultTypeInfo.Type}'.");
        }
    }
}

internal abstract class EventBinding
{
    protected EventBinding(ProtocolExecutionOwner owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public ProtocolExecutionOwner Owner { get; }

    public abstract Type DataType { get; }

    public abstract Type? MetaType { get; }

    internal abstract void ValidateTypes(EventDescriptor descriptor);
}

internal sealed class EventBinding<TData> : EventBinding
{
    public EventBinding(ProtocolExecutionOwner owner)
        : base(owner)
    {
    }

    public override Type DataType => typeof(TData);

    public override Type? MetaType => null;

    internal override void ValidateTypes(EventDescriptor descriptor)
    {
        if (descriptor.DataTypeInfo.Type != typeof(TData) || descriptor.MetaTypeInfo is not null)
        {
            throw new ArgumentException($"Event binding '{descriptor.Name.Value}' must omit metadata and use '{descriptor.DataTypeInfo.Type}' data.");
        }
    }
}

internal sealed class EventBinding<TData, TMeta> : EventBinding
{
    public EventBinding(ProtocolExecutionOwner owner)
        : base(owner)
    {
    }

    public override Type DataType => typeof(TData);

    public override Type? MetaType => typeof(TMeta);

    internal override void ValidateTypes(EventDescriptor descriptor)
    {
        if (descriptor.DataTypeInfo.Type != typeof(TData) || descriptor.MetaTypeInfo?.Type != typeof(TMeta))
        {
            throw new ArgumentException(
                $"Event binding '{descriptor.Name.Value}' must use '{descriptor.DataTypeInfo.Type}' data and '{descriptor.MetaTypeInfo?.Type}' metadata.");
        }
    }
}

internal sealed class ProtocolCatalogBuilder
{
    private readonly object _gate = new();
    private readonly OpenRpcInfo _documentInfo;
    private readonly Dictionary<RpcMethod, ProtocolDefinition<RpcDescriptor>> _rpcs = new(RpcMethodComparer.Instance);
    private readonly Dictionary<EventName, ProtocolDefinition<EventDescriptor>> _events = new(EventNameComparer.Instance);
    private readonly Dictionary<RpcMethod, RpcBinding> _rpcBindings = new(RpcMethodComparer.Instance);
    private readonly Dictionary<EventName, EventBinding> _eventBindings = new(EventNameComparer.Instance);
    private FrozenProtocolCatalog? _catalog;

    public ProtocolCatalogBuilder(OpenRpcInfo documentInfo)
    {
        _documentInfo = documentInfo ?? throw new ArgumentNullException(nameof(documentInfo));
    }

    public FrozenProtocolCatalog? Catalog => Volatile.Read(ref _catalog);

    public void AddRpcDefinition(ProtocolExecutionOwner owner, RpcDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            EnsureMutable();
            ValidateRpcDescriptor(owner, descriptor);
            EnsureNoOtherEntryKindName(descriptor.Method.Value, ProtocolCatalogEntryKind.Rpc);
            if (!_rpcs.TryAdd(descriptor.Method, new ProtocolDefinition<RpcDescriptor>(owner, descriptor)))
            {
                throw new ArgumentException($"The RPC method '{descriptor.Method.Value}' is already registered.", nameof(descriptor));
            }
        }
    }

    public void AddEventDefinition(ProtocolExecutionOwner owner, EventDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            EnsureMutable();
            ValidateEventDescriptor(owner, descriptor);
            EnsureNoOtherEntryKindName(descriptor.Name.Value, ProtocolCatalogEntryKind.Event);
            if (!_events.TryAdd(descriptor.Name, new ProtocolDefinition<EventDescriptor>(owner, descriptor)))
            {
                throw new ArgumentException($"The event '{descriptor.Name.Value}' is already registered.", nameof(descriptor));
            }
        }
    }

    public void AddRpcBinding(RpcMethod method, RpcBinding binding)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(binding);

        lock (_gate)
        {
            EnsureMutable();
            ProtocolCatalogNamePolicy.ValidateOwnedName(binding.Owner, method.Value, ProtocolCatalogEntryKind.Rpc);
            EnsureNoOtherEntryKindName(method.Value, ProtocolCatalogEntryKind.Rpc);
            if (!_rpcBindings.TryAdd(method, binding))
            {
                throw new ArgumentException($"The RPC binding '{method.Value}' is already registered.", nameof(binding));
            }
        }
    }

    public void AddEventBinding(EventName name, EventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(binding);

        lock (_gate)
        {
            EnsureMutable();
            ProtocolCatalogNamePolicy.ValidateOwnedName(binding.Owner, name.Value, ProtocolCatalogEntryKind.Event);
            EnsureNoOtherEntryKindName(name.Value, ProtocolCatalogEntryKind.Event);
            if (!_eventBindings.TryAdd(name, binding))
            {
                throw new ArgumentException($"The event binding '{name.Value}' is already registered.", nameof(binding));
            }
        }
    }

    /// <summary>
    /// Convenience for the eventual built-in composition root. It deliberately does not enumerate
    /// the definitions or invent application bindings before that composition exists.
    /// </summary>
    public void RegisterBuiltInRpc(RpcDescriptor descriptor, RpcBinding binding)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(binding);

        lock (_gate)
        {
            EnsureMutable();
            EnsureBuiltInBinding(binding);
            ValidateRpcDescriptor(ProtocolExecutionOwner.BuiltIn, descriptor);
            binding.ValidateTypes(descriptor);
            EnsureNoOtherEntryKindName(descriptor.Method.Value, ProtocolCatalogEntryKind.Rpc);

            if (_rpcs.ContainsKey(descriptor.Method))
            {
                throw new ArgumentException($"The RPC method '{descriptor.Method.Value}' is already registered.", nameof(descriptor));
            }

            if (_rpcBindings.ContainsKey(descriptor.Method))
            {
                throw new ArgumentException($"The RPC binding '{descriptor.Method.Value}' is already registered.", nameof(binding));
            }

            _rpcs.Add(descriptor.Method, new ProtocolDefinition<RpcDescriptor>(ProtocolExecutionOwner.BuiltIn, descriptor));
            _rpcBindings.Add(descriptor.Method, binding);
        }
    }

    /// <summary>Convenience for the eventual built-in event composition root.</summary>
    public void RegisterBuiltInEvent(EventDescriptor descriptor, EventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(binding);

        lock (_gate)
        {
            EnsureMutable();
            EnsureBuiltInBinding(binding);
            ValidateEventDescriptor(ProtocolExecutionOwner.BuiltIn, descriptor);
            binding.ValidateTypes(descriptor);
            EnsureNoOtherEntryKindName(descriptor.Name.Value, ProtocolCatalogEntryKind.Event);

            if (_events.ContainsKey(descriptor.Name))
            {
                throw new ArgumentException($"The event '{descriptor.Name.Value}' is already registered.", nameof(descriptor));
            }

            if (_eventBindings.ContainsKey(descriptor.Name))
            {
                throw new ArgumentException($"The event binding '{descriptor.Name.Value}' is already registered.", nameof(binding));
            }

            _events.Add(descriptor.Name, new ProtocolDefinition<EventDescriptor>(ProtocolExecutionOwner.BuiltIn, descriptor));
            _eventBindings.Add(descriptor.Name, binding);
        }
    }

    public FrozenProtocolCatalog Freeze()
    {
        lock (_gate)
        {
            EnsureMutable();
            ProtocolCatalogNamePolicy.EnsureDistinctWireNames(_rpcs.Keys, _events.Keys);
            ValidateBindings();

            var rpcs = _rpcs.Values
                .OrderBy(static registration => registration.Descriptor.Method.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            var events = _events.Values
                .OrderBy(static registration => registration.Descriptor.Name.Value, StringComparer.Ordinal)
                .ToImmutableArray();

            // This is intentionally the only OpenRPC construction path for the frozen runtime catalog.
            var document = ProtocolDocumentBuilder.Create(
                _documentInfo,
                rpcs.Select(static registration => registration.Descriptor).ToImmutableArray(),
                events.Select(static registration => registration.Descriptor).ToImmutableArray());
            var documentUtf8 = JsonSerializer
                .SerializeToUtf8Bytes(document, BuiltInProtocolJsonContext.Default.OpenRpcDocument)
                .ToImmutableArray();

            var frozenRpcs = rpcs
                .Select(registration => new KeyValuePair<RpcMethod, FrozenRpcBinding>(
                    registration.Descriptor.Method,
                    new FrozenRpcBinding(registration.Owner, registration.Descriptor, _rpcBindings[registration.Descriptor.Method])))
                .ToFrozenDictionary(RpcMethodComparer.Instance);
            var frozenEvents = events
                .Select(registration => new KeyValuePair<EventName, FrozenEventBinding>(
                    registration.Descriptor.Name,
                    new FrozenEventBinding(registration.Owner, registration.Descriptor, _eventBindings[registration.Descriptor.Name])))
                .ToFrozenDictionary(EventNameComparer.Instance);

            var catalog = new FrozenProtocolCatalog(
                frozenRpcs,
                frozenEvents,
                rpcs.Select(static registration => registration.Descriptor).ToImmutableArray(),
                events.Select(static registration => registration.Descriptor).ToImmutableArray(),
                document,
                documentUtf8);
            Volatile.Write(ref _catalog, catalog);
            return catalog;
        }
    }

    private void ValidateBindings()
    {
        foreach (var (method, definition) in _rpcs)
        {
            if (!_rpcBindings.TryGetValue(method, out var binding))
            {
                throw new InvalidOperationException($"The RPC method '{method.Value}' does not have a daemon binding.");
            }

            ValidateBinding(definition, binding);
        }

        foreach (var method in _rpcBindings.Keys)
        {
            if (!_rpcs.ContainsKey(method))
            {
                throw new InvalidOperationException($"The RPC binding '{method.Value}' does not have a registered definition.");
            }
        }

        foreach (var (name, definition) in _events)
        {
            if (!_eventBindings.TryGetValue(name, out var binding))
            {
                throw new InvalidOperationException($"The event '{name.Value}' does not have a daemon binding.");
            }

            ValidateBinding(definition, binding);
        }

        foreach (var name in _eventBindings.Keys)
        {
            if (!_events.ContainsKey(name))
            {
                throw new InvalidOperationException($"The event binding '{name.Value}' does not have a registered definition.");
            }
        }
    }

    private static void ValidateBinding(ProtocolDefinition<RpcDescriptor> definition, RpcBinding binding)
    {
        if (!definition.Owner.Equals(binding.Owner))
        {
            throw new ArgumentException($"The RPC binding '{definition.Descriptor.Method.Value}' must have the same owner as its definition.");
        }

        binding.ValidateTypes(definition.Descriptor);
    }

    private static void ValidateBinding(ProtocolDefinition<EventDescriptor> definition, EventBinding binding)
    {
        if (!definition.Owner.Equals(binding.Owner))
        {
            throw new ArgumentException($"The event binding '{definition.Descriptor.Name.Value}' must have the same owner as its definition.");
        }

        binding.ValidateTypes(definition.Descriptor);
    }

    private static void ValidateRpcDescriptor(ProtocolExecutionOwner owner, RpcDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ProtocolCatalogNamePolicy.ValidateOwnedName(owner, descriptor.Method.Value, ProtocolCatalogEntryKind.Rpc);
        ValidateMetadata(descriptor.Permission, descriptor.RequestTypeInfo, descriptor.ResultTypeInfo, descriptor.Documentation);
    }

    private static void ValidateEventDescriptor(ProtocolExecutionOwner owner, EventDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ProtocolCatalogNamePolicy.ValidateOwnedName(owner, descriptor.Name.Value, ProtocolCatalogEntryKind.Event);
        ValidateMetadata(descriptor.Permission, descriptor.DataTypeInfo, descriptor.MetaTypeInfo, descriptor.Documentation);

        if (descriptor.DataPresence == OpenRpcEventFieldPresence.Omitted ||
            (descriptor.MetaPresence == OpenRpcEventFieldPresence.Omitted && descriptor.MetaTypeInfo is not null) ||
            (descriptor.MetaPresence != OpenRpcEventFieldPresence.Omitted && descriptor.MetaTypeInfo is null) ||
            (descriptor.Documentation!.MetaSchemaId is null) != (descriptor.MetaPresence == OpenRpcEventFieldPresence.Omitted))
        {
            throw new ArgumentException($"Event descriptor '{descriptor.Name.Value}' has inconsistent data, metadata, or documentation semantics.", nameof(descriptor));
        }
    }

    private static void ValidateMetadata(
        PermissionName permission,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo primaryTypeInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo? secondaryTypeInfo,
        object? documentation)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(primaryTypeInfo);
        if (primaryTypeInfo.Type is null || (secondaryTypeInfo is not null && secondaryTypeInfo.Type is null))
        {
            throw new ArgumentException("Protocol descriptors must use concrete JSON type metadata.");
        }

        if (documentation is null)
        {
            throw new ArgumentException("Protocol descriptors require documentation metadata.");
        }
    }

    private void EnsureNoOtherEntryKindName(string name, ProtocolCatalogEntryKind entryKind)
    {
        var hasCollision = entryKind switch
        {
            ProtocolCatalogEntryKind.Rpc =>
                _events.Keys.Any(candidate => StringComparer.Ordinal.Equals(candidate.Value, name)) ||
                _eventBindings.Keys.Any(candidate => StringComparer.Ordinal.Equals(candidate.Value, name)),
            ProtocolCatalogEntryKind.Event =>
                _rpcs.Keys.Any(candidate => StringComparer.Ordinal.Equals(candidate.Value, name)) ||
                _rpcBindings.Keys.Any(candidate => StringComparer.Ordinal.Equals(candidate.Value, name)),
            _ => throw new ArgumentOutOfRangeException(nameof(entryKind))
        };

        if (hasCollision)
        {
            throw new ArgumentException($"The wire name '{name}' is already used by the other protocol entry kind.", nameof(name));
        }
    }

    private void EnsureMutable()
    {
        if (_catalog is not null)
        {
            throw new InvalidOperationException("The protocol catalog has already been frozen.");
        }
    }

    private static void EnsureBuiltInBinding(RpcBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.Owner.Equals(ProtocolExecutionOwner.BuiltIn))
        {
            throw new ArgumentException("A built-in definition requires a built-in binding owner.", nameof(binding));
        }
    }

    private static void EnsureBuiltInBinding(EventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.Owner.Equals(ProtocolExecutionOwner.BuiltIn))
        {
            throw new ArgumentException("A built-in definition requires a built-in binding owner.", nameof(binding));
        }
    }

    private sealed record ProtocolDefinition<TDescriptor>(ProtocolExecutionOwner Owner, TDescriptor Descriptor);
}

internal static class ProtocolCatalogNamePolicy
{
    public static void ValidateOwnedName(
        ProtocolExecutionOwner owner,
        string name,
        ProtocolCatalogEntryKind entryKind)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var isAllowed = owner.Kind switch
        {
            ProtocolExecutionOwnerKind.BuiltIn => IsBuiltInName(name, entryKind),
            ProtocolExecutionOwnerKind.Plugin => owner.Plugin is not null &&
                                                 name.StartsWith(owner.GetOwnedNamespace(entryKind), StringComparison.Ordinal),
            _ => false
        };

        if (!isAllowed)
        {
            throw new ArgumentException(
                $"The {entryKind.ToString().ToLowerInvariant()} name '{name}' is outside owner '{owner.Kind}' namespace.",
                nameof(name));
        }
    }

    public static void EnsureDistinctWireNames(
        IEnumerable<RpcMethod> methods,
        IEnumerable<EventName> events)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(events);

        var names = new HashSet<string>(
            methods.Select(static method => method.Value),
            StringComparer.Ordinal);
        foreach (var @event in events)
        {
            if (!names.Add(@event.Value))
            {
                throw new ArgumentException($"The wire name '{@event.Value}' cannot identify both an RPC method and an event.");
            }
        }
    }

    private static bool IsBuiltInName(string name, ProtocolCatalogEntryKind entryKind) => entryKind switch
    {
        ProtocolCatalogEntryKind.Rpc => IsBuiltInRpcName(name),
        ProtocolCatalogEntryKind.Event => IsBuiltInEventName(name),
        _ => false
    };

    private static bool IsBuiltInRpcName(string name)
    {
        if (StringComparer.Ordinal.Equals(name, "rpc.discover"))
        {
            return true;
        }

        const string prefix = "mcsl.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || IsBuiltInEventName(name))
        {
            return false;
        }

        var suffix = name.AsSpan(prefix.Length);
        var separator = suffix.IndexOf('.');
        return separator > 0 && separator < suffix.Length - 1;
    }

    private static bool IsBuiltInEventName(string name)
    {
        const string prefix = "mcsl.event.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = name.AsSpan(prefix.Length);
        var separator = suffix.IndexOf('.');
        return separator > 0 && separator < suffix.Length - 1;
    }
}

internal sealed class FrozenProtocolCatalog(
    FrozenDictionary<RpcMethod, FrozenRpcBinding> rpcs,
    FrozenDictionary<EventName, FrozenEventBinding> events,
    ImmutableArray<RpcDescriptor> rpcDefinitions,
    ImmutableArray<EventDescriptor> eventDefinitions,
    OpenRpcDocument document,
    ImmutableArray<byte> documentUtf8)
{
    public FrozenDictionary<RpcMethod, FrozenRpcBinding> Rpcs { get; } = rpcs ?? throw new ArgumentNullException(nameof(rpcs));

    public FrozenDictionary<EventName, FrozenEventBinding> Events { get; } = events ?? throw new ArgumentNullException(nameof(events));

    public ImmutableArray<RpcDescriptor> RpcDefinitions { get; } = rpcDefinitions.IsDefault
        ? throw new ArgumentException("RPC definitions cannot be default.", nameof(rpcDefinitions))
        : rpcDefinitions;

    public ImmutableArray<EventDescriptor> EventDefinitions { get; } = eventDefinitions.IsDefault
        ? throw new ArgumentException("Event definitions cannot be default.", nameof(eventDefinitions))
        : eventDefinitions;

    public OpenRpcDocument Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

    public ImmutableArray<byte> DocumentUtf8 { get; } = documentUtf8.IsDefault
        ? throw new ArgumentException("OpenRPC UTF-8 bytes cannot be default.", nameof(documentUtf8))
        : documentUtf8;

    public bool TryGetRpc(RpcMethod method, out FrozenRpcBinding binding)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Rpcs.TryGetValue(method, out binding!);
    }

    public bool TryGetEvent(EventName name, out FrozenEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Events.TryGetValue(name, out binding!);
    }
}

internal sealed record FrozenRpcBinding(ProtocolExecutionOwner Owner, RpcDescriptor Descriptor, RpcBinding Binding);

internal sealed record FrozenEventBinding(ProtocolExecutionOwner Owner, EventDescriptor Descriptor, EventBinding Binding);

internal sealed class RpcMethodComparer : IEqualityComparer<RpcMethod>
{
    public static RpcMethodComparer Instance { get; } = new();

    public bool Equals(RpcMethod? x, RpcMethod? y) => StringComparer.Ordinal.Equals(x?.Value, y?.Value);

    public int GetHashCode(RpcMethod obj) => StringComparer.Ordinal.GetHashCode(obj?.Value ?? throw new ArgumentNullException(nameof(obj)));
}

internal sealed class EventNameComparer : IEqualityComparer<EventName>
{
    public static EventNameComparer Instance { get; } = new();

    public bool Equals(EventName? x, EventName? y) => StringComparer.Ordinal.Equals(x?.Value, y?.Value);

    public int GetHashCode(EventName obj) => StringComparer.Ordinal.GetHashCode(obj?.Value ?? throw new ArgumentNullException(nameof(obj)));
}
