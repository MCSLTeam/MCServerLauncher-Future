using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Services;
using RustyOptions;

namespace MCServerLauncher.WPF.Tests.Services;

public sealed class DaemonEventBridgeTests
{
    private static readonly Guid InstanceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task ManagerInitializerUsesExactLogFilterAndWildcardNotificationFilter()
    {
        var source = new FakeEventSource();
        await using var bridge = (await CreateThroughManagerAsync(source)).Unwrap();

        var log = Assert.Single(source.Requests, request => request.Name == "mcsl.event.instance.log");
        Assert.Equal(DaemonEventFieldKind.Value, log.LogFilterKind);
        Assert.Equal(InstanceId, log.LogFilterInstanceId);

        var notification = Assert.Single(source.Requests, request => request.Name == "mcsl.event.notification");
        Assert.Equal(DaemonEventFieldKind.Missing, notification.NotificationFilterKind);
    }

    [Fact]
    public async Task ManagerCallbacksDispatchLogAndNotificationThroughInjectedDispatcher()
    {
        var source = new FakeEventSource();
        var logs = new List<string>();
        var notifications = new List<NotificationDelivery>();
        var dispatchCount = 0;
        await using var bridge = (await CreateThroughManagerAsync(
            source,
            action =>
            {
                dispatchCount++;
                action();
                return Task.CompletedTask;
            },
            logs.Add,
            (title, message, severity) => notifications.Add(new NotificationDelivery(title, message, severity)))).Unwrap();

        await source.EmitLogAsync(InstanceId, "line");
        await source.EmitNotificationAsync(InstanceId, "Title", "Message", "Success");

        Assert.Equal(2, dispatchCount);
        Assert.Equal(["line"], logs);
        Assert.Equal(new NotificationDelivery("Title", "Message", InfoBarSeverity.Success), Assert.Single(notifications));
    }

    [Fact]
    public async Task ManagerCallbacksRejectWrongInstanceBeforeDispatcherOrSink()
    {
        var source = new FakeEventSource();
        var dispatchCount = 0;
        var sinkCount = 0;
        await using var bridge = (await CreateThroughManagerAsync(
            source,
            action =>
            {
                dispatchCount++;
                action();
                return Task.CompletedTask;
            },
            _ => sinkCount++,
            (_, _, _) => sinkCount++)).Unwrap();

        var otherInstanceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        await source.EmitLogAsync(otherInstanceId, "wrong");
        await source.EmitNotificationAsync(otherInstanceId, "wrong", "wrong", "Error");

        Assert.Equal(0, dispatchCount);
        Assert.Equal(0, sinkCount);
    }

    [Fact]
    public async Task ManagerInitializerResultErrorRollsBackLogSubscriptionOnce()
    {
        var source = new FakeEventSource
        {
            NotificationFailure = NotificationSubscribeFailure.ResultError
        };

        var result = await CreateThroughManagerAsync(source);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("notification.subscribe_failed", error!.Code);
        Assert.Equal(1, Assert.Single(source.Handles).DisposeCount);
    }

    [Fact]
    public async Task ManagerInitializerCancellationRollsBackLogSubscriptionOnceAndPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new FakeEventSource
        {
            NotificationFailure = NotificationSubscribeFailure.Cancellation
        };

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CreateThroughManagerAsync(source, cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, Assert.Single(source.Handles).DisposeCount);
    }

    [Fact]
    public async Task ManagerInitializerExceptionRollsBackLogSubscriptionOnceAndPreservesException()
    {
        var expected = new InvalidOperationException("notification subscribe exploded");
        var source = new FakeEventSource
        {
            NotificationFailure = NotificationSubscribeFailure.Exception,
            NotificationException = expected
        };

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateThroughManagerAsync(source));

        Assert.Same(expected, actual);
        Assert.Equal(1, Assert.Single(source.Handles).DisposeCount);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndUnsubscribesEachHandleOnce()
    {
        var source = new FakeEventSource();
        var bridge = (await CreateThroughManagerAsync(source)).Unwrap();

        var first = bridge.DisposeAsync().AsTask();
        var second = bridge.DisposeAsync().AsTask();
        Assert.Same(first, second);
        await Task.WhenAll(first, second);

        Assert.All(source.Handles, handle => Assert.Equal(1, handle.DisposeCount));
    }

    [Fact]
    public async Task DisposeWaitsForAcceptedDispatcherDeliveryAndRejectsLaterDelivery()
    {
        var source = new FakeEventSource();
        var logs = new List<string>();
        var dispatchEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchCount = 0;
        var bridge = (await CreateThroughManagerAsync(
            source,
            async action =>
            {
                Interlocked.Increment(ref dispatchCount);
                dispatchEntered.TrySetResult(null);
                await releaseDispatch.Task;
                action();
            },
            logs.Add)).Unwrap();

        var acceptedDelivery = source.EmitLogAsync(InstanceId, "accepted");
        await dispatchEntered.Task;

        var disposeTask = bridge.DisposeAsync().AsTask();
        Assert.False(disposeTask.IsCompleted);

        await source.EmitLogAsync(InstanceId, "late");
        Assert.Equal(1, Volatile.Read(ref dispatchCount));

        releaseDispatch.TrySetResult(null);
        await acceptedDelivery;
        await disposeTask;

        Assert.Equal(["accepted"], logs);
        Assert.All(source.Handles, handle => Assert.Equal(1, handle.DisposeCount));
    }

    [Fact]
    public async Task CallbackCanInitiateAndAwaitDisposeWithoutWaitingOnItself()
    {
        var source = new FakeEventSource();
        var logs = new List<string>();
        DaemonEventBridge? bridge = null;
        bridge = (await CreateThroughManagerAsync(
            source,
            async action =>
            {
                action();
                await bridge!.DisposeAsync();
            },
            logs.Add)).Unwrap();

        await source.EmitLogAsync(InstanceId, "line");
        await bridge.DisposeAsync();

        Assert.Equal(["line"], logs);
        Assert.All(source.Handles, handle => Assert.Equal(1, handle.DisposeCount));
    }

    [Fact]
    public async Task DisposePreventsCallbacksAfterCleanup()
    {
        var source = new FakeEventSource();
        var sinkCount = 0;
        var bridge = (await CreateThroughManagerAsync(
            source,
            appendLog: _ => sinkCount++,
            pushNotification: (_, _, _) => sinkCount++)).Unwrap();

        await bridge.DisposeAsync();
        await source.EmitLogAsync(InstanceId, "late");
        await source.EmitNotificationAsync(InstanceId, "late", "late", "Warning");

        Assert.Equal(0, sinkCount);
    }

    [Theory]
    [InlineData("Success", InfoBarSeverity.Success)]
    [InlineData("Warning", InfoBarSeverity.Warning)]
    [InlineData("Error", InfoBarSeverity.Error)]
    [InlineData("Informational", InfoBarSeverity.Informational)]
    [InlineData("unknown", InfoBarSeverity.Informational)]
    public async Task ManagerNotificationCallbackUsesProductionSeverityMapping(
        string severity,
        InfoBarSeverity expected)
    {
        var source = new FakeEventSource();
        InfoBarSeverity? actual = null;
        await using var bridge = (await CreateThroughManagerAsync(
            source,
            pushNotification: (_, _, value) => actual = value)).Unwrap();

        await source.EmitNotificationAsync(InstanceId, "Title", "Message", severity);

        Assert.Equal(expected, actual);
    }

    private static Task<Result<DaemonEventBridge, DaemonError>> CreateThroughManagerAsync(
        FakeEventSource source,
        Func<Action, Task>? dispatchAsync = null,
        Action<string>? appendLog = null,
        Action<string, string, InfoBarSeverity>? pushNotification = null,
        CancellationToken cancellationToken = default) =>
        InstanceDataManager.CreateEventBridgeAsync(
            source,
            InstanceId,
            dispatchAsync ?? ImmediateDispatchAsync,
            appendLog ?? (_ => { }),
            pushNotification ?? ((_, _, _) => { }),
            cancellationToken);

    private static Task ImmediateDispatchAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private sealed class FakeEventSource : ITypedDaemonEventSource
    {
        internal List<Request> Requests { get; } = [];
        internal List<CountingDisposable> Handles { get; } = [];
        internal NotificationSubscribeFailure NotificationFailure { get; init; }
        internal Exception? NotificationException { get; init; }
        private Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task>? _logCallback;
        private Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task>? _notificationCallback;

        public Task<Result<IAsyncDisposable, DaemonError>> SubscribeAsync<TData, TMeta>(
            EventDescriptor<TData, TMeta> descriptor,
            DaemonEventFilter<TMeta> filter,
            Func<DaemonEvent<TData, TMeta>, Task> callback,
            CancellationToken cancellationToken = default)
        {
            var request = new Request(descriptor.Name.Value, filter);
            Requests.Add(request);
            if (descriptor.DataTypeInfo.Type == typeof(NotificationEventData))
            {
                switch (NotificationFailure)
                {
                    case NotificationSubscribeFailure.ResultError:
                        return Task.FromResult(Result.Err<IAsyncDisposable, DaemonError>(new TransportDaemonError(
                            "notification.subscribe_failed",
                            "test failure")));
                    case NotificationSubscribeFailure.Cancellation:
                        return Task.FromException<Result<IAsyncDisposable, DaemonError>>(
                            new OperationCanceledException(cancellationToken));
                    case NotificationSubscribeFailure.Exception:
                        return Task.FromException<Result<IAsyncDisposable, DaemonError>>(
                            NotificationException ?? new InvalidOperationException("test failure"));
                }
            }

            var handle = new CountingDisposable();
            Handles.Add(handle);
            if (descriptor.DataTypeInfo.Type == typeof(InstanceLogEventData))
                _logCallback = (Func<DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>, Task>)(object)callback;
            else
                _notificationCallback = (Func<DaemonEvent<NotificationEventData, NotificationEventMeta>, Task>)(object)callback;

            return Task.FromResult(Result.Ok<IAsyncDisposable, DaemonError>(handle));
        }

        internal Task EmitLogAsync(Guid instanceId, string log) =>
            _logCallback?.Invoke(new DaemonEvent<InstanceLogEventData, InstanceLogEventMeta>(
                1,
                2,
                DaemonEventField<InstanceLogEventMeta>.FromValue(new InstanceLogEventMeta(instanceId)),
                DaemonEventField<InstanceLogEventData>.FromValue(new InstanceLogEventData(log)))) ?? Task.CompletedTask;

        internal Task EmitNotificationAsync(Guid instanceId, string title, string message, string severity) =>
            _notificationCallback?.Invoke(new DaemonEvent<NotificationEventData, NotificationEventMeta>(
                3,
                4,
                DaemonEventField<NotificationEventMeta>.FromValue(new NotificationEventMeta(instanceId, Guid.NewGuid())),
                DaemonEventField<NotificationEventData>.FromValue(new NotificationEventData(title, message, severity)))) ?? Task.CompletedTask;
    }

    private sealed record Request(string Name, object Filter)
    {
        internal DaemonEventFieldKind LogFilterKind =>
            Filter is DaemonEventFilter<InstanceLogEventMeta> value ? value.Kind : throw new InvalidOperationException();

        internal Guid LogFilterInstanceId =>
            Filter is DaemonEventFilter<InstanceLogEventMeta> value ? value.Value.InstanceId : throw new InvalidOperationException();

        internal DaemonEventFieldKind NotificationFilterKind =>
            Filter is DaemonEventFilter<NotificationEventMeta> value ? value.Kind : throw new InvalidOperationException();
    }

    private sealed record NotificationDelivery(string Title, string Message, InfoBarSeverity Severity);

    private sealed class CountingDisposable : IAsyncDisposable
    {
        private int _disposeCount;
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private enum NotificationSubscribeFailure
    {
        None,
        ResultError,
        Cancellation,
        Exception
    }
}
