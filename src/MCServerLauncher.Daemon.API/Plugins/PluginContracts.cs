using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

public delegate Task<Result<TResult, DaemonError>> PluginRpcHandler<TRequest, TResult>(
    TRequest request,
    CancellationToken cancellationToken)
    where TResult : notnull;

public interface IPluginRpcRegistrar
{
    Result<Unit, DaemonError> Register<TRequest, TResult>(
        string relativeMethod,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        RpcDocumentation documentation,
        PluginRpcHandler<TRequest, TResult> handler,
        bool allowNotification = false)
        where TResult : notnull;
}

public interface IPluginEventRegistrar
{
    Result<IPluginEventPublisher<TData, TMeta>, DaemonError> Register<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor);
}

public interface IPluginEventPublisher<TData, TMeta>
{
    EventDescriptor<TData, TMeta> Descriptor { get; }

    ValueTask<Result<Unit, DaemonError>> PublishAsync(
        DaemonEventField<TMeta> meta,
        DaemonEventField<TData> data,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Granted application surfaces bound to one explicit caller. Every property returns an
/// authorization proxy; the host never exposes the underlying application implementation.
/// </summary>
public interface IPluginAuthorizedApplications
{
    ICallerContext Caller { get; }

    IInstanceSnapshotSource InstanceCatalog { get; }

    IInstanceQueryApplication InstanceQueries { get; }

    ISystemQueryApplication System { get; }

    IInstanceManagementApplication InstanceManagement { get; }

    IOperationQueryApplication OperationQueries { get; }

    IOperationControlApplication OperationControl { get; }

    IProvisioningApplication Provisioning { get; }
}

public interface IPluginContext : IPluginAuthorizedApplications
{
    PluginIdentity Identity { get; }

    ILogger Logger { get; }

    IPluginErrorFactory Errors { get; }

    IPluginRpcRegistrar Rpc { get; }

    IPluginEventRegistrar Events { get; }

    /// <summary>
    /// Cold-start configuration reader (base API; not a feature).
    /// </summary>
    IPluginConfiguration Configuration { get; }

    /// <summary>
    /// Plugin-private storage. Throws if the plugin did not declare <c>storage.private</c>.
    /// </summary>
    IPluginPrivateStorage Storage { get; }

    /// <summary>
    /// HTTP endpoint policy. Throws if the plugin did not declare <c>network.http.listen</c>.
    /// </summary>
    IPluginHttpEndpointPolicy HttpEndpoints { get; }

    /// <summary>
    /// Token verification. Throws if the plugin did not declare <c>auth.verify</c>.
    /// </summary>
    IPluginAuthentication Authentication { get; }

    /// <summary>
    /// Returns the same granted feature set bound to a verified user principal.
    /// The returned applications remain authorization proxies and never expose host internals.
    /// </summary>
    IPluginAuthorizedApplications ForPrincipal(VerifiedPrincipal principal);

    Task Activation { get; }

    CancellationToken LifetimeToken { get; }
}

public interface IDaemonPlugin
{
    Result<Unit, DaemonError> Configure(IPluginContext context);

    Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Marker contract for source-generated daemon plugin adapters. The host reads the generated
/// assembly metadata before loading or constructing the adapter or developer module.
/// </summary>
public interface IGeneratedDaemonPluginAdapter : IDaemonPlugin
{
}
