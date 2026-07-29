using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Audit;
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
    private readonly string _pluginId;
    private readonly IAuditSink? _auditSink;
    private readonly IInstanceSnapshotSource? _instanceCatalog;
    private readonly IInstanceQueryApplication? _instanceQueries;
    private readonly ISystemQueryApplication? _system;
    private readonly IInstanceManagementApplication? _instanceManagement;
    private readonly IOperationQueryApplication? _operationQueries;
    private readonly IOperationControlApplication? _operationControl;
    private readonly IProvisioningApplication? _provisioning;
    private readonly IBackupApplication? _backups;
    private readonly IAuditApplication? _audit;
    private readonly IMonitoringApplication? _monitoring;
    private readonly IAutomationApplication? _automation;
    private readonly IEventRuleApplication? _eventRules;
    private readonly ContainedPluginFileApplication? _containedFiles;
    private readonly IFileReadApplication? _fileReads;
    private readonly IFileWriteApplication? _fileWrites;

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
        IProvisioningApplication? provisioning,
        IBackupApplication? backups,
        IAuditApplication? audit,
        IMonitoringApplication? monitoring,
        IAutomationApplication? automation,
        IEventRuleApplication? eventRules,
        IFileApplication? files,
        IAuditSink? auditSink = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(grantedFeatureIds);
        var grantedFeatures = grantedFeatureIds.ToHashSet(StringComparer.Ordinal);
        _pluginId = identity.Id;
        _auditSink = auditSink;
        _callerContexts = (callerContexts ?? throw new ArgumentNullException(nameof(callerContexts)))
            .ForPlugin(identity);
        _instanceCatalog = HasFeature(PluginFeature.InstanceQuery) ? instanceCatalog : null;
        _instanceQueries = HasFeature(PluginFeature.InstanceQuery) ? instanceQueries : null;
        _system = HasFeature(PluginFeature.SystemQuery) ? system : null;
        _instanceManagement = HasFeature(PluginFeature.InstanceManage) ? instanceManagement : null;
        _operationQueries = HasFeature(PluginFeature.OperationQuery) ? operationQueries : null;
        _operationControl = HasFeature(PluginFeature.OperationCancel) ? operationControl : null;
        _provisioning = HasFeature(PluginFeature.ProvisioningManage) ? provisioning : null;
        _backups = HasFeature(PluginFeature.BackupManage) ? backups : null;
        _audit = HasFeature(PluginFeature.AuditQuery) ? audit : null;
        _monitoring = HasFeature(PluginFeature.MonitoringQuery) ? monitoring : null;
        _automation = HasFeature(PluginFeature.AutomationManage) ? automation : null;
        _eventRules = HasFeature(PluginFeature.EventRuleManage) ? eventRules : null;
        // One file application feeds both narrow views, mirroring how one instance application
        // feeds the query and management slots. It is confined first: the shared application is
        // rooted at the whole daemon data root, which also holds the audit, plan, automation,
        // backup, operation and monitoring stores. Containment wraps the singleton rather than the
        // per-caller proxies so a plugin acting for a foreign principal is confined too — the
        // plugin code is the untrusted party regardless of whose token it presents.
        //
        // Containment consults the ungated catalog, not the feature-gated _instanceCatalog: whether
        // a path belongs to a real instance is a property of the daemon, not of what this plugin was
        // granted, and a plugin holding only the file features must still be confined to it.
        _containedFiles = files is null ||
                          !(HasFeature(PluginFeature.FileRead) || HasFeature(PluginFeature.FileWrite))
            ? null
            : new ContainedPluginFileApplication(files, instanceCatalog);
        _fileReads = HasFeature(PluginFeature.FileRead) ? _containedFiles : null;
        _fileWrites = HasFeature(PluginFeature.FileWrite) ? _containedFiles : null;
        Host = Create(_callerContexts.CreateHost(identity, grantedFeatures));

        bool HasFeature(PluginFeature feature) => grantedFeatures.Contains(feature.Value);
    }

    internal IPluginAuthorizedApplications Host { get; }

    internal Task ReleaseFileSessionsAsync() =>
        _containedFiles?.ReleaseSessionsAsync() ?? Task.CompletedTask;

    internal IPluginAuthorizedApplications ForPrincipal(VerifiedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Create(_callerContexts.ForPrincipal(principal));
    }

    private IPluginAuthorizedApplications Create(ICallerContext caller) =>
        new PluginAuthorizedApplications(
            caller,
            _instanceCatalog is null ? null : Audited<IInstanceSnapshotSource>(
                new AuthorizedInstanceCatalog(caller, _instanceCatalog),
                caller,
                static (inner, _, _, _) => new AuditedInstanceCatalog(inner)),
            _instanceQueries is null ? null : Audited<IInstanceQueryApplication>(
                new AuthorizedInstanceQueryApplication(caller, _instanceQueries),
                caller,
                static (inner, c, p, s) => new AuditedInstanceQueryApplication(inner, c, p, s)),
            _system is null ? null : Audited<ISystemQueryApplication>(
                new AuthorizedSystemQueryApplication(caller, _system),
                caller,
                static (inner, c, p, s) => new AuditedSystemQueryApplication(inner, c, p, s)),
            _instanceManagement is null ? null : Audited<IInstanceManagementApplication>(
                new AuthorizedInstanceManagementApplication(caller, _instanceManagement),
                caller,
                static (inner, c, p, s) => new AuditedInstanceManagementApplication(inner, c, p, s)),
            _operationQueries is null ? null : Audited<IOperationQueryApplication>(
                new AuthorizedOperationQueryApplication(caller, _operationQueries),
                caller,
                static (inner, c, p, s) => new AuditedOperationQueryApplication(inner, c, p, s)),
            _operationControl is null ? null : Audited<IOperationControlApplication>(
                new AuthorizedOperationControlApplication(caller, _operationControl),
                caller,
                static (inner, c, p, s) => new AuditedOperationControlApplication(inner, c, p, s)),
            _provisioning is null ? null : Audited<IProvisioningApplication>(
                new AuthorizedProvisioningApplication(caller, _provisioning),
                caller,
                static (inner, c, p, s) => new AuditedProvisioningApplication(inner, c, p, s)),
            _backups is null ? null : Audited<IBackupApplication>(
                new AuthorizedBackupApplication(caller, _backups),
                caller,
                static (inner, c, p, s) => new AuditedBackupApplication(inner, c, p, s)),
            _audit is null ? null : Audited<IAuditApplication>(
                new AuthorizedAuditApplication(caller, _audit),
                caller,
                static (inner, c, p, s) => new AuditedAuditApplication(inner, c, p, s)),
            _monitoring is null ? null : Audited<IMonitoringApplication>(
                new AuthorizedMonitoringApplication(caller, _monitoring),
                caller,
                static (inner, c, p, s) => new AuditedMonitoringApplication(inner, c, p, s)),
            _automation is null ? null : Audited<IAutomationApplication>(
                new AuthorizedAutomationApplication(caller, _automation),
                caller,
                static (inner, c, p, s) => new AuditedAutomationApplication(inner, c, p, s)),
            _eventRules is null ? null : Audited<IEventRuleApplication>(
                new AuthorizedEventRuleApplication(caller, _eventRules),
                caller,
                static (inner, c, p, s) => new AuditedEventRuleApplication(inner, c, p, s)),
            _fileReads is null ? null : Audited<IFileReadApplication>(
                new AuthorizedFileReadApplication(caller, _fileReads),
                caller,
                static (inner, c, p, s) => new AuditedFileReadApplication(inner, c, p, s)),
            _fileWrites is null ? null : Audited<IFileWriteApplication>(
                new AuthorizedFileWriteApplication(caller, _fileWrites),
                caller,
                static (inner, c, p, s) => new AuditedFileWriteApplication(inner, c, p, s)));

    /// <summary>
    /// Wraps an already-permission-checked proxy with its audit decorator, only when a sink is
    /// configured - absent a sink, behavior is exactly what it was before this boundary existed.
    /// </summary>
    private TInterface Audited<TInterface>(
        TInterface authorized,
        ICallerContext caller,
        Func<TInterface, ICallerContext, string, IAuditSink, TInterface> decorate)
        where TInterface : class =>
        _auditSink is null ? authorized : decorate(authorized, caller, _pluginId, _auditSink);
}

internal sealed class PluginAuthorizedApplications(
    ICallerContext caller,
    IInstanceSnapshotSource? instanceCatalog,
    IInstanceQueryApplication? instanceQueries,
    ISystemQueryApplication? system,
    IInstanceManagementApplication? instanceManagement,
    IOperationQueryApplication? operationQueries,
    IOperationControlApplication? operationControl,
    IProvisioningApplication? provisioning,
    IBackupApplication? backups,
    IAuditApplication? audit,
    IMonitoringApplication? monitoring,
    IAutomationApplication? automation,
    IEventRuleApplication? eventRules,
    IFileReadApplication? fileReads,
    IFileWriteApplication? fileWrites) : IPluginAuthorizedApplications
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

    public IBackupApplication Backups =>
        backups ?? throw MissingFeature("backup.manage");

    public IAuditApplication Audit =>
        audit ?? throw MissingFeature("audit.query");

    public IMonitoringApplication Monitoring =>
        monitoring ?? throw MissingFeature("monitoring.query");

    public IAutomationApplication Automation =>
        automation ?? throw MissingFeature("automation.manage");

    public IEventRuleApplication EventRules =>
        eventRules ?? throw MissingFeature("event-rule.manage");

    public IFileReadApplication FileReads =>
        fileReads ?? throw MissingFeature("file.read");

    public IFileWriteApplication FileWrites =>
        fileWrites ?? throw MissingFeature("file.write");

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

    /// <summary>
    /// Revokes the plugin's file capability and closes every session it still holds. The host calls
    /// it once the plugin's stop attempt has terminated, so a crashed, throwing or hanging plugin
    /// cannot pin file handles and staging bytes until session expiry.
    /// </summary>
    internal Task ReleaseFileSessionsAsync() => _applications.ReleaseFileSessionsAsync();

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

    public IBackupApplication Backups => _applications.Host.Backups;

    public IAuditApplication Audit => _applications.Host.Audit;

    public IMonitoringApplication Monitoring => _applications.Host.Monitoring;

    public IAutomationApplication Automation => _applications.Host.Automation;

    public IEventRuleApplication EventRules => _applications.Host.EventRules;

    public IFileReadApplication FileReads => _applications.Host.FileReads;

    public IFileWriteApplication FileWrites => _applications.Host.FileWrites;

    public IPluginAuthorizedApplications ForPrincipal(VerifiedPrincipal principal) =>
        _applications.ForPrincipal(principal);

    public Task Activation { get; }

    public CancellationToken LifetimeToken { get; }
}
