using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginErrorFactory(PluginIdentity identity) : IPluginErrorFactory
{
    internal PluginIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));

    public PluginError Create(string code, string message, JsonElement? details = null) =>
        new(Identity, code, message, details);
}

internal sealed class PluginApplicationAuthorizer
{
    private readonly CallerContextFactory _callerContexts;
    private readonly IInstanceSnapshotSource? _instanceCatalog;
    private readonly IInstanceQueryApplication? _instanceQueries;
    private readonly ISystemQueryApplication? _system;
    private readonly IInstanceManagementApplication? _instanceManagement;
    private readonly IOperationQueryApplication? _operationQueries;
    private readonly IOperationControlApplication? _operationControl;
    private readonly IProvisioningApplication? _provisioning;

    internal PluginApplicationAuthorizer(
        PluginIdentity identity,
        IEnumerable<string> grantedFeatureIds,
        CallerContextFactory callerContexts,
        IInstanceSnapshotSource? instanceCatalog,
        IInstanceQueryApplication? instanceQueries,
        ISystemQueryApplication? system,
        IInstanceManagementApplication? instanceManagement,
        IOperationQueryApplication? operationQueries,
        IOperationControlApplication? operationControl,
        IProvisioningApplication? provisioning)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(grantedFeatureIds);
        var grantedFeatures = grantedFeatureIds.ToHashSet(StringComparer.Ordinal);
        _callerContexts = (callerContexts ?? throw new ArgumentNullException(nameof(callerContexts)))
            .ForPlugin(identity);
        _instanceCatalog = HasFeature(PluginFeature.InstanceQuery) ? instanceCatalog : null;
        _instanceQueries = HasFeature(PluginFeature.InstanceQuery) ? instanceQueries : null;
        _system = HasFeature(PluginFeature.SystemQuery) ? system : null;
        _instanceManagement = HasFeature(PluginFeature.InstanceManage) ? instanceManagement : null;
        _operationQueries = HasFeature(PluginFeature.OperationQuery) ? operationQueries : null;
        _operationControl = HasFeature(PluginFeature.OperationCancel) ? operationControl : null;
        _provisioning = HasFeature(PluginFeature.ProvisioningManage) ? provisioning : null;
        Host = Create(_callerContexts.CreateHost(identity, grantedFeatures));

        bool HasFeature(PluginFeature feature) => grantedFeatures.Contains(feature.Value);
    }

    internal IPluginAuthorizedApplications Host { get; }

    internal IPluginAuthorizedApplications ForPrincipal(VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Create(_callerContexts.ForPrincipal(principal));
    }

    private IPluginAuthorizedApplications Create(ICallerContext caller) =>
        new PluginAuthorizedApplications(
            caller,
            _instanceCatalog is null ? null : new AuthorizedInstanceCatalog(caller, _instanceCatalog),
            _instanceQueries is null ? null : new AuthorizedInstanceQueryApplication(caller, _instanceQueries),
            _system is null ? null : new AuthorizedSystemQueryApplication(caller, _system),
            _instanceManagement is null ? null : new AuthorizedInstanceManagementApplication(caller, _instanceManagement),
            _operationQueries is null ? null : new AuthorizedOperationQueryApplication(caller, _operationQueries),
            _operationControl is null ? null : new AuthorizedOperationControlApplication(caller, _operationControl),
            _provisioning is null ? null : new AuthorizedProvisioningApplication(caller, _provisioning));
}

internal sealed class PluginAuthorizedApplications(
    ICallerContext caller,
    IInstanceSnapshotSource? instanceCatalog,
    IInstanceQueryApplication? instanceQueries,
    ISystemQueryApplication? system,
    IInstanceManagementApplication? instanceManagement,
    IOperationQueryApplication? operationQueries,
    IOperationControlApplication? operationControl,
    IProvisioningApplication? provisioning) : IPluginAuthorizedApplications
{
    public ICallerContext Caller { get; } = caller ?? throw new ArgumentNullException(nameof(caller));

    public IInstanceSnapshotSource InstanceCatalog =>
        instanceCatalog ?? throw MissingFeature("instance.query");

    public IInstanceQueryApplication InstanceQueries =>
        instanceQueries ?? throw MissingFeature("instance.query");

    public ISystemQueryApplication System =>
        system ?? throw MissingFeature("system.query");

    public IInstanceManagementApplication InstanceManagement =>
        instanceManagement ?? throw MissingFeature("instance.manage");

    public IOperationQueryApplication OperationQueries =>
        operationQueries ?? throw MissingFeature("operation.query");

    public IOperationControlApplication OperationControl =>
        operationControl ?? throw MissingFeature("operation.cancel");

    public IProvisioningApplication Provisioning =>
        provisioning ?? throw MissingFeature("provisioning.manage");

    private static InvalidOperationException MissingFeature(string feature) =>
        new($"The plugin did not declare the '{feature}' feature.");
}

internal sealed class PluginContext : IPluginContext
{
    private readonly IPluginPrivateStorage? _storage;
    private readonly IPluginHttpEndpointPolicy? _httpEndpoints;
    private readonly IPluginAuthentication? _authentication;
    private readonly PluginApplicationAuthorizer _applications;

    internal PluginContext(
        PluginIdentity identity,
        ILogger logger,
        PluginErrorFactory errors,
        PluginRegistrationDraft registrations,
        PluginApplicationAuthorizer applications,
        IPluginConfiguration configuration,
        IPluginPrivateStorage? storage,
        IPluginHttpEndpointPolicy? httpEndpoints,
        IPluginAuthentication? authentication,
        Task activation,
        CancellationToken lifetimeToken)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        Rpc = registrations ?? throw new ArgumentNullException(nameof(registrations));
        Events = registrations;
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _storage = storage;
        _httpEndpoints = httpEndpoints;
        _authentication = authentication;
        Activation = activation ?? throw new ArgumentNullException(nameof(activation));
        LifetimeToken = lifetimeToken;
    }

    public PluginIdentity Identity { get; }

    public ILogger Logger { get; }

    public IPluginErrorFactory Errors { get; }

    public IPluginRpcRegistrar Rpc { get; }

    public IPluginEventRegistrar Events { get; }

    public ICallerContext Caller => _applications.Host.Caller;

    public IInstanceSnapshotSource InstanceCatalog => _applications.Host.InstanceCatalog;

    public IInstanceQueryApplication InstanceQueries => _applications.Host.InstanceQueries;

    public IPluginConfiguration Configuration { get; }

    public IPluginPrivateStorage Storage =>
        _storage ?? throw new InvalidOperationException("The plugin did not declare the 'storage.private' feature.");

    public IPluginHttpEndpointPolicy HttpEndpoints =>
        _httpEndpoints ?? throw new InvalidOperationException("The plugin did not declare the 'network.http.listen' feature.");

    public IPluginAuthentication Authentication =>
        _authentication ?? throw new InvalidOperationException("The plugin did not declare the 'auth.verify' feature.");

    public ISystemQueryApplication System => _applications.Host.System;

    public IInstanceManagementApplication InstanceManagement => _applications.Host.InstanceManagement;

    public IOperationQueryApplication OperationQueries => _applications.Host.OperationQueries;

    public IOperationControlApplication OperationControl => _applications.Host.OperationControl;

    public IProvisioningApplication Provisioning => _applications.Host.Provisioning;

    public IPluginAuthorizedApplications ForPrincipal(VerifiedPrincipal principal) =>
        _applications.ForPrincipal(principal);

    public Task Activation { get; }

    public CancellationToken LifetimeToken { get; }
}
