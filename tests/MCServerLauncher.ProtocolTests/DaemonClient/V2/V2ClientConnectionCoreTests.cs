using System.Collections.Immutable;
using System.Text;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientConnectionCoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task NoParameterRequestHasCanonicalGoldenShapeAndTypedSuccess()
    {
        var transport = new RecordingTransport();
        var core = Core(transport, JsonRpcRequestId.FromString("request-1"));
        var descriptor = Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping");

        var task = core.InvokeAsync(descriptor, new EmptyRequest());
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.daemon.ping\",\"id\":\"request-1\"}", transport.Text);

        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"request-1\",\"result\":{\"time\":42}}"));
        var result = await task;

        Assert.True(result.IsOk(out var ping));
        Assert.Equal(42, ping!.Time);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task ObjectParameterUsesDescriptorMetadataAndInt64ResponseId()
    {
        var transport = new RecordingTransport();
        var core = Core(transport, JsonRpcRequestId.FromInt64(long.MaxValue));
        var descriptor = Descriptor<PathRequest, UnitResult>("mcsl.directory.create");

        var task = core.InvokeUnitAsync(descriptor, new PathRequest("world/data"));
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.directory.create\",\"id\":9223372036854775807,\"params\":{\"path\":\"world/data\"}}", transport.Text);
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":9223372036854775807,\"result\":{}}"));

        Assert.True((await task).IsOk(out _));
        Assert.Equal(0, core.PendingCount);
    }

    [Theory]
    [InlineData(-32001, "permission.denied", "permission", typeof(PermissionDaemonError))]
    [InlineData(-32000, "file.upload.incomplete", "conflict", typeof(ConflictDaemonError))]
    [InlineData(-32000, "file.chunk.too_large", "validation", typeof(ValidationDaemonError))]
    [InlineData(-32000, "file.session.expired", "not_found", typeof(NotFoundDaemonError))]
    [InlineData(-32000, "storage.failed", "storage", typeof(StorageDaemonError))]
    [InlineData(-32000, "connection.closed", "transport", typeof(TransportDaemonError))]
    [InlineData(-32005, "plugin.failed", "internal", typeof(InternalDaemonError))]
    public async Task RemoteErrorsMapToSafeConcreteDaemonErrors(int rpcCode, string daemonCode, string daemonKind, Type expectedType)
    {
        var core = Core(new RecordingTransport(), JsonRpcRequestId.FromString("e"));
        var task = core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":\"e\",\"error\":{{\"code\":{rpcCode},\"message\":\"Remote failure\",\"data\":{{\"daemon_error_code\":\"{daemonCode}\",\"daemon_error_kind\":\"{daemonKind}\",\"correlation_id\":\"c\"}}}}}}"));

        Assert.True((await task).IsErr(out var error));
        Assert.IsType(expectedType, error);
        Assert.Equal(daemonCode, error!.Code);
    }

    [Fact]
    public async Task CallerCancellationPreservesTokenAndRemovesPending()
    {
        using var cancellation = new CancellationTokenSource();
        var core = Core(new RecordingTransport(), JsonRpcRequestId.FromString("cancel"));
        var task = core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest(), cancellation.Token);

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task TimeoutUsesInjectedTimeProviderAndReturnsTypedTransportError()
    {
        var time = new ManualTimeProvider();
        var core = new V2ClientConnectionCore(new RecordingTransport(), time, TimeSpan.FromSeconds(30),
            () => JsonRpcRequestId.FromString("timeout"));
        var task = core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());

        time.Advance(TimeSpan.FromSeconds(30));
        var result = await task;

        Assert.True(result.IsErr(out var error));
        Assert.IsType<TransportDaemonError>(error);
        Assert.Equal("request.timeout", error!.Code);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task BlockingSendCannotDelayCancellationTimeoutOrCloseAndLateFaultIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelTransport = new BlockingTransport();
        var cancelCore = new V2ClientConnectionCore(cancelTransport, TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString("blocked-cancel"));
        var canceled = cancelCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest(), cancellation.Token);
        cancellation.Cancel();
        await cancelTransport.TokenCanceled;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(0, cancelCore.PendingCount);
        cancelTransport.Fail();
        await cancelCore.WaitForSendObserversAsync();
        Assert.Equal(0, cancelCore.SendObserverCount);

        var time = new ManualTimeProvider();
        var timeoutTransport = new BlockingTransport();
        var timeoutCore = new V2ClientConnectionCore(timeoutTransport, time, TimeSpan.FromSeconds(5),
            () => JsonRpcRequestId.FromString("blocked-timeout"));
        var timedOut = timeoutCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        time.Advance(TimeSpan.FromSeconds(5));
        await timeoutTransport.TokenCanceled;
        Assert.True((await timedOut).IsErr(out var timeoutError));
        Assert.Equal("request.timeout", timeoutError!.Code);
        Assert.Equal(0, timeoutCore.PendingCount);
        timeoutTransport.Succeed();
        await timeoutCore.WaitForSendObserversAsync();
        Assert.Equal(0, timeoutCore.SendObserverCount);

        var closeTransport = new BlockingTransport();
        var closeCore = new V2ClientConnectionCore(closeTransport, TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString("blocked-close"));
        var closed = closeCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        closeCore.Close();
        await closeTransport.TokenCanceled;
        Assert.True((await closed).IsErr(out var closeError));
        Assert.Equal("connection.closed", closeError!.Code);
        Assert.Equal(0, closeCore.PendingCount);
        closeTransport.Fail();
        await closeCore.WaitForSendObserversAsync();
        Assert.Equal(0, closeCore.SendObserverCount);
    }

    [Fact]
    public async Task CloseContainsThrowingTransportCancellationCallbackAndStillDrains()
    {
        var transport = new ThrowingCancellationTransport();
        var next = 0;
        var core = new V2ClientConnectionCore(transport, TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString($"throwing-close-{Interlocked.Increment(ref next)}"));
        var requests = Enumerable.Range(0, 4)
            .Select(_ => core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest()))
            .ToArray();

        core.Close();
        var results = await Task.WhenAll(requests);

        Assert.All(results, result =>
        {
            Assert.True(result.IsErr(out var error));
            Assert.Equal("connection.closed", error!.Code);
        });
        Assert.Equal(0, core.PendingCount);
        transport.Release();
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task ClosePreventsAnyLaterTransportAdmission()
    {
        var transport = new CountingTransport();
        var core = new V2ClientConnectionCore(transport, TimeProvider.System, Timeout);
        using var barrier = new Barrier(2);
        Result<PingResult, DaemonError>? raced = null;
        var worker = new Thread(() =>
        {
            barrier.SignalAndWait();
            raced = core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest())
                .GetAwaiter().GetResult();
        });
        worker.Start();
        core.Close();
        barrier.SignalAndWait();
        worker.Join();
        var results = new List<Result<PingResult, DaemonError>> { raced!.Value };
        for (var index = 0; index < 32; index++)
        {
            results.Add(await core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest()));
        }

        Assert.Equal(0, transport.SendCount);
        Assert.All(results, result => Assert.True(result.IsErr(out var error) && error!.Code == "connection.closed"));
    }

    [Fact]
    public async Task CloseWaitsForSynchronousAdmissionThenCancelsItAndRejectsConcurrentFollowers()
    {
        var transport = new SynchronouslyEnteringTransport();
        var core = new V2ClientConnectionCore(transport, TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString(Guid.NewGuid().ToString("D")));
        var admitted = Task.Run(() => core.InvokeAsync(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest()));
        Assert.True(transport.Entered.Wait(TimeSpan.FromSeconds(5)));
        var close = Task.Run(core.Close);
        Assert.False(close.IsCompleted);

        transport.ReleaseReturn.Set();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.TokenCanceled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await admitted).IsErr(out var closed) && closed!.Code == "connection.closed");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var followers = Enumerable.Range(0, 32).Select(async _ =>
        {
            await start.Task;
            return await core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(followers);
        Assert.Equal(1, transport.SendCount);
        Assert.All(results, result => Assert.True(result.IsErr(out var error) && error!.Code == "connection.closed"));
        transport.Succeed();
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task CompletionRacesHaveOneWinnerAndNoPendingResidue()
    {
        using var caller = new CancellationTokenSource();
        var cancelCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("response-cancel"));
        var responseCancel = cancelCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest(), caller.Token);
        using (var barrier = new Barrier(3))
        {
            var response = Task.Run(() => { barrier.SignalAndWait(); cancelCore.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"response-cancel\",\"result\":{\"time\":1}}")); });
            var cancel = Task.Run(() => { barrier.SignalAndWait(); caller.Cancel(); });
            barrier.SignalAndWait();
            await Task.WhenAll(response, cancel);
        }
        try { await responseCancel; } catch (OperationCanceledException) { }
        Assert.Equal(0, cancelCore.PendingCount);

        var time = new ManualTimeProvider();
        var timeoutTransport = new BlockingTransport();
        var timeoutCore = new V2ClientConnectionCore(timeoutTransport, time, TimeSpan.FromSeconds(1),
            () => JsonRpcRequestId.FromString("timeout-failure"));
        var timeoutFailure = timeoutCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        using (var barrier = new Barrier(3))
        {
            var timeout = Task.Run(() => { barrier.SignalAndWait(); time.Advance(TimeSpan.FromSeconds(1)); });
            var failure = Task.Run(() => { barrier.SignalAndWait(); timeoutTransport.Fail(); });
            barrier.SignalAndWait();
            await Task.WhenAll(timeout, failure);
        }
        Assert.True((await timeoutFailure).IsErr(out _));
        await timeoutCore.WaitForSendObserversAsync();
        Assert.Equal(0, timeoutCore.PendingCount);

        var closeCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("close-response"));
        var closeResponse = closeCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        using (var barrier = new Barrier(3))
        {
            var close = Task.Run(() => { barrier.SignalAndWait(); closeCore.Close(); });
            var response = Task.Run(() => { barrier.SignalAndWait(); closeCore.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"close-response\",\"result\":{\"time\":2}}")); });
            barrier.SignalAndWait();
            await Task.WhenAll(close, response);
        }
        await closeResponse;
        Assert.Equal(0, closeCore.PendingCount);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", 0)]
    [InlineData("11111111-1111-1111-1111-111111111111", -1)]
    public async Task InvalidTypedResultBecomesProtocolTransportError(string sessionId, long offset)
    {
        var core = Core(new RecordingTransport(), JsonRpcRequestId.FromString("invalid-result"));
        var task = core.InvokeAsync(
            Descriptor<DownloadChunkRequest, DownloadReadResult>("mcsl.file.download.read"),
            new DownloadChunkRequest(Guid.NewGuid(), 0, 1));
        core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":\"invalid-result\",\"result\":{{\"session_id\":\"{sessionId}\",\"offset\":{offset},\"length\":1,\"is_final\":false}}}}"));

        Assert.True((await task).IsErr(out var error));
        Assert.IsType<TransportDaemonError>(error);
        Assert.Equal("protocol.result_invalid", error!.Code);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task SendFailureAndCloseReturnTypedTransportErrorsAndDrain()
    {
        var failing = Core(new RecordingTransport(fail: true), JsonRpcRequestId.FromString("send"));
        var send = await failing.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        Assert.True(send.IsErr(out var sendError));
        Assert.IsType<TransportDaemonError>(sendError);
        Assert.Equal(0, failing.PendingCount);

        var next = 0;
        var core = new V2ClientConnectionCore(new RecordingTransport(), TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString($"close-{Interlocked.Increment(ref next)}"));
        var requests = Enumerable.Range(0, 40)
            .Select(_ => core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest()))
            .ToArray();
        core.Close();
        var results = await Task.WhenAll(requests);

        Assert.All(results, result =>
        {
            Assert.True(result.IsErr(out var error));
            Assert.IsType<TransportDaemonError>(error);
        });
        Assert.Equal(0, core.PendingCount);
        var afterClose = await core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        Assert.True(afterClose.IsErr(out var closed));
        Assert.Equal("connection.closed", closed!.Code);
    }

    [Fact]
    public async Task NonAdmittedPathsDisposeSendLifetimeWithoutResidualRegistrations()
    {
        var closedCore = new V2ClientConnectionCore(new CountingTransport(), TimeProvider.System, Timeout);
        closedCore.Close();
        await closedCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        Assert.Equal(0, closedCore.ActiveSendLifetimeCount);

        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();
        var canceledCore = new V2ClientConnectionCore(new CountingTransport(), TimeProvider.System, Timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledCore.InvokeAsync(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest(), preCanceled.Token));
        Assert.Equal(0, canceledCore.ActiveSendLifetimeCount);

        var throwingCore = Core(new RecordingTransport(fail: true), JsonRpcRequestId.FromString("sync-throw"));
        Assert.True((await throwingCore.InvokeAsync(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest())).IsErr(out _));
        Assert.Equal(0, throwingCore.ActiveSendLifetimeCount);

        var blocking = new BlockingTransport();
        var duplicateCore = new V2ClientConnectionCore(blocking, TimeProvider.System, Timeout,
            () => JsonRpcRequestId.FromString("duplicate"));
        var first = duplicateCore.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        Assert.Equal(1, duplicateCore.ActiveSendLifetimeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => duplicateCore.InvokeAsync(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest()));
        Assert.Equal(1, duplicateCore.ActiveSendLifetimeCount);
        duplicateCore.Close();
        blocking.Succeed();
        await first;
        await duplicateCore.WaitForSendObserversAsync();
        Assert.Equal(0, duplicateCore.ActiveSendLifetimeCount);
        Assert.Equal(0, duplicateCore.PendingCount);
    }

    [Fact]
    public async Task UnknownLateMalformedBatchBomAndUnmappedMessagesAreDiagnosticsNotExceptions()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var core = Core(new RecordingTransport(), JsonRpcRequestId.FromString("known"), diagnostics.Add);
        var task = core.InvokeAsync(Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"), new EmptyRequest());
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"other\",\"result\":{\"time\":1}}"));
        core.RouteText(Utf8("[{\"jsonrpc\":\"2.0\"}]"));
        core.RouteText([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"known\",\"result\":{},\"extra\":1}"));
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"known\",\"id\":\"known\",\"result\":{}}"));
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"known\",\"result\":{\"time\":9}}"));
        await task;
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"known\",\"result\":{\"time\":9}}"));

        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.UnknownResponse);
        Assert.True(diagnostics.Count(value => value.Kind == V2ClientDiagnosticKind.ProtocolFault) >= 4);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void RemoteEventsAndUploadAcknowledgementsUseSeparateCallbacks()
    {
        var events = new List<JsonRpcRemoteEventNotification>();
        var acknowledgements = new List<UploadChunkAcknowledgement>();
        var core = new V2ClientConnectionCore(new RecordingTransport(), TimeProvider.System, Timeout,
            remoteEvent: events.Add, uploadAcknowledgement: acknowledgements.Add);
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":2,\"data\":null}}"));
        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.notification\",\"params\":{\"sequence\":2,\"timestamp\":3,\"meta\":{\"instance_id\":\"x\"},\"data\":{}}}"));
        var session = Guid.NewGuid();
        core.RouteText(Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{session:D}\",\"offset\":0,\"length\":4,\"status\":\"accepted\"}}}}"));

        Assert.Equal(2, events.Count);
        Assert.Equal(JsonRpcOptionalPayloadKind.Missing, events[0].Params.Meta.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.ExplicitNull, events[0].Params.Data.Kind);
        Assert.Equal(JsonRpcOptionalPayloadKind.Value, events[1].Params.Meta.Kind);
        Assert.Single(acknowledgements);
        Assert.Equal(session, acknowledgements[0].SessionId);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void ThrowingNotificationConsumersAreContainedAndDiagnosedWithoutSensitiveData(bool acknowledgement, int exceptionKind)
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        Action thrower = () =>
        {
            if (exceptionKind == 1)
            {
                throw new System.Text.Json.JsonException("secret-payload");
            }

            if (exceptionKind == 2)
            {
                throw new OperationCanceledException("secret-payload");
            }

            throw new InvalidOperationException("secret-payload");
        };
        var core = new V2ClientConnectionCore(new RecordingTransport(), TimeProvider.System, Timeout,
            diagnostic: diagnostics.Add,
            remoteEvent: _ => thrower(),
            uploadAcknowledgement: _ => thrower());
        var json = acknowledgement
            ? $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{Guid.NewGuid():D}\",\"offset\":0,\"length\":1,\"status\":\"accepted\"}}}}"
            : "{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":2}}";

        core.RouteText(Utf8(json));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(V2ClientDiagnosticKind.ConsumerFault, diagnostic.Kind);
        Assert.DoesNotContain("secret", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThrowingDiagnosticSinkCannotLeakConsumerFailure()
    {
        var core = new V2ClientConnectionCore(new RecordingTransport(), TimeProvider.System, Timeout,
            diagnostic: _ => throw new OperationCanceledException("diagnostic failed"),
            remoteEvent: _ => throw new InvalidOperationException("consumer failed"));

        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.event.daemon.report\",\"params\":{\"sequence\":1,\"timestamp\":2}}"));
    }

    private static V2ClientConnectionCore Core(RecordingTransport transport, JsonRpcRequestId id, Action<V2ClientDiagnostic>? diagnostic = null) =>
        new(transport, TimeProvider.System, Timeout, () => id, diagnostic);

    private static RpcDescriptor<TRequest, TResult> Descriptor<TRequest, TResult>(string method) =>
        Assert.IsType<RpcDescriptor<TRequest, TResult>>(BuiltInProtocolDefinitions.Rpcs.Single(value => value.Method.Value == method));

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class RecordingTransport(bool fail = false) : IV2ClientWireTransport
    {
        private ImmutableArray<byte> _bytes;
        public string Text => Encoding.UTF8.GetString(_bytes.AsSpan());

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            if (fail) throw new IOException("test-only transport failure");
            _bytes = utf8Json;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tokenCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task TokenCanceled => _tokenCanceled.Task;
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => _tokenCanceled.TrySetResult());
            return new(_completion.Task);
        }
        public void Succeed() => _completion.TrySetResult();
        public void Fail() => _completion.TrySetException(new IOException("late test failure"));
    }

    private sealed class CountingTransport : IV2ClientWireTransport
    {
        public int SendCount;
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref SendCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SynchronouslyEnteringTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tokenCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim ReleaseReturn { get; } = new();
        public Task TokenCanceled => _tokenCanceled.Task;
        public int SendCount;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref SendCount);
            cancellationToken.Register(() => _tokenCanceled.TrySetResult());
            Entered.Set();
            if (!ReleaseReturn.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return new(_completion.Task);
        }

        public void Succeed() => _completion.TrySetResult();
    }

    private sealed class ThrowingCancellationTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            cancellationToken.Register(static () => throw new InvalidOperationException("test cancellation callback"));
            return new(_completion.Task);
        }

        public void Release() => _completion.TrySetResult();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            foreach (var timer in _timers.ToArray())
            {
                timer.Advance(elapsed);
            }
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan remaining) : ITimer
        {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                remaining = dueTime;
                return !_disposed;
            }

            public void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (!_disposed && remaining <= TimeSpan.Zero)
                {
                    _disposed = true;
                    callback(state);
                }
            }

            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
