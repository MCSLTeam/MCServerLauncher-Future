using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Protocol;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Connection.V2;

/// <summary>
/// Owns catalog readiness for one physical V2 connection epoch.
/// </summary>
internal sealed class V2ClientConnectionCoordinator
{
    private const string ClosedCode = "connection.closed";
    private const string SupersededCode = "connection.epoch_superseded";
    private const string InvalidEventCode = "protocol.event_data_invalid";
    private readonly object _gate = new();
    private readonly RemoteInstanceCatalogMirror _mirror;
    private readonly Channel<byte> _refetchSignals;
    private readonly TaskCompletionSource<bool> _startSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Result<Unit, DaemonError>> _initialReadiness =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<Result<Unit, DaemonError>> _generationReadiness =
        NewReadinessSignal();
    private readonly Task _initializationWorker;
    private readonly Task _refetchWorker;
    private long _generation;
    private bool _started;
    private bool _subscribed;
    private DaemonError? _terminalError;
    private int _ready;

    internal V2ClientConnectionCoordinator(
        IV2ClientWireTransport transport,
        RemoteInstanceCatalogMirror mirror,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        Func<JsonRpcRequestId>? idFactory = null,
        Action<V2ClientDiagnostic>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        ArgumentNullException.ThrowIfNull(timeProvider);

        _refetchSignals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        });
        Core = new V2ClientConnectionCore(
            transport,
            timeProvider,
            requestTimeout,
            idFactory,
            diagnostic,
            HandleRemoteEvent);
        _initializationWorker = RunInitializationAsync();
        _refetchWorker = RunRefetchWorkerAsync();
    }

    internal V2ClientConnectionCore Core { get; }

    internal bool IsReady
    {
        get
        {
            if (Volatile.Read(ref _ready) == 0)
                return false;

            lock (_gate)
            {
                return _terminalError is null &&
                       Volatile.Read(ref _ready) != 0 &&
                       _mirror.IsCurrentGeneration(_generation);
            }
        }
    }

    internal DaemonError? TerminalError
    {
        get
        {
            lock (_gate)
            {
                return _terminalError;
            }
        }
    }

    internal Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken = default)
    {
        DaemonError? terminal;
        var superseded = false;
        var start = false;
        lock (_gate)
        {
            terminal = _terminalError;
            superseded = terminal is null && _started && !_mirror.IsCurrentGeneration(_generation);
            if (terminal is null && !_started)
            {
                _generation = _mirror.BeginReconciliation();
                _started = true;
                start = true;
            }
        }

        if (superseded)
        {
            CompleteEpoch(SupersededError());
            terminal = TerminalError;
        }

        if (terminal is not null)
            return Task.FromResult(Result.Err<Unit, DaemonError>(terminal));

        if (start)
            _startSignal.TrySetResult(true);

        return _initialReadiness.Task.WaitAsync(cancellationToken);
    }

    internal Task<Result<Unit, DaemonError>> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        Task<Result<Unit, DaemonError>> readiness;
        DaemonError? terminal;
        var superseded = false;
        lock (_gate)
        {
            terminal = _terminalError;
            superseded = terminal is null && _started && !_mirror.IsCurrentGeneration(_generation);
            if (terminal is null && IsReady)
                return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));

            readiness = _generationReadiness.Task;
        }

        if (superseded)
        {
            CompleteEpoch(SupersededError());
            terminal = TerminalError;
        }

        return terminal is not null
            ? Task.FromResult(Result.Err<Unit, DaemonError>(terminal))
            : readiness.WaitAsync(cancellationToken);
    }

    internal async Task CloseAsync()
    {
        CompleteEpoch(ClosedError());
        await _initializationWorker.ConfigureAwait(false);
        await _refetchWorker.ConfigureAwait(false);
        await Core.WaitForSendObserversAsync().ConfigureAwait(false);
    }

    private async Task RunInitializationAsync()
    {
        try
        {
            if (!await _startSignal.Task.ConfigureAwait(false))
                return;

            var subscribed = await Core.InvokeUnitAsync(
                V2ClientProtocol.SubscribeEvent,
                new EventSubscriptionRequest(V2ClientProtocol.InstanceCatalogChanged.Name.Value))
                .ConfigureAwait(false);
            if (subscribed.IsErr(out var error))
            {
                CompleteEpoch(error!);
                return;
            }

            var schedule = false;
            lock (_gate)
            {
                if (_terminalError is null)
                {
                    _subscribed = true;
                    schedule = true;
                }
            }

            if (schedule)
                _refetchSignals.Writer.TryWrite(0);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CompleteEpoch(UnexpectedTransportError(exception));
        }
    }

    private async Task RunRefetchWorkerAsync()
    {
        try
        {
            await foreach (var _ in _refetchSignals.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                long generation;
                DaemonError? terminalToFinish = null;
                TaskCompletionSource<Result<Unit, DaemonError>>? terminalReadiness = null;
                lock (_gate)
                {
                    if (_terminalError is not null)
                        return;

                    generation = _generation;
                    if (!_mirror.IsCurrentGeneration(generation))
                    {
                        terminalToFinish = SupersededError();
                        TrySetTerminalLocked(terminalToFinish, out terminalReadiness);
                    }
                }

                if (terminalToFinish is not null)
                {
                    FinishEpoch(terminalToFinish, terminalReadiness!);
                    return;
                }

                var result = await Core.InvokeAsync(
                    V2ClientProtocol.GetInstanceCatalog,
                    new EmptyRequest()).ConfigureAwait(false);

                TaskCompletionSource<Result<Unit, DaemonError>>? readySignal = null;
                var refetch = false;
                lock (_gate)
                {
                    if (_terminalError is not null)
                        return;

                    if (generation != _generation)
                    {
                        refetch = true;
                    }
                    else if (!_mirror.IsCurrentGeneration(generation))
                    {
                        terminalToFinish = SupersededError();
                        TrySetTerminalLocked(terminalToFinish, out terminalReadiness);
                    }
                    else if (result.IsErr(out var error))
                    {
                        terminalToFinish = error!;
                        TrySetTerminalLocked(terminalToFinish, out terminalReadiness);
                    }
                    else
                    {
                        var transition = _mirror.ApplyFullSnapshot(generation, result.Unwrap());
                        switch (transition)
                        {
                            case RemoteInstanceCatalogTransition.Ready:
                                Volatile.Write(ref _ready, 1);
                                readySignal = _generationReadiness;
                                break;
                            case RemoteInstanceCatalogTransition.NeedsResync:
                                ArmReconciliationLocked();
                                refetch = true;
                                break;
                            case RemoteInstanceCatalogTransition.IgnoredStaleGeneration:
                                refetch = true;
                                break;
                            case RemoteInstanceCatalogTransition.Closed:
                                throw new InvalidOperationException("A connection coordinator cannot close its long-lived catalog mirror.");
                            default:
                                throw new InvalidOperationException("A full catalog snapshot produced an invalid mirror transition.");
                        }
                    }
                }

                if (terminalToFinish is not null)
                {
                    FinishEpoch(terminalToFinish, terminalReadiness!);
                    return;
                }

                if (readySignal is not null)
                {
                    var resultValue = Result.Ok<Unit, DaemonError>(Unit.Default);
                    readySignal.TrySetResult(resultValue);
                    _initialReadiness.TrySetResult(resultValue);
                }

                if (refetch)
                    _refetchSignals.Writer.TryWrite(0);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CompleteEpoch(UnexpectedTransportError(exception));
        }
    }

    private void HandleRemoteEvent(JsonRpcRemoteEventNotification notification)
    {
        if (!StringComparer.Ordinal.Equals(
                notification.Method,
                V2ClientProtocol.InstanceCatalogChanged.Name.Value))
        {
            return;
        }

        var supersededBeforeDecode = false;
        lock (_gate)
        {
            if (_terminalError is not null || !_started)
                return;

            if (!_mirror.IsCurrentGeneration(_generation))
            {
                supersededBeforeDecode = true;
            }
        }

        if (supersededBeforeDecode)
        {
            CompleteEpoch(SupersededError());
            return;
        }

        InstanceCatalogChangedEventData? change;
        try
        {
            if (notification.Params.Meta.Kind != JsonRpcOptionalPayloadKind.Missing ||
                notification.Params.Data.Kind != JsonRpcOptionalPayloadKind.Value)
            {
                throw new JsonException("The catalog event fields do not match their descriptor.");
            }

            change = notification.Params.Data.Deserialize(
                V2ClientProtocol.InstanceCatalogChanged.DataTypeInfo);
            if (change is null)
                throw new JsonException("The catalog event data is required.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           ArgumentException or FormatException or OverflowException or NotSupportedException)
        {
            CompleteEpoch(InvalidEventError());
            return;
        }

        var refetch = false;
        var superseded = false;
        lock (_gate)
        {
            if (_terminalError is not null || !_started)
                return;

            if (!_mirror.IsCurrentGeneration(_generation))
            {
                superseded = true;
            }
            else
            {
                var transition = _mirror.ReceiveChange(_generation, change);
                if (transition == RemoteInstanceCatalogTransition.NeedsResync)
                {
                    ArmReconciliationLocked();
                    refetch = _subscribed;
                }
            }
        }

        if (superseded)
        {
            CompleteEpoch(SupersededError());
            return;
        }

        if (refetch)
            _refetchSignals.Writer.TryWrite(0);
    }

    private void ArmReconciliationLocked()
    {
        _generation = _mirror.BeginReconciliation();
        if (IsReady)
            _generationReadiness = NewReadinessSignal();

        Volatile.Write(ref _ready, 0);
    }

    private void CompleteEpoch(DaemonError error)
    {
        TaskCompletionSource<Result<Unit, DaemonError>>? generationReadiness;
        lock (_gate)
        {
            if (!TrySetTerminalLocked(error, out generationReadiness))
                return;
        }

        FinishEpoch(error, generationReadiness!);
    }

    private bool TrySetTerminalLocked(
        DaemonError error,
        out TaskCompletionSource<Result<Unit, DaemonError>>? generationReadiness)
    {
        generationReadiness = null;
        if (_terminalError is not null)
            return false;

        _terminalError = error;
        Volatile.Write(ref _ready, 0);
        generationReadiness = _generationReadiness;
        return true;
    }

    private void FinishEpoch(
        DaemonError error,
        TaskCompletionSource<Result<Unit, DaemonError>> generationReadiness)
    {
        var result = Result.Err<Unit, DaemonError>(error);
        generationReadiness.TrySetResult(result);
        _initialReadiness.TrySetResult(result);
        _startSignal.TrySetResult(false);
        _refetchSignals.Writer.TryComplete();
        Core.Close();
    }

    private static TransportDaemonError ClosedError() =>
        new(ClosedCode, "The V2 connection is closed.");

    private static TransportDaemonError SupersededError() =>
        new(SupersededCode, "The V2 connection epoch no longer owns the catalog mirror generation.");

    private static TransportDaemonError InvalidEventError() =>
        new(InvalidEventCode, "A required V2 catalog event payload violates its descriptor metadata.");

    private static TransportDaemonError UnexpectedTransportError(Exception exception) =>
        new("transport.coordinator_failed", $"The V2 connection coordinator failed: {exception.GetType().Name}.");

    private static TaskCompletionSource<Result<Unit, DaemonError>> NewReadinessSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
