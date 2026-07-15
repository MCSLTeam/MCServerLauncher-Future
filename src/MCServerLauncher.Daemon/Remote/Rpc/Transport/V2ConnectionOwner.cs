using System.Collections.Immutable;
using System.Threading.Channels;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Authentication;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal enum V2ConnectionState
{
    Created,
    Running,
    Completing,
    Closing,
    Closed
}

internal sealed class V2ConnectionOwner : ICompiledProtocolPermissionView, IAsyncDisposable
{
    internal const int OutboundCapacity = 256;
    internal static readonly TimeSpan FrameSendTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly IV2OutboundSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<V2OutboundMessage> _outbound;
    private readonly CancellationTokenSource _connectionLifetime;
    private readonly CancellationToken _connectionToken;
    private readonly List<IV2ConnectionCleanup> _cleanups = [];
    private readonly TaskCompletionSource _pumpCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private V2ConnectionState _state = V2ConnectionState.Created;
    private V2ConnectionCloseReason? _closeReason;
    private V2ConnectionStopCause? _diagnosticStopCause;
    private bool _pumpStarted;
    private int _stopCause;
    private int _disposeStarted;

    internal V2ConnectionOwner(
        IV2OutboundSender sender,
        IEnumerable<string>? permissions = null,
        TimeProvider? timeProvider = null,
        CancellationToken connectionCancellation = default)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _timeProvider = timeProvider ?? TimeProvider.System;
        Permissions = NormalizePermissions(permissions);
        CompiledPermissions = CompilePermissions(Permissions);
        _connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation);
        _connectionToken = _connectionLifetime.Token;
        _outbound = Channel.CreateBounded<V2OutboundMessage>(
            new BoundedChannelOptions(OutboundCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public ImmutableArray<string> Permissions { get; }

    public Permissions CompiledPermissions { get; }

    internal V2ConnectionState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    internal V2ConnectionCloseReason? CloseReason
    {
        get
        {
            lock (_gate)
                return _closeReason;
        }
    }

    internal V2ConnectionStopCause? DiagnosticStopCause
    {
        get
        {
            lock (_gate)
                return _diagnosticStopCause;
        }
    }

    internal CancellationToken ConnectionToken => _connectionToken;

    internal Task Start()
    {
        var runPump = false;
        lock (_gate)
        {
            if (!_pumpStarted && _state is V2ConnectionState.Created or V2ConnectionState.Running)
            {
                _pumpStarted = true;
                _state = V2ConnectionState.Running;
                runPump = true;
            }
        }

        if (runPump)
            _ = PumpAsync();

        return _pumpCompletion.Task;
    }

    internal bool TryEnqueue(V2OutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ClosePlan? closePlan = null;
        var accepted = false;
        lock (_gate)
        {
            if (_state is not (V2ConnectionState.Created or V2ConnectionState.Running))
                return false;

            accepted = _outbound.Writer.TryWrite(message);
            if (!accepted)
                closePlan = TransitionToClosingLocked(V2ConnectionStopCause.SlowConsumer);
        }

        LaunchClose(closePlan);
        return accepted;
    }

    internal bool TryRegisterCleanup(IV2ConnectionCleanup cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_gate)
        {
            if (_state is not (V2ConnectionState.Created or V2ConnectionState.Running))
                return false;

            _cleanups.Add(cleanup);
            return true;
        }
    }

    internal bool TryUnregisterCleanup(IV2ConnectionCleanup cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (_gate)
        {
            var index = _cleanups.FindIndex(candidate => ReferenceEquals(candidate, cleanup));
            if (index < 0)
                return false;

            _cleanups.RemoveAt(index);
            return true;
        }
    }

    internal int CleanupRegistrationCount
    {
        get
        {
            lock (_gate)
                return _cleanups.Count;
        }
    }

    internal Task CompleteAsync()
    {
        var runPump = false;
        lock (_gate)
        {
            if (_state is V2ConnectionState.Created or V2ConnectionState.Running)
            {
                _state = V2ConnectionState.Completing;
                _outbound.Writer.TryComplete();
                if (!_pumpStarted)
                {
                    _pumpStarted = true;
                    runPump = true;
                }
            }
        }

        if (runPump)
            _ = PumpAsync();

        return AwaitShutdownAsync();
    }

    internal Task AbortAsync(V2ConnectionCloseReason reason = V2ConnectionCloseReason.Abort)
    {
        if (reason == V2ConnectionCloseReason.Graceful)
            throw new ArgumentOutOfRangeException(nameof(reason), "Use CompleteAsync for a graceful close.");

        ClosePlan? closePlan;
        lock (_gate)
            closePlan = TransitionToClosingLocked(ToStopCause(reason));

        LaunchClose(closePlan);
        return _closeCompletion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
        {
            ClosePlan? closePlan = null;
            lock (_gate)
            {
                if (_state is V2ConnectionState.Created or V2ConnectionState.Running)
                    closePlan = TransitionToClosingLocked(V2ConnectionStopCause.Abort);
            }

            LaunchClose(closePlan);
        }

        await AwaitShutdownAsync();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(_connectionToken))
            {
                if (IsNonGracefulStopRequested())
                    break;

                foreach (var frame in message.Frames)
                {
                    Task sendTask;
                    try
                    {
                        sendTask = _sender.SendAsync(frame, _connectionToken).AsTask();
                    }
                    catch (Exception)
                    {
                        BeginClose(V2ConnectionStopCause.SendFailure);
                        return;
                    }

                    try
                    {
                        await sendTask.WaitAsync(
                            FrameSendTimeout,
                            _timeProvider,
                            _connectionToken);
                    }
                    catch (TimeoutException)
                    {
                        ObserveAbandonedSend(sendTask);
                        BeginClose(V2ConnectionStopCause.SendTimeout);
                        return;
                    }
                    catch (OperationCanceledException) when (_connectionToken.IsCancellationRequested)
                    {
                        ObserveAbandonedSend(sendTask);
                        BeginClose(V2ConnectionStopCause.Abort);
                        return;
                    }
                    catch (Exception)
                    {
                        BeginClose(V2ConnectionStopCause.SendFailure);
                        return;
                    }
                }
            }

            if (!IsNonGracefulStopRequested())
                BeginClose(V2ConnectionStopCause.Graceful);
        }
        catch (OperationCanceledException) when (_connectionToken.IsCancellationRequested)
        {
            BeginClose(V2ConnectionStopCause.Abort);
        }
        catch (Exception)
        {
            BeginClose(V2ConnectionStopCause.SendFailure);
        }
        finally
        {
            _pumpCompletion.TrySetResult();
        }
    }

    private void BeginClose(V2ConnectionStopCause cause)
    {
        ClosePlan? closePlan;
        lock (_gate)
            closePlan = TransitionToClosingLocked(cause);
        LaunchClose(closePlan);
    }

    private ClosePlan? TransitionToClosingLocked(V2ConnectionStopCause cause)
    {
        var encodedCause = (int)cause + 1;
        if (Interlocked.CompareExchange(ref _stopCause, encodedCause, 0) != 0)
            return null;

        var closeReason = ToCloseReason(cause);
        _state = V2ConnectionState.Closing;
        _diagnosticStopCause = cause;
        _closeReason = closeReason;
        _outbound.Writer.TryComplete();
        var cleanups = _cleanups.ToArray();
        _cleanups.Clear();
        return new ClosePlan(closeReason, cleanups, !_pumpStarted);
    }

    private void LaunchClose(ClosePlan? closePlan)
    {
        if (closePlan is not null)
            _ = CloseCoreAsync(closePlan);
    }

    private async Task CloseCoreAsync(ClosePlan closePlan)
    {
        // A producer that detects backpressure only schedules shutdown; it never runs cleanup or close callbacks inline.
        await Task.Yield();

        List<Exception>? failures = null;
        try
        {
            if (closePlan.CompletePump)
                _pumpCompletion.TrySetResult();

            try
            {
                await _connectionLifetime.CancelAsync();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            await _pumpCompletion.Task;

            foreach (var cleanup in closePlan.Cleanups)
            {
                try
                {
                    await cleanup.CleanupAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            try
            {
                Task closeTask;
                try
                {
                    closeTask = _sender.CloseAsync(closePlan.Reason, CancellationToken.None).AsTask();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                    closeTask = Task.CompletedTask;
                }

                try
                {
                    await closeTask.WaitAsync(FrameSendTimeout, _timeProvider, CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    ObserveAbandonedSend(closeTask);
                }
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        finally
        {
            _connectionLifetime.Dispose();
            lock (_gate)
                _state = V2ConnectionState.Closed;

            if (failures is null)
                _closeCompletion.TrySetResult();
            else
                _closeCompletion.TrySetException(new AggregateException("Closing the V2 connection failed.", failures));
        }
    }

    private async Task AwaitShutdownAsync()
    {
        await _pumpCompletion.Task;
        await _closeCompletion.Task;
    }

    private static void ObserveAbandonedSend(Task sendTask)
    {
        if (sendTask.IsCompleted)
        {
            _ = sendTask.Exception;
            return;
        }

        _ = sendTask.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool IsNonGracefulStopRequested()
    {
        var encodedCause = Volatile.Read(ref _stopCause);
        return encodedCause != 0 &&
               encodedCause != (int)V2ConnectionStopCause.Graceful + 1;
    }

    private static V2ConnectionStopCause ToStopCause(V2ConnectionCloseReason reason) => reason switch
    {
        V2ConnectionCloseReason.Graceful => V2ConnectionStopCause.Graceful,
        V2ConnectionCloseReason.SlowConsumer => V2ConnectionStopCause.SlowConsumer,
        V2ConnectionCloseReason.SendFailure => V2ConnectionStopCause.SendFailure,
        V2ConnectionCloseReason.Peer => V2ConnectionStopCause.Peer,
        V2ConnectionCloseReason.Abort => V2ConnectionStopCause.Abort,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static V2ConnectionCloseReason ToCloseReason(V2ConnectionStopCause cause) => cause switch
    {
        V2ConnectionStopCause.Graceful => V2ConnectionCloseReason.Graceful,
        V2ConnectionStopCause.SlowConsumer or V2ConnectionStopCause.SendTimeout => V2ConnectionCloseReason.SlowConsumer,
        V2ConnectionStopCause.SendFailure => V2ConnectionCloseReason.SendFailure,
        V2ConnectionStopCause.Peer => V2ConnectionCloseReason.Peer,
        V2ConnectionStopCause.Abort => V2ConnectionCloseReason.Abort,
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null)
    };

    private static ImmutableArray<string> NormalizePermissions(IEnumerable<string>? permissions)
    {
        if (permissions is null)
            return [];

        var normalized = permissions
            .Select(static permission =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(permission);
                return permission.Trim().ToLowerInvariant();
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return normalized;
    }

    private static Permissions CompilePermissions(ImmutableArray<string> permissions)
    {
        try
        {
            return new Permissions(permissions.ToArray());
        }
        catch (ArgumentException)
        {
            return MCServerLauncher.Daemon.Remote.Authentication.Permissions.Never;
        }
    }

    private sealed record ClosePlan(
        V2ConnectionCloseReason Reason,
        IV2ConnectionCleanup[] Cleanups,
        bool CompletePump);
}
