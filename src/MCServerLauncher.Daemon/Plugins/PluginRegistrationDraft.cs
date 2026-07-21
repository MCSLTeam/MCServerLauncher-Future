using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginRegistrationDraft(
    PluginManifest manifest,
    ProtocolExecutionOwner owner,
    PluginErrorFactory errorFactory,
    IPluginEventBus eventBus,
    ILogger logger) : IPluginRpcRegistrar, IPluginEventRegistrar
{
    private readonly List<IPluginCatalogRegistration> _registrations = [];
    private PluginError? _invalidError;
    private bool _closed;
    private readonly IPluginEventBus _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    internal PluginManifest Manifest { get; } = manifest ?? throw new ArgumentNullException(nameof(manifest));

    internal ProtocolExecutionOwner Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));

    internal int RegistrationCount => _registrations.Count;

    internal bool IsInvalid => _invalidError is not null;

    public Result<Unit, DaemonError> Register<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        PluginRpcHandler<TRequest, TResult> handler)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (!EnsureWritable(out var stateError))
            return Result.Err<Unit, DaemonError>(stateError!);
        if (!Manifest.HasFeature(PluginFeature.RpcRegister))
        {
            return Error(
                "plugin_feature_required",
                $"Plugin '{Manifest.Identity.Id}' must declare feature 'rpc.register' before registering an RPC.");
        }

        try
        {
            var binding = new RpcBinding<TRequest, TResult>(
                Owner,
                async (_, request, cancellationToken) =>
                {
                    var result = await handler(request, cancellationToken).ConfigureAwait(false);
                    return result.IsOk(out var value)
                        ? ProtocolRpcExecution<TResult>.Ok(value)
                        : ProtocolRpcExecution<TResult>.Err(result.UnwrapErr());
                });
            _registrations.Add(new PluginRpcRegistration<TRequest, TResult>(Owner, descriptor, binding));
            return PluginResult.Ok();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Error("plugin_rpc_registration_invalid", exception.Message);
        }
    }

    public Result<IPluginEventPublisher<TData, TMeta>, DaemonError> Register<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!EnsureWritable(out var stateError))
            return Result.Err<IPluginEventPublisher<TData, TMeta>, DaemonError>(stateError!);
        if (!Manifest.HasFeature(PluginFeature.EventPublish))
        {
            return Error<IPluginEventPublisher<TData, TMeta>>(
                "plugin_feature_required",
                $"Plugin '{Manifest.Identity.Id}' must declare feature 'event.publish' before registering an event.");
        }

        var publisher = new PluginEventPublisher<TData, TMeta>(
            descriptor,
            _eventBus.Create(descriptor, errorFactory, _logger));
        _registrations.Add(new PluginEventRegistration<TData, TMeta>(Owner, descriptor, publisher));
        return Result.Ok<IPluginEventPublisher<TData, TMeta>, DaemonError>(publisher);
    }

    internal ImmutableArray<string> WireNames => _registrations
        .Select(static registration => registration.WireName)
        .ToImmutableArray();

    internal void Validate()
    {
        if (_invalidError is not null)
            throw new PluginErrorException(_invalidError);
        if (!_closed)
            throw new InvalidOperationException("The plugin registration draft must be closed before validation.");

        var validator = new ProtocolCatalogBuilder(new OpenRpcInfo("plugin-validation", Manifest.Identity.Version));
        try
        {
            foreach (var registration in _registrations)
                registration.AddTo(validator);
            _ = validator.Freeze();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new PluginErrorException(
                errorFactory.Create("plugin_catalog_invalid", exception.Message),
                exception);
        }
    }

    internal void AddTo(ProtocolCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var registration in _registrations)
            registration.AddTo(builder);
    }

    internal void Attach(
        FrozenProtocolCatalog catalog,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger eventOwner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(remoteEvents);
        foreach (var registration in _registrations)
            registration.Attach(catalog, remoteEvents, eventOwner);
    }

    internal void Clear()
    {
        _closed = true;
        foreach (var registration in _registrations)
            registration.Clear();
        _registrations.Clear();
    }

    internal void Close() => _closed = true;

    private bool EnsureWritable(out PluginError? error)
    {
        if (_closed)
        {
            error = Reject("plugin_registration_closed", "The plugin registration draft is already closed.");
            return false;
        }

        if (_invalidError is not null)
        {
            error = _invalidError;
            return false;
        }

        error = null;
        return true;
    }

    private Result<Unit, DaemonError> Error(string code, string message) =>
        PluginResult.Fail(Reject(code, message));

    private Result<TResult, DaemonError> Error<TResult>(string code, string message)
        where TResult : notnull =>
        PluginResult.Fail<TResult>(Reject(code, message));

    private PluginError Reject(string code, string message) =>
        _invalidError ??= errorFactory.Create(code, message);
}

internal interface IPluginCatalogRegistration
{
    string WireName { get; }

    void AddTo(ProtocolCatalogBuilder builder);

    void Attach(
        FrozenProtocolCatalog catalog,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger eventOwner);

    void Clear();
}

internal sealed class PluginRpcRegistration<TRequest, TResult>(
    ProtocolExecutionOwner owner,
    RpcDescriptor<TRequest, TResult> descriptor,
    RpcBinding<TRequest, TResult> binding) : IPluginCatalogRegistration
    where TResult : notnull
{
    public string WireName => descriptor.Method.Value;

    public void AddTo(ProtocolCatalogBuilder builder)
    {
        builder.AddRpcDefinition(owner, descriptor);
        builder.AddRpcBinding(descriptor.Method, binding);
    }

    public void Attach(
        FrozenProtocolCatalog catalog,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger eventOwner)
    {
        _ = catalog;
        _ = remoteEvents;
        _ = eventOwner;
    }

    public void Clear()
    {
    }
}

internal sealed class PluginEventRegistration<TData, TMeta>(
    ProtocolExecutionOwner owner,
    EventDescriptor<TData, TMeta> descriptor,
    PluginEventPublisher<TData, TMeta> publisher) : IPluginCatalogRegistration
{
    public string WireName => descriptor.Name.Value;

    public void AddTo(ProtocolCatalogBuilder builder)
    {
        EventBinding binding = descriptor.MetaTypeInfo is null
            ? new EventBinding<TData>(owner)
            : new EventBinding<TData, TMeta>(owner);
        builder.AddEventDefinition(owner, descriptor);
        builder.AddEventBinding(descriptor.Name, binding);
    }

    public void Attach(
        FrozenProtocolCatalog catalog,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger eventOwner)
    {
        if (!catalog.TryGetEvent(descriptor.Name, out var binding) ||
            !ReferenceEquals(binding.Descriptor, descriptor))
        {
            throw new InvalidOperationException($"The plugin event '{descriptor.Name.Value}' was not admitted into the frozen catalog.");
        }

        publisher.Attach(binding, remoteEvents, eventOwner);
    }

    public void Clear() => publisher.Clear();
}

internal sealed class PluginEventPublisher<TData, TMeta>(
    EventDescriptor<TData, TMeta> descriptor,
    PluginEventSlot<TData, TMeta> slot) : IPluginEventPublisher<TData, TMeta>
{
    public EventDescriptor<TData, TMeta> Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    private PluginEventSlot<TData, TMeta> Slot { get; } = slot ?? throw new ArgumentNullException(nameof(slot));

    public ValueTask<Result<Unit, DaemonError>> PublishAsync(
        DaemonEventField<TMeta> meta,
        DaemonEventField<TData> data,
        CancellationToken cancellationToken = default)
        => Slot.PublishAsync(meta, data, cancellationToken);

    internal void Attach(
        FrozenEventBinding binding,
        V2RemoteEventBridge remoteEvents,
        PluginEventOwnerLedger eventOwner)
    {
        Slot.Attach(binding, remoteEvents, eventOwner);
    }

    internal void Clear() => Slot.Clear();
}
