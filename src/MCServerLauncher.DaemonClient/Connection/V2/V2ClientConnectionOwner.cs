using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal enum V2ClientConnectionOwnerState
{
    Created,
    Connecting,
    Synchronizing,
    Ready,
    WaitingToReconnect,
    Closing,
    Closed
}

/// <summary>
/// Owns one logical daemon connection across replaceable physical V2 epochs.
/// </summary>
internal sealed class V2ClientConnectionOwner : IAsyncDisposable
{
    private const string ClosedCode = "connection.closed";
    private const string EpochLostCode = "connection.epoch_lost";
    private static readonly TimeSpan MaximumReconnectDelay =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly object _gate = new();
    private readonly IV2ClientConnectionSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _reconnectDelay;
    private readonly Action<V2ClientDiagnostic>? _diagnostic;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly RemoteInstanceCatalogMirror _mirror = new();
    private readonly V2ClientSubscriptionRegistry _registry;
    private Task? _lifecycleWorker;
    private Task? _closeTask;
    private PhysicalEpoch? _candidate;
    private PhysicalEpoch? _current;
    private bool _hasReachedReady;
    private DaemonError? _lastFailure;
    private V2ClientConnectionOwnerState _state = V2ClientConnectionOwnerState.Created;

    internal V2ClientConnectionOwner(
        IV2ClientConnectionSessionFactory sessionFactory,
        TimeProvider timeProvider,
        TimeSpan reconnectDelay,
        Action<V2ClientDiagnostic>? diagnostic = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (reconnectDelay < TimeSpan.Zero || reconnectDelay > MaximumReconnectDelay)
            throw new ArgumentOutOfRangeException(nameof(reconnectDelay));

        _reconnectDelay = reconnectDelay;
        _diagnostic = diagnostic;
        _registry = new V2ClientSubscriptionRegistry(InvalidateEpoch, diagnostic);
    }

    internal RemoteInstanceCatalogMirror Mirror => _mirror;

    internal V2ClientSubscriptionRegistry Subscriptions => _registry;

    internal V2ClientConnectionOwnerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    internal bool IsReady => State == V2ClientConnectionOwnerState.Ready;

    internal DaemonError? LastFailure
    {
        get
        {
            lock (_gate)
            {
                return _lastFailure;
            }
        }
    }

    internal bool TryGetReadyCore(out V2ClientConnectionCore core)
    {
        lock (_gate)
        {
            if (_state == V2ClientConnectionOwnerState.Ready && _current is { } current)
            {
                core = current.Coordinator.Core;
                return true;
            }
        }

        core = null!;
        return false;
    }

    internal Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<Result<Unit, DaemonError>> readiness;
        lock (_gate)
        {
            if (_state is V2ClientConnectionOwnerState.Closing or V2ClientConnectionOwnerState.Closed)
                return Task.FromResult(Result.Err<Unit, DaemonError>(ClosedError()));
            if (_state == V2ClientConnectionOwnerState.Ready)
                return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
            if (_lifecycleWorker is not null)
                throw new InvalidOperationException("The initial V2 connection attempt is already running.");

            readiness = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = V2ClientConnectionOwnerState.Connecting;
            _lifecycleWorker = RunLifecycleAsync(readiness, cancellationToken);
        }

        return readiness.Task;
    }

    internal Task CloseAsync()
    {
        TaskCompletionSource completion;
        Task? worker;
        PhysicalEpoch? candidate;
        PhysicalEpoch? current;
        lock (_gate)
        {
            if (_closeTask is not null)
                return _closeTask;

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = completion.Task;
            _state = V2ClientConnectionOwnerState.Closing;
            worker = _lifecycleWorker;
            candidate = _candidate;
            current = _current;
            _candidate = null;
            _current = null;
        }

        var failures = new List<Exception>();
        StartDetachedEpochLoss(candidate, ClosedError());
        if (!ReferenceEquals(current, candidate))
            StartDetachedEpochLoss(current, ClosedError());
        TryCancel(_lifetimeCancellation, failures);
        _ = CompleteCloseAsync(worker, candidate, current, failures, completion);
        return completion.Task;
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private async Task RunLifecycleAsync(
        TaskCompletionSource<Result<Unit, DaemonError>> initialReadiness,
        CancellationToken initialCallerToken)
    {
        await Task.Yield();
        var initial = true;
        try
        {
            while (!_lifetimeCancellation.IsCancellationRequested)
            {
                PhysicalEpoch? epoch = null;
                Result<Unit, DaemonError> established;
                var callerCanceled = false;
                try
                {
                    epoch = CreateEpoch();
                    established = await EstablishAsync(
                        epoch,
                        initial ? initialCallerToken : CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                    if (epoch is not null)
                    {
                        await AbandonEpochAsync(epoch, ClosedError()).ConfigureAwait(false);
                        ReportCleanupFailures(await CleanupEpochAsync(epoch).ConfigureAwait(false));
                    }
                    initialReadiness.TrySetResult(Result.Err<Unit, DaemonError>(ClosedError()));
                    return;
                }
                catch (OperationCanceledException) when (initial && initialCallerToken.IsCancellationRequested)
                {
                    callerCanceled = true;
                    established = Result.Err<Unit, DaemonError>(EpochLostError());
                }
                catch (OperationCanceledException) when (epoch?.OperationToken.IsCancellationRequested == true)
                {
                    await epoch.WaitForLossSignalAsync().ConfigureAwait(false);
                    established = epoch.Loss.Task.IsCompletedSuccessfully
                        ? Result.Err<Unit, DaemonError>(await epoch.Loss.Task.ConfigureAwait(false))
                        : Result.Err<Unit, DaemonError>(UnexpectedSessionError(
                            new OperationCanceledException("The epoch operation was canceled without a loss signal.")));
                }
                catch (OperationCanceledException exception)
                {
                    established = Result.Err<Unit, DaemonError>(UnexpectedSessionError(exception));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    established = Result.Err<Unit, DaemonError>(UnexpectedSessionError(exception));
                }

                if (epoch is null)
                {
                    if (!_hasReachedReady)
                    {
                        ReturnToCreated();
                        initialReadiness.TrySetResult(established);
                        return;
                    }

                    await WaitToReconnectAsync().ConfigureAwait(false);
                    initial = false;
                    continue;
                }

                if (established.IsErr(out var error))
                {
                    await AbandonEpochAsync(epoch, error!).ConfigureAwait(false);
                    ReportCleanupFailures(await CleanupEpochAsync(epoch).ConfigureAwait(false));
                    if (callerCanceled)
                    {
                        ReturnToCreated();
                        initialReadiness.TrySetCanceled(initialCallerToken);
                        return;
                    }
                    if (!_hasReachedReady)
                    {
                        ReturnToCreated();
                        initialReadiness.TrySetResult(Result.Err<Unit, DaemonError>(error!));
                        return;
                    }

                    await WaitToReconnectAsync().ConfigureAwait(false);
                    initial = false;
                    continue;
                }

                initialReadiness.TrySetResult(Result.Ok<Unit, DaemonError>(Unit.Default));
                initial = false;
                await epoch.Loss.Task.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
                ReportCleanupFailures(await CleanupEpochAsync(epoch).ConfigureAwait(false));
                if (_lifetimeCancellation.IsCancellationRequested)
                    return;

                await WaitToReconnectAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            initialReadiness.TrySetResult(Result.Err<Unit, DaemonError>(ClosedError()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var failure = UnexpectedSessionError(exception);
            ReportCleanupFailures([exception]);
            await EnterRetryableFaultAsync(failure).ConfigureAwait(false);
            initialReadiness.TrySetResult(Result.Err<Unit, DaemonError>(failure));
        }
    }

    private PhysicalEpoch CreateEpoch()
    {
        var session = _sessionFactory.Create(_mirror, _registry.Route, _diagnostic);
        var epoch = new PhysicalEpoch(session);
        var closing = false;
        lock (_gate)
        {
            if (_state is V2ClientConnectionOwnerState.Closing or V2ClientConnectionOwnerState.Closed)
                closing = true;
            else
            {
                _candidate = epoch;
                _state = V2ClientConnectionOwnerState.Connecting;
            }
        }

        epoch.StartMonitor(ObserveCompletionAsync(epoch));
        if (closing)
            StartDetachedEpochLoss(epoch, ClosedError());
        return epoch;
    }

    private async Task<Result<Unit, DaemonError>> EstablishAsync(
        PhysicalEpoch epoch,
        CancellationToken initialCallerToken)
    {
        using var establishmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            epoch.OperationToken,
            initialCallerToken);
        var cancellationToken = establishmentCancellation.Token;

        var connectTask = epoch.Session.ConnectAsync(cancellationToken);
        epoch.TrackStage(connectTask);
        var connected = await connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (connected.IsErr(out var connectError))
            return Result.Err<Unit, DaemonError>(connectError!);
        if (!TryAdvanceCandidate(epoch, V2ClientConnectionOwnerState.Synchronizing))
            return Result.Err<Unit, DaemonError>(await epoch.Loss.Task.ConfigureAwait(false));

        var synchronizationTask = epoch.Coordinator.StartAsync(cancellationToken);
        epoch.TrackStage(synchronizationTask);
        var synchronized = await synchronizationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (synchronized.IsErr(out var synchronizationError))
            return Result.Err<Unit, DaemonError>(synchronizationError!);

        var bindingTask = _registry.BindReadyEpochAsync(epoch.Coordinator, cancellationToken);
        epoch.TrackStage(bindingTask);
        var bound = await bindingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (bound.IsErr(out var bindingError))
            return Result.Err<Unit, DaemonError>(bindingError!);

        lock (_gate)
        {
            if (!ReferenceEquals(_candidate, epoch) || epoch.Loss.Task.IsCompleted ||
                _state is V2ClientConnectionOwnerState.Closing or V2ClientConnectionOwnerState.Closed)
            {
                return Result.Err<Unit, DaemonError>(EpochLostError());
            }

            _candidate = null;
            _current = epoch;
            _hasReachedReady = true;
            _lastFailure = null;
            _state = V2ClientConnectionOwnerState.Ready;
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
    }

    private bool TryAdvanceCandidate(PhysicalEpoch epoch, V2ClientConnectionOwnerState state)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_candidate, epoch) || epoch.Loss.Task.IsCompleted ||
                _state is V2ClientConnectionOwnerState.Closing or V2ClientConnectionOwnerState.Closed)
            {
                return false;
            }

            _state = state;
            return true;
        }
    }

    private async Task ObserveCompletionAsync(PhysicalEpoch epoch)
    {
        DaemonError? error = null;
        try
        {
            error = await epoch.Session.Completion
                .WaitAsync(epoch.MonitorToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (epoch.MonitorToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = UnexpectedSessionError(exception);
        }

        if (error is not null)
            await SignalEpochLossAsync(epoch, error).ConfigureAwait(false);
    }

    private void InvalidateEpoch(V2ClientConnectionCoordinator coordinator, DaemonError error)
    {
        PhysicalEpoch? epoch;
        lock (_gate)
        {
            epoch = Matches(_candidate, coordinator) ? _candidate :
                Matches(_current, coordinator) ? _current : null;
        }

        if (epoch is not null)
            SignalEpochLossAsync(epoch, error);
    }

    internal void InvalidateEpoch(V2ClientConnectionCore core, DaemonError error)
    {
        PhysicalEpoch? epoch;
        lock (_gate)
        {
            epoch = Matches(_candidate, core) ? _candidate :
                Matches(_current, core) ? _current : null;
        }

        core.Close();
        if (epoch is not null)
            SignalEpochLossAsync(epoch, error);
    }

    private Task SignalEpochLossAsync(PhysicalEpoch epoch, DaemonError error)
    {
        var detach = false;
        lock (_gate)
        {
            if (ReferenceEquals(_candidate, epoch))
            {
                _candidate = null;
                detach = true;
            }
            else if (ReferenceEquals(_current, epoch))
            {
                _current = null;
                detach = true;
            }

            if (!detach)
                return epoch.LossSignalTask;
            if (_state is not V2ClientConnectionOwnerState.Closing and not V2ClientConnectionOwnerState.Closed)
                _state = _hasReachedReady ? V2ClientConnectionOwnerState.WaitingToReconnect : V2ClientConnectionOwnerState.Connecting;
        }

        return StartDetachedEpochLoss(epoch, error);
    }

    private Task AbandonEpochAsync(PhysicalEpoch epoch, DaemonError error)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_candidate, epoch))
                _candidate = null;
            if (ReferenceEquals(_current, epoch))
                _current = null;
        }

        return StartDetachedEpochLoss(epoch, error);
    }

    private async Task WaitToReconnectAsync()
    {
        lock (_gate)
        {
            if (_state is not V2ClientConnectionOwnerState.Closing and not V2ClientConnectionOwnerState.Closed)
                _state = V2ClientConnectionOwnerState.WaitingToReconnect;
        }

        await Task.Delay(_reconnectDelay, _timeProvider, _lifetimeCancellation.Token).ConfigureAwait(false);
    }

    private async Task CompleteCloseAsync(
        Task? worker,
        PhysicalEpoch? candidate,
        PhysicalEpoch? current,
        List<Exception> failures,
        TaskCompletionSource completion)
    {
        if (worker is not null)
            await CollectFailureAsync(worker, failures).ConfigureAwait(false);
        if (candidate is not null)
            failures.AddRange(await CleanupEpochAsync(candidate).ConfigureAwait(false));
        if (current is not null && !ReferenceEquals(current, candidate))
            failures.AddRange(await CleanupEpochAsync(current).ConfigureAwait(false));

        await CollectFailureAsync(() => _registry.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        try
        {
            _mirror.Close();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _lifetimeCancellation.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        lock (_gate)
        {
            _state = V2ClientConnectionOwnerState.Closed;
        }

        if (failures.Count == 0)
            completion.TrySetResult();
        else
            completion.TrySetException(new AggregateException("The logical V2 client connection failed to close cleanly.", failures));
    }

    private Task<ImmutableArray<Exception>> CleanupEpochAsync(PhysicalEpoch epoch) =>
        epoch.GetOrStartCleanup(() => CleanupEpochCoreAsync(epoch));

    private async Task<ImmutableArray<Exception>> CleanupEpochCoreAsync(PhysicalEpoch epoch)
    {
        var failures = new List<Exception>();
        await epoch.WaitForLossSignalAsync().ConfigureAwait(false);
        await epoch.CancelOperationAsync().ConfigureAwait(false);
        while (epoch.CancellationFailures.TryDequeue(out var cancellationFailure))
            failures.Add(cancellationFailure);
        await CollectFailureAsync(() => epoch.Coordinator.CloseAsync(), failures).ConfigureAwait(false);
        await CollectFailureAsync(() => epoch.Session.CloseAsync(), failures).ConfigureAwait(false);
        await CollectFailureAsync(() => epoch.Session.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        failures.AddRange(await epoch.CancelMonitorAsync().ConfigureAwait(false));
        await CollectFailureAsync(epoch.ConvergeMonitorAsync, failures).ConfigureAwait(false);
        foreach (var stage in epoch.SnapshotStages())
            await ObserveStageAsync(stage, epoch.OperationToken, failures).ConfigureAwait(false);
        epoch.DisposeCancellations(failures);
        return [.. failures];
    }

    private Task StartDetachedEpochLoss(PhysicalEpoch? epoch, DaemonError error)
    {
        if (epoch is null)
            return Task.CompletedTask;

        return epoch.GetOrStartLossSignal(error, async () =>
        {
            try
            {
                DetachEpoch(epoch);
            }
            catch (Exception exception)
            {
                epoch.CancellationFailures.Enqueue(exception);
            }

            foreach (var failure in await epoch.CancelOperationAsync().ConfigureAwait(false))
                epoch.CancellationFailures.Enqueue(failure);
        });
    }

    private async Task EnterRetryableFaultAsync(DaemonError failure)
    {
        PhysicalEpoch? candidate;
        PhysicalEpoch? current;
        lock (_gate)
        {
            candidate = _candidate;
            current = _current;
            _candidate = null;
            _current = null;
            _lastFailure = failure;
            if (_state is not V2ClientConnectionOwnerState.Closing and not V2ClientConnectionOwnerState.Closed)
                _state = V2ClientConnectionOwnerState.Created;
            _lifecycleWorker = null;
        }

        if (candidate is not null)
        {
            await StartDetachedEpochLoss(candidate, failure).ConfigureAwait(false);
            ReportCleanupFailures(await CleanupEpochAsync(candidate).ConfigureAwait(false));
        }
        if (current is not null && !ReferenceEquals(current, candidate))
        {
            await StartDetachedEpochLoss(current, failure).ConfigureAwait(false);
            ReportCleanupFailures(await CleanupEpochAsync(current).ConfigureAwait(false));
        }
    }

    private void DetachEpoch(PhysicalEpoch epoch)
    {
        if (Interlocked.Exchange(ref epoch.Detached, 1) == 0)
            _registry.DetachEpoch(epoch.Coordinator);
    }

    private static void TryCancel(CancellationTokenSource cancellation, List<Exception> failures)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task CollectFailureAsync(Task task, List<Exception> failures)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task ObserveStageAsync(
        Task task,
        CancellationToken operationToken,
        List<Exception> failures)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void ReportCleanupFailures(IEnumerable<Exception> failures)
    {
        foreach (var exception in failures)
        {
            try
            {
                _diagnostic?.Invoke(new V2ClientDiagnostic(
                    V2ClientDiagnosticKind.ProtocolFault,
                    $"V2 epoch cleanup failed: {exception.GetType().Name}."));
            }
            catch (Exception)
            {
                // Diagnostics cannot own connection lifecycle.
            }
        }
    }

    private void ReturnToCreated()
    {
        lock (_gate)
        {
            if (_state is not V2ClientConnectionOwnerState.Closing and not V2ClientConnectionOwnerState.Closed)
                _state = V2ClientConnectionOwnerState.Created;
            _lifecycleWorker = null;
        }
    }

    private static bool Matches(PhysicalEpoch? epoch, V2ClientConnectionCoordinator coordinator) =>
        epoch is not null && ReferenceEquals(epoch.Coordinator, coordinator);

    private static bool Matches(PhysicalEpoch? epoch, V2ClientConnectionCore core) =>
        epoch is not null && ReferenceEquals(epoch.Coordinator.Core, core);

    private static TransportDaemonError ClosedError() =>
        new(ClosedCode, "The logical V2 client connection is closed.");

    private static TransportDaemonError EpochLostError() =>
        new(EpochLostCode, "The V2 physical connection epoch was lost.");

    private static TransportDaemonError UnexpectedSessionError(Exception exception) =>
        new("transport.session_failed", $"The V2 physical session failed: {exception.GetType().Name}.");

    private sealed class PhysicalEpoch
    {
        private readonly object _gate = new();
        private readonly object _cancellationGate = new();
        private readonly List<Task> _stages = [];
        private readonly TaskCompletionSource _monitorStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _lossSignalStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<ImmutableArray<Exception>>? _cleanupTask;
        private Task? _lossSignalTask;
        private Task? _monitorTask;
        private readonly CancellationTokenSource _operationCancellation;
        private readonly CancellationTokenSource _monitorCancellation = new();
        private Task<ImmutableArray<Exception>>? _operationCancellationTask;
        private Task<ImmutableArray<Exception>>? _monitorCancellationTask;
        private bool _cancellationsDisposed;

        internal PhysicalEpoch(IV2ClientConnectionSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            _operationCancellation = new CancellationTokenSource();
        }

        internal IV2ClientConnectionSession Session { get; }
        internal V2ClientConnectionCoordinator Coordinator => Session.Coordinator;
        internal CancellationToken OperationToken
        {
            get
            {
                lock (_cancellationGate)
                {
                    return _operationCancellation.Token;
                }
            }
        }
        internal CancellationToken MonitorToken
        {
            get
            {
                lock (_cancellationGate)
                {
                    return _monitorCancellation.Token;
                }
            }
        }
        internal TaskCompletionSource<DaemonError> Loss { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task LossSignalTask
        {
            get
            {
                lock (_gate)
                {
                    return _lossSignalTask ?? Task.CompletedTask;
                }
            }
        }
        internal ConcurrentQueue<Exception> CancellationFailures { get; } = new();
        internal int Detached;

        internal Task<ImmutableArray<Exception>> CancelOperationAsync()
        {
            TaskCompletionSource<ImmutableArray<Exception>> completion;
            lock (_cancellationGate)
            {
                if (_operationCancellationTask is not null)
                    return _operationCancellationTask;
                if (_cancellationsDisposed)
                    return Task.FromResult(ImmutableArray<Exception>.Empty);

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _operationCancellationTask = completion.Task;
            }

            _ = CompleteCancellationAsync(_operationCancellation, completion);
            return completion.Task;
        }

        internal Task<ImmutableArray<Exception>> CancelMonitorAsync()
        {
            TaskCompletionSource<ImmutableArray<Exception>> completion;
            lock (_cancellationGate)
            {
                if (_monitorCancellationTask is not null)
                    return _monitorCancellationTask;
                if (_cancellationsDisposed)
                    return Task.FromResult(ImmutableArray<Exception>.Empty);

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _monitorCancellationTask = completion.Task;
            }

            _ = CompleteCancellationAsync(_monitorCancellation, completion);
            return completion.Task;
        }

        internal void DisposeCancellations(List<Exception> failures)
        {
            lock (_cancellationGate)
            {
                if (_cancellationsDisposed)
                    return;
                if (_operationCancellationTask is not { IsCompleted: true } ||
                    _monitorCancellationTask is not { IsCompleted: true })
                {
                    throw new InvalidOperationException("Epoch cancellation must converge before cancellation sources are disposed.");
                }

                _cancellationsDisposed = true;
                try
                {
                    _monitorCancellation.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try
                {
                    _operationCancellation.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        internal Task GetOrStartLossSignal(DaemonError error, Func<Task> beforePublish)
        {
            TaskCompletionSource completion;
            lock (_gate)
            {
                if (_lossSignalTask is not null)
                    return _lossSignalTask;

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _lossSignalTask = completion.Task;
            }

            _lossSignalStarted.TrySetResult();
            _ = CompleteLossSignalAsync(error, beforePublish, completion);
            return completion.Task;
        }

        internal async Task WaitForLossSignalAsync()
        {
            await _lossSignalStarted.Task.ConfigureAwait(false);
            Task signal;
            lock (_gate)
            {
                signal = _lossSignalTask!;
            }
            await signal.ConfigureAwait(false);
        }

        internal void StartMonitor(Task monitor)
        {
            lock (_gate)
            {
                _monitorTask = monitor;
            }
            _monitorStarted.TrySetResult();
        }

        internal async Task ConvergeMonitorAsync()
        {
            await _monitorStarted.Task.ConfigureAwait(false);
            Task monitor;
            lock (_gate)
            {
                monitor = _monitorTask!;
            }
            await monitor.ConfigureAwait(false);
        }

        internal void TrackStage(Task stage)
        {
            lock (_gate)
            {
                _stages.Add(stage);
            }
        }

        internal Task[] SnapshotStages()
        {
            lock (_gate)
            {
                return _stages.ToArray();
            }
        }

        internal Task<ImmutableArray<Exception>> GetOrStartCleanup(
            Func<Task<ImmutableArray<Exception>>> cleanup)
        {
            TaskCompletionSource<ImmutableArray<Exception>> completion;
            lock (_gate)
            {
                if (_cleanupTask is not null)
                    return _cleanupTask;

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _cleanupTask = completion.Task;
            }

            _ = CompleteCleanupAsync(cleanup, completion);
            return completion.Task;
        }

        private static async Task CompleteCleanupAsync(
            Func<Task<ImmutableArray<Exception>>> cleanup,
            TaskCompletionSource<ImmutableArray<Exception>> completion)
        {
            try
            {
                completion.TrySetResult(await cleanup().ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                completion.TrySetResult([exception]);
            }
        }

        private async Task CompleteLossSignalAsync(
            DaemonError error,
            Func<Task> beforePublish,
            TaskCompletionSource completion)
        {
            try
            {
                await beforePublish().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                CancellationFailures.Enqueue(exception);
            }
            finally
            {
                Loss.TrySetResult(error);
                completion.TrySetResult();
            }
        }

        private static async Task CompleteCancellationAsync(
            CancellationTokenSource cancellation,
            TaskCompletionSource<ImmutableArray<Exception>> completion)
        {
            var failures = ImmutableArray.CreateBuilder<Exception>();
            try
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            completion.TrySetResult(failures.ToImmutable());
        }
    }
}
