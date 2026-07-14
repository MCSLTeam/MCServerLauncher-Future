using System;
using System.Threading;
using System.Threading.Tasks;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient;
using RustyOptions;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Services;

internal interface ITypedDaemonEventSource
{
    Task<Result<IAsyncDisposable, DaemonError>> SubscribeAsync<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventFilter<TMeta> filter,
        Func<DaemonEvent<TData, TMeta>, Task> callback,
        CancellationToken cancellationToken = default);
}

internal sealed class DaemonClientEventSource : ITypedDaemonEventSource
{
    private readonly TypedDaemonClient _client;

    internal DaemonClientEventSource(TypedDaemonClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<Result<IAsyncDisposable, DaemonError>> SubscribeAsync<TData, TMeta>(
        EventDescriptor<TData, TMeta> descriptor,
        DaemonEventFilter<TMeta> filter,
        Func<DaemonEvent<TData, TMeta>, Task> callback,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.SubscribeAsync(descriptor, filter, callback, cancellationToken).ConfigureAwait(false);
        return result.IsOk(out var subscription)
            ? Result.Ok<IAsyncDisposable, DaemonError>(subscription!)
            : Result.Err<IAsyncDisposable, DaemonError>(result.UnwrapErr());
    }
}

internal sealed class DaemonEventBridge : IAsyncDisposable
{
    private static readonly AsyncLocal<DeliveryScope?> CurrentDelivery = new();
    private readonly object _gate = new();
    private readonly Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task> _logCallback;
    private readonly Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task> _notificationCallback;
    private IAsyncDisposable? _logSubscription;
    private IAsyncDisposable? _notificationSubscription;
    private TaskCompletionSource<object?>? _drainCompletion;
    private Task? _disposeTask;
    private int _inFlightDeliveryCount;
    private bool _disposed;

    private DaemonEventBridge(
        Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task> logCallback,
        Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task> notificationCallback)
    {
        _logCallback = logCallback;
        _notificationCallback = notificationCallback;
    }

    internal static Task<Result<DaemonEventBridge, DaemonError>> CreateAsync(
        TypedDaemonClient client,
        Guid instanceId,
        Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task> logCallback,
        Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task> notificationCallback,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            new DaemonClientEventSource(client),
            instanceId,
            logCallback,
            notificationCallback,
            cancellationToken);

    internal static async Task<Result<DaemonEventBridge, DaemonError>> CreateAsync(
        ITypedDaemonEventSource source,
        Guid instanceId,
        Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task> logCallback,
        Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task> notificationCallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(logCallback);
        ArgumentNullException.ThrowIfNull(notificationCallback);

        var bridge = new DaemonEventBridge(logCallback, notificationCallback);
        try
        {
            var logResult = await source.SubscribeAsync(
                BuiltInProtocolDefinitions.InstanceLog,
                DaemonEventFilter<InstanceLogEventMeta>.Exact(new InstanceLogEventMeta(instanceId)),
                bridge.OnLogAsync,
                cancellationToken).ConfigureAwait(false);
            if (logResult.IsErr(out var logError))
                return Result.Err<DaemonEventBridge, DaemonError>(logError!);

            bridge._logSubscription = logResult.Unwrap();
            var notificationResult = await source.SubscribeAsync(
                BuiltInProtocolDefinitions.Notification,
                DaemonEventFilter<NotificationEventMeta>.Wildcard,
                bridge.OnNotificationAsync,
                cancellationToken).ConfigureAwait(false);
            if (notificationResult.IsErr(out var notificationError))
            {
                await bridge.DisposeAfterFailedCreateAsync().ConfigureAwait(false);
                return Result.Err<DaemonEventBridge, DaemonError>(notificationError!);
            }

            bridge._notificationSubscription = notificationResult.Unwrap();
            return Result.Ok<DaemonEventBridge, DaemonError>(bridge);
        }
        catch
        {
            await bridge.DisposeAfterFailedCreateAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static InfoBarSeverity MapNotificationSeverity(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "success" => InfoBarSeverity.Success,
            "warning" => InfoBarSeverity.Warning,
            "error" => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        Task drainTask = Task.CompletedTask;
        TaskCompletionSource<object?>? disposeCompletion = null;
        IAsyncDisposable? logSubscription = null;
        IAsyncDisposable? notificationSubscription = null;

        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                disposeCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = disposeCompletion.Task;
                logSubscription = _logSubscription;
                notificationSubscription = _notificationSubscription;

                if (_inFlightDeliveryCount > 0)
                {
                    _drainCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    drainTask = _drainCompletion.Task;
                }
            }

            disposeTask = _disposeTask;
        }

        if (disposeCompletion is not null)
        {
            _ = CompleteDisposalAsync(
                drainTask,
                logSubscription,
                notificationSubscription,
                disposeCompletion);
        }

        // A reentrant call only initiates disposal; waiting for the full drain here would wait on itself.
        // Non-delivery callers always observe the cached task that includes drain and handle cleanup.
        return IsInsideActiveDelivery() ? ValueTask.CompletedTask : new ValueTask(disposeTask);
    }

    private static async Task CompleteDisposalAsync(
        Task drainTask,
        IAsyncDisposable? logSubscription,
        IAsyncDisposable? notificationSubscription,
        TaskCompletionSource<object?> disposeCompletion)
    {
        try
        {
            await drainTask.ConfigureAwait(false);
            await DisposeSubscriptionsAsync(logSubscription, notificationSubscription).ConfigureAwait(false);
            disposeCompletion.TrySetResult(null);
        }
        catch (Exception ex)
        {
            disposeCompletion.TrySetException(ex);
        }
    }

    private static async Task DisposeSubscriptionsAsync(
        IAsyncDisposable? logSubscription,
        IAsyncDisposable? notificationSubscription)
    {
        Exception? firstFailure = null;
        if (logSubscription is not null)
        {
            try
            {
                await logSubscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                firstFailure = ex;
            }
        }

        if (notificationSubscription is not null)
        {
            try
            {
                await notificationSubscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        if (firstFailure is not null)
            throw firstFailure;
    }

    private async Task DisposeAfterFailedCreateAsync()
    {
        try
        {
            await DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preserve the subscription failure or cancellation that made creation fail.
        }
    }

    private Task OnLogAsync(DaemonEvent<InstanceLogEventData, InstanceLogEventMeta> @event) =>
        DeliverAsync(@event, _logCallback);

    private Task OnNotificationAsync(DaemonEvent<NotificationEventData, NotificationEventMeta> @event) =>
        DeliverAsync(@event, _notificationCallback);

    private async Task DeliverAsync<TData, TMeta>(
        DaemonEvent<TData, TMeta> @event,
        Func<DaemonEvent<TData, TMeta>, Task> callback)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _inFlightDeliveryCount++;
        }

        var deliveryScope = new DeliveryScope(this, CurrentDelivery.Value);
        CurrentDelivery.Value = deliveryScope;
        try
        {
            await callback(@event).ConfigureAwait(false);
        }
        finally
        {
            deliveryScope.Deactivate();
            CurrentDelivery.Value = deliveryScope.Parent;

            TaskCompletionSource<object?>? drainCompletion = null;
            lock (_gate)
            {
                _inFlightDeliveryCount--;
                if (_disposed && _inFlightDeliveryCount == 0)
                    drainCompletion = _drainCompletion;
            }

            drainCompletion?.TrySetResult(null);
        }
    }

    private bool IsInsideActiveDelivery()
    {
        for (var scope = CurrentDelivery.Value; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope.Bridge, this) && scope.IsActive)
                return true;
        }

        return false;
    }

    private sealed class DeliveryScope(
        DaemonEventBridge bridge,
        DeliveryScope? parent)
    {
        private int _active = 1;

        internal DaemonEventBridge Bridge { get; } = bridge;
        internal DeliveryScope? Parent { get; } = parent;
        internal bool IsActive => Volatile.Read(ref _active) != 0;

        internal void Deactivate() => Volatile.Write(ref _active, 0);
    }
}
