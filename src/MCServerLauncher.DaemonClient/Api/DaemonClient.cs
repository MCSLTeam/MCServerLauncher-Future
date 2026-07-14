using System;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly object _disposeGate = new();
    private readonly V2ClientConnectionOwner _owner;
    private Task? _disposeTask;

    public DaemonClient(DaemonClientOptions options)
        : this(CreateOwner(options))
    {
    }

    internal DaemonClient(V2ClientConnectionOwner owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        var invoker = new V2RemoteApplicationInvoker(owner);
        Instances = new RemoteInstanceApplication(invoker);
        Files = new RemoteFileApplication(invoker, owner);
        System = new RemoteSystemApplication(invoker);
        EventRules = new RemoteEventRuleApplication(invoker);
        InstanceCatalog = owner.Mirror;
    }

    public IInstanceApplication Instances { get; }

    public IFileApplication Files { get; }

    public ISystemApplication System { get; }

    public IEventRuleApplication EventRules { get; }

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
