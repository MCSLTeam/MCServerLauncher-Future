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
        RpcDescriptor<TRequest, TResult> descriptor,
        PluginRpcHandler<TRequest, TResult> handler)
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

public interface IPluginContext
{
    PluginIdentity Identity { get; }

    ILogger Logger { get; }

    IPluginErrorFactory Errors { get; }

    IPluginRpcRegistrar Rpc { get; }

    IPluginEventRegistrar Events { get; }

    IInstanceSnapshotSource Instances { get; }

    /// <summary>
    /// System facts and Java discovery. Throws if the plugin did not declare <c>system.query</c>.
    /// </summary>
    ISystemQueryApplication System { get; }

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

    Task Activation { get; }

    CancellationToken LifetimeToken { get; }
}

public interface IDaemonPlugin
{
    Result<Unit, DaemonError> Configure(IPluginContext context);

    Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken);

    Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken);
}
