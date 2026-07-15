using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginErrorFactory(PluginIdentity identity) : IPluginErrorFactory
{
    internal PluginIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));

    public PluginError Create(string code, string message, JsonElement? details = null) =>
        new(Identity, code, message, details);
}

internal sealed class PluginContext(
    PluginIdentity identity,
    ILogger logger,
    PluginErrorFactory errors,
    PluginRegistrationDraft registrations,
    IInstanceSnapshotSource instances,
    bool canQueryInstances,
    Task activation,
    CancellationToken lifetimeToken) : IPluginContext
{
    public PluginIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    public IPluginErrorFactory Errors { get; } = errors ?? throw new ArgumentNullException(nameof(errors));

    public IPluginRpcRegistrar Rpc { get; } = registrations ?? throw new ArgumentNullException(nameof(registrations));

    public IPluginEventRegistrar Events { get; } = registrations;

    public IInstanceSnapshotSource Instances { get; } = canQueryInstances
        ? instances ?? throw new ArgumentNullException(nameof(instances))
        : CapabilityDeniedSnapshotSource.Instance;

    public Task Activation { get; } = activation ?? throw new ArgumentNullException(nameof(activation));

    public CancellationToken LifetimeToken { get; } = lifetimeToken;
}

internal sealed class CapabilityDeniedSnapshotSource : IInstanceSnapshotSource
{
    internal static CapabilityDeniedSnapshotSource Instance { get; } = new();

    public PublishedState<InstanceCatalogSnapshot> Current =>
        throw new InvalidOperationException("The plugin did not declare the 'instance.query' capability.");

    public bool TryGet(Guid instanceId, out InstanceSnapshot snapshot)
    {
        _ = instanceId;
        snapshot = null!;
        throw new InvalidOperationException("The plugin did not declare the 'instance.query' capability.");
    }
}
