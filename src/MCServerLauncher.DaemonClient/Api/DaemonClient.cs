using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.DaemonClient.Application;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.DaemonClient;

public sealed class DaemonClient : IDaemonApplication, IAsyncDisposable
{
    private static readonly TimeSpan RestartPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestartTerminalTimeout = TimeSpan.FromSeconds(30);
    private readonly object _disposeGate = new();
    private readonly V2RemoteApplicationInvoker _invoker;
    private readonly V2ClientConnectionOwner _owner;
    private readonly TimeProvider _timeProvider;
    private Task? _disposeTask;

    public DaemonClient(DaemonClientOptions options)
        : this(CreateOwner(options), TimeProvider.System)
    {
    }

    internal DaemonClient(V2ClientConnectionOwner owner)
        : this(owner, TimeProvider.System)
    {
    }

    internal DaemonClient(V2ClientConnectionOwner owner, TimeProvider timeProvider)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _invoker = new V2RemoteApplicationInvoker(owner);
        Instances = new RemoteInstanceApplication(_invoker);
        Files = new RemoteFileApplication(_invoker, owner);
        System = new RemoteSystemApplication(_invoker);
        EventRules = new RemoteEventRuleApplication(_invoker);
        Operations = new RemoteOperationApplication(_invoker);
        Provisioning = new RemoteProvisioningApplication(_invoker);
        InstanceCatalog = owner.Mirror;
    }

    public IInstanceApplication Instances { get; }

    public IFileApplication Files { get; }

    public ISystemApplication System { get; }

    public IEventRuleApplication EventRules { get; }

    public IOperationApplication Operations { get; }

    public IProvisioningApplication Provisioning { get; }

    public IInstanceSnapshotSource InstanceCatalog { get; }

    public DaemonConnectionState ConnectionState => _owner.ConnectionState;

    public DaemonError? LastFailure => _owner.LastFailure;

    public event Func<DaemonConnectionState, Task> StateChanged
    {
        add => _owner.StateChanged += value;
        remove => _owner.StateChanged -= value;
    }

    public Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken = default) =>
        _owner.ConnectAsync(cancellationToken);

    public Task<Result<PingResult, DaemonError>> PingAsync(CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.PingDaemon, new EmptyRequest(), cancellationToken);

    public Task<Result<OpenRpcDocument, DaemonError>> DiscoverAsync(
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.DiscoverRpc, new EmptyRequest(), cancellationToken);

    public Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
        RpcDescriptor<TRequest, TResult> descriptor,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TResult : notnull =>
        _invoker.InvokeAsync(descriptor, request, cancellationToken);

    public async Task<Result<Unit, DaemonError>> RestartInstanceAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var instance = new InstanceReference(instanceId);
        var stopResult = await Instances.StopInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
        if (stopResult.IsErr(out var stopError))
            return Result.Err<Unit, DaemonError>(stopError!);

        using var timeout = new CancellationTokenSource(RestartTerminalTimeout, _timeProvider);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                var reportResult = await Instances.GetInstanceReportAsync(instance, wait.Token).ConfigureAwait(false);
                if (reportResult.IsErr(out var reportError))
                    return Result.Err<Unit, DaemonError>(reportError!);

                var status = reportResult.Unwrap().Status;
                if (status is InstanceStatus.Stopped or InstanceStatus.Crashed)
                    break;

                await Task.Delay(RestartPollInterval, _timeProvider, wait.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Result.Err<Unit, DaemonError>(new ConflictDaemonError(
                "instance.restart_timeout",
                "The instance did not reach a terminal state before the restart timeout."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Instances.StartInstanceAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DaemonEventSubscription, DaemonError>> SubscribeAsync<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventFilter<TMeta> filter,
        Func<DaemonEvent<TData, TMeta>, Task> callback,
        CancellationToken cancellationToken = default)
    {
        var result = await _owner.Subscriptions.SubscribeAsync(
            descriptor,
            filter,
            callback,
            cancellationToken).ConfigureAwait(false);
        return result.IsOk(out var handle)
            ? Result.Ok<DaemonEventSubscription, DaemonError>(new DaemonEventSubscription(handle!))
            : Result.Err<DaemonEventSubscription, DaemonError>(result.UnwrapErr());
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private static V2ClientConnectionOwner CreateOwner(DaemonClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var factory = new TouchSocketV2ClientConnectionSessionFactory(
            options.Endpoint,
            options.Token,
            TimeProvider.System,
            options.RequestTimeout);
        return new V2ClientConnectionOwner(factory, TimeProvider.System, options.ReconnectDelay);
    }

    private async Task DisposeCoreAsync()
    {
        await _owner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
