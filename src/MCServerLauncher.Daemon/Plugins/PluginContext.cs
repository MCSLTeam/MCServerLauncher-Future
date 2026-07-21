using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginErrorFactory(PluginIdentity identity) : IPluginErrorFactory
{
    internal PluginIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));

    public PluginError Create(string code, string message, JsonElement? details = null) =>
        new(Identity, code, message, details);
}

internal sealed class PluginContext : IPluginContext
{
    private readonly IPluginPrivateStorage? _storage;
    private readonly IPluginHttpEndpointPolicy? _httpEndpoints;
    private readonly IPluginAuthentication? _authentication;
    private readonly ISystemQueryApplication? _system;

    internal PluginContext(
        PluginIdentity identity,
        ILogger logger,
        PluginErrorFactory errors,
        PluginRegistrationDraft registrations,
        IInstanceSnapshotSource instances,
        bool canQueryInstances,
        IPluginConfiguration configuration,
        IPluginPrivateStorage? storage,
        IPluginHttpEndpointPolicy? httpEndpoints,
        IPluginAuthentication? authentication,
        ISystemQueryApplication? system,
        Task activation,
        CancellationToken lifetimeToken)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        Rpc = registrations ?? throw new ArgumentNullException(nameof(registrations));
        Events = registrations;
        Instances = canQueryInstances
            ? instances ?? throw new ArgumentNullException(nameof(instances))
            : FeatureDeniedSnapshotSource.Instance;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _storage = storage;
        _httpEndpoints = httpEndpoints;
        _authentication = authentication;
        _system = system;
        Activation = activation ?? throw new ArgumentNullException(nameof(activation));
        LifetimeToken = lifetimeToken;
    }

    public PluginIdentity Identity { get; }

    public ILogger Logger { get; }

    public IPluginErrorFactory Errors { get; }

    public IPluginRpcRegistrar Rpc { get; }

    public IPluginEventRegistrar Events { get; }

    public IInstanceSnapshotSource Instances { get; }

    public IPluginConfiguration Configuration { get; }

    public IPluginPrivateStorage Storage =>
        _storage ?? throw new InvalidOperationException("The plugin did not declare the 'storage.private' feature.");

    public IPluginHttpEndpointPolicy HttpEndpoints =>
        _httpEndpoints ?? throw new InvalidOperationException("The plugin did not declare the 'network.http.listen' feature.");

    public IPluginAuthentication Authentication =>
        _authentication ?? throw new InvalidOperationException("The plugin did not declare the 'auth.verify' feature.");

    public ISystemQueryApplication System =>
        _system ?? throw new InvalidOperationException("The plugin did not declare the 'system.query' feature.");

    public Task Activation { get; }

    public CancellationToken LifetimeToken { get; }
}

internal sealed class FeatureDeniedSnapshotSource : IInstanceSnapshotSource
{
    internal static FeatureDeniedSnapshotSource Instance { get; } = new();

    public PublishedState<InstanceCatalogSnapshot> Current =>
        throw new InvalidOperationException("The plugin did not declare the 'instance.query' feature.");

    public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot)
    {
        _ = instanceId;
        snapshot = null;
        throw new InvalidOperationException("The plugin did not declare the 'instance.query' feature.");
    }
}
