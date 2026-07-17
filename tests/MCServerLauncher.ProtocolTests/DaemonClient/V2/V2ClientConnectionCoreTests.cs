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
        await core.Closed;
        var results = await Task.WhenAll(requests);

        Assert.All(results, result =>
        {
            Assert.True(result.IsErr(out var error));
            Assert.Equal("connection.closed", error!.Code);
        });
        Assert.Equal(0, core.PendingCount);
        Assert.True(core.Closed.IsCompletedSuccessfully);
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
    public async Task DownloadJsonMetadataRegistersBeforeBinaryAndUsesOnePendingObject()
    {
        var transport = new DownloadTransport();
        var id = JsonRpcRequestId.FromString("download-read");
        var core = Core(transport, id);
        var session = DownloadSession(length: 4, maximumChunkSize: 4);
        Assert.True(core.TryRegisterDownloadSession(session, out _));

        var read = core.ReadDownloadChunkAsync(
            new DownloadChunkRequest(session.SessionId, 0, 4),
            CancellationToken.None);
        Assert.Contains("mcsl.file.download.read", transport.Text, StringComparison.Ordinal);
        Assert.Equal(1, core.PendingCount);
        Assert.Equal(1, core.DownloadPendingCount);

        core.RouteText(DownloadMetadata(id, session.SessionId, 0, 4, isFinal: true));
        Assert.False(read.IsCompleted);
        Assert.Equal(1, core.PendingCount);
        core.RouteBinary(DownloadFrame(session.SessionId, 0, 1, 2, 3, 4));

        Assert.True((await read).IsOk(out var chunk));
        Assert.True(chunk!.Data.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        Assert.True(chunk.IsFinal);
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
    }

    [Fact]
    public async Task DownloadCancellationAfterSendReturnsImmediatelyButDrainsLatePairAndPoisonsSession()
    {
        var transport = new DownloadTransport();
        var id = JsonRpcRequestId.FromString("download-abandoned");
        var core = Core(transport, id);
        var session = DownloadSession(length: 1, maximumChunkSize: 1);
        Assert.True(core.TryRegisterDownloadSession(session, out _));
        using var cancellation = new CancellationTokenSource();
        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(1, core.PendingCount);
        Assert.Equal(1, core.AbandonedDownloadCount);
        core.RouteText(DownloadMetadata(id, session.SessionId, 0, 1, isFinal: true));
        core.RouteBinary(DownloadFrame(session.SessionId, 0, 9));
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.AbandonedDownloadCount);

        var retry = await core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.Equal("file.download.session_poisoned", poisoned!.Code);
    }

    [Fact]
    public async Task DownloadErrorResponseCompletesWithoutWaitingForBinary()
    {
        var transport = new DownloadTransport();
        var id = JsonRpcRequestId.FromString("download-error");
        var core = Core(transport, id);
        var session = DownloadSession();
        Assert.True(core.TryRegisterDownloadSession(session, out _));
        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);

        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"download-error\",\"error\":{\"code\":-32000,\"message\":\"missing\",\"data\":{\"daemon_error_code\":\"file.session.not_found\",\"daemon_error_kind\":\"not_found\",\"correlation_id\":\"download\"}}}"));

        Assert.True((await read).IsErr(out var error));
        Assert.IsType<NotFoundDaemonError>(error);
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
    }

    [Fact]
    public async Task DownloadBinaryBeforeJsonUnknownAndDuplicateAreProtocolFaults()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var transport = new DownloadTransport();
        var id = JsonRpcRequestId.FromString("download-order");
        var core = Core(transport, id, diagnostics.Add);
        var session = DownloadSession(length: 1, maximumChunkSize: 1);
        Assert.True(core.TryRegisterDownloadSession(session, out _));
        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);

        core.RouteBinary(DownloadFrame(session.SessionId, 0, 1));
        Assert.True((await read).IsErr(out var error));
        Assert.Equal("protocol.download_binary_mismatch", error!.Code);
        core.RouteBinary(DownloadFrame(Guid.NewGuid(), 0, 1));

        Assert.True(diagnostics.Count(value => value.Kind == V2ClientDiagnosticKind.ProtocolFault) >= 2);
    }

    [Fact]
    public async Task DownloadCloseDrainsRequestAndSendObserver()
    {
        var transport = new BlockingDownloadTransport();
        var core = Core(transport, JsonRpcRequestId.FromString("download-close"));
        var session = DownloadSession();
        Assert.True(core.TryRegisterDownloadSession(session, out _));
        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);

        core.Close();
        Assert.True((await read).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task DownloadMetadataCanArriveSynchronouslyInsideTextSend()
    {
        V2ClientConnectionCore? core = null;
        var id = JsonRpcRequestId.FromString("download-sync");
        var session = DownloadSession(length: 1, maximumChunkSize: 1);
        var transport = new CallbackDownloadTransport(() =>
            core!.RouteText(DownloadMetadata(id, session.SessionId, 0, 1, isFinal: true)));
        core = Core(transport, id);
        Assert.True(core.TryRegisterDownloadSession(session, out _));

        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);

        Assert.False(read.IsCompleted);
        Assert.Equal(1, core.PendingCount);
        core.RouteBinary(DownloadFrame(session.SessionId, 0, 4));
        Assert.True((await read).IsOk(out _));
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task DownloadTimeoutRetainsOnePendingUntilLatePairDrains()
    {
        var time = new ManualTimeProvider();
        var id = JsonRpcRequestId.FromString("download-timeout");
        var core = new V2ClientConnectionCore(
            new DownloadTransport(),
            time,
            TimeSpan.FromSeconds(3),
            () => id);
        var session = DownloadSession(length: 1, maximumChunkSize: 1);
        Assert.True(core.TryRegisterDownloadSession(session, out _));
        var read = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True((await read).IsErr(out var error));
        Assert.Equal("request.timeout", error!.Code);
        Assert.Equal(1, core.PendingCount);
        Assert.Equal(1, core.AbandonedDownloadCount);
        core.RouteText(DownloadMetadata(id, session.SessionId, 0, 1, isFinal: true));
        core.RouteBinary(DownloadFrame(session.SessionId, 0, 5));
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.AbandonedDownloadCount);
    }

    [Fact]
    public async Task BlockingDownloadTextSendCannotBlockCloseAndCancellationDrainsReservation()
    {
        var transport = new SynchronouslyBlockingDownloadTransport();
        var core = Core(transport, JsonRpcRequestId.FromString("download-blocking-send"));
        var session = DownloadSession();
        Assert.True(core.TryRegisterDownloadSession(session, out _));

        var invoke = Task.Run(() => core.ReadDownloadChunkAsync(
            new DownloadChunkRequest(session.SessionId, 0, 1),
            CancellationToken.None));
        await transport.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(core.Close).WaitAsync(TimeSpan.FromSeconds(5));
        await transport.TokenCanceled.WaitAsync(TimeSpan.FromSeconds(5));
        var read = await invoke.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(read.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await core.WaitForSendObserversAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task DownloadRegistrationFailureRollsBackBothMapsAndAllowsSameIdAndSessionRetry()
    {
        var time = new ThrowOnceTimerTimeProvider();
        var transport = new DownloadTransport();
        var id = JsonRpcRequestId.FromString("download-register-retry");
        var core = new V2ClientConnectionCore(transport, time, Timeout, () => id);
        var session = DownloadSession(length: 1, maximumChunkSize: 1);
        Assert.True(core.TryRegisterDownloadSession(session, out _));

        var failed = await core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);
        Assert.True(failed.IsErr(out var registrationError));
        Assert.Equal("transport.request_registration_failed", registrationError!.Code);
        Assert.Equal(0, transport.SendCount);
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);

        var retry = core.ReadDownloadChunkAsync(new(session.SessionId, 0, 1), CancellationToken.None);
        Assert.Equal(1, transport.SendCount);
        core.RouteText(DownloadMetadata(id, session.SessionId, 0, 1, isFinal: true));
        core.RouteBinary(DownloadFrame(session.SessionId, 0, 3));
        Assert.True((await retry).IsOk(out _));
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(0, core.DownloadPendingCount);
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

        var diagnostic = Assert.Single(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ConsumerFault);
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

    [Fact]
    public async Task SynchronouslyBlockingBinarySendCannotHoldAdmissionLockAgainstClose()
    {
        var transport = new CancellationBlockingBinaryTransport();
        var core = new V2ClientConnectionCore(transport, TimeProvider.System, Timeout);
        var session = Guid.NewGuid();
        var upload = Task.Run(() => core.SendUploadChunkAsync(
            new UploadChunkRequest(session, 0, ImmutableArray.Create<byte>(1)),
            1,
            CancellationToken.None));
        await transport.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var close = Task.Run(core.Close);
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await transport.TokenCanceled.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            transport.ReleaseForFailedAssertion();
        }

        var result = await upload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await core.WaitForSendObserversAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, transport.BinarySendCount);
        Assert.Equal(0, core.UploadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task SynchronousUploadCancellationReleasesAdmissionBeforeCloseCanContinue()
    {
        var transport = new CloseBlockingCancellationBinaryTransport();
        var core = new V2ClientConnectionCore(transport, TimeProvider.System, Timeout);
        var upload = Task.Run(() => core.SendUploadChunkAsync(
            new UploadChunkRequest(Guid.NewGuid(), 0, ImmutableArray.Create<byte>(1)),
            1,
            CancellationToken.None));
        await transport.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        var close = Task.Run(core.Close);
        try
        {
            await transport.CloseBlocked.WaitAsync(TimeSpan.FromSeconds(5));

            var result = await upload.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(result.IsErr(out var error));
            Assert.Equal("connection.closed", error!.Code);
            Assert.False(close.IsCompleted);
            Assert.Equal(0, core.UploadPendingCount);
            Assert.Equal(0, core.SendObserverCount);
            Assert.Equal(0, core.ActiveSendLifetimeCount);
        }
        finally
        {
            transport.ReleaseClose();
        }

        await close.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AlreadyCanceledCallerRegistrationCompletesSynchronouslyAndPoisonsPending()
    {
        var time = new TrackingTimeProvider();
        var lifetimeCreated = 0;
        var lifetimeDisposed = 0;
        var coordinator = new V2ClientUploadCoordinator(
            time,
            Timeout,
            () => Interlocked.Increment(ref lifetimeCreated),
            () => Interlocked.Increment(ref lifetimeDisposed));
        var session = Guid.NewGuid();
        Assert.True(coordinator.TryAdmit(session, 0, 1, out var pending, out _));
        pending!.CreateSendLifetime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        pending.Register(cancellation.Token);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(pending.IsCompleted);
        Assert.Equal(0, time.CreateTimerCount);
        pending.DisposeSendLifetime();
        Assert.Equal(1, lifetimeCreated);
        Assert.Equal(1, lifetimeDisposed);
        Assert.False(coordinator.TryAdmit(session, 1, 1, out _, out var error));
        Assert.IsType<ConflictDaemonError>(error);
        Assert.Equal("file.upload.session_poisoned", error!.Code);
    }

    [Fact]
    public async Task CloseDuringTimerRegistrationDoesNotBlockAndSkipsBinarySend()
    {
        var time = new BlockingTimerCreationTimeProvider();
        var transport = new BinaryCountingTransport();
        var core = new V2ClientConnectionCore(transport, time, Timeout);
        // LongRunning: CreateTimer blocks until Close races, so avoid thread-pool starvation under
        // the full protocol suite (Task.Run alone can miss the 5s Entered wait on a busy CI runner).
        var upload = Task.Factory.StartNew(
                () => core.SendUploadChunkAsync(
                    new UploadChunkRequest(Guid.NewGuid(), 0, ImmutableArray.Create<byte>(1)),
                    1,
                    CancellationToken.None),
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();
        await time.Entered.WaitAsync(TimeSpan.FromSeconds(15));

        try
        {
            await Task.Factory.StartNew(
                    core.Close,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            time.Release();
        }

        var result = await upload.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        Assert.Equal(0, transport.BinarySendCount);
        Assert.True(time.TimerDisposed);
        Assert.Equal(0, core.UploadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task SynchronousTimeoutDuringRegistrationIsAmbiguousWithoutBinarySendAndPoisonsSession()
    {
        var time = new SynchronousTimeoutTimeProvider();
        var transport = new BinaryCountingTransport();
        var core = new V2ClientConnectionCore(transport, time, TimeSpan.FromSeconds(1));
        var session = Guid.NewGuid();

        var upload = core.SendUploadChunkTracked(
            new UploadChunkRequest(session, 0, ImmutableArray.Create<byte>(1)),
            1,
            CancellationToken.None);
        var disposition = ObserveDispositionAtCompletion(upload);
        var result = await upload.Completion;

        Assert.True(result.IsErr(out var error));
        Assert.Equal("request.timeout", error!.Code);
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            await disposition);
        Assert.Equal(0, transport.BinarySendCount);
        Assert.True(time.TimerDisposed);
        Assert.Equal(0, core.UploadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
        var retry = await core.SendUploadChunkAsync(
            new UploadChunkRequest(session, 1, ImmutableArray.Create<byte>(2)),
            1,
            CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
    }

    [Fact]
    public async Task UploadTimerRegistrationFailureIsAmbiguousWithoutBinarySendAndPoisonsSession()
    {
        var time = new ThrowOnceTimerTimeProvider();
        var transport = new BinaryCountingTransport();
        var core = new V2ClientConnectionCore(transport, time, Timeout);
        var session = Guid.NewGuid();

        var upload = core.SendUploadChunkTracked(
            new UploadChunkRequest(session, 0, ImmutableArray.Create<byte>(1)),
            1,
            CancellationToken.None);
        var disposition = ObserveDispositionAtCompletion(upload);
        var result = await upload.Completion;

        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.send_failed", error!.Code);
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            await disposition);
        Assert.Equal(0, transport.BinarySendCount);
        Assert.Equal(0, core.UploadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);

        var retry = await core.SendUploadChunkAsync(
            new UploadChunkRequest(session, 1, ImmutableArray.Create<byte>(2)),
            1,
            CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
        Assert.Equal("file.upload.session_poisoned", poisoned!.Code);
    }

    [Fact]
    public async Task TrackedJsonResponsesPublishAuthoritativeOutcomeBeforeCompletion()
    {
        var successCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-success"));
        var success = successCore.InvokeTracked(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"),
            new EmptyRequest());
        var successDisposition = ObserveDispositionAtCompletion(success);
        successCore.RouteText(Utf8(
            "{\"jsonrpc\":\"2.0\",\"id\":\"tracked-success\",\"result\":{\"time\":42}}"));

        Assert.True((await success.Completion).IsOk(out _));
        Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, await successDisposition);

        var errorCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-error"));
        var error = errorCore.InvokeTracked(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"),
            new EmptyRequest());
        var errorDisposition = ObserveDispositionAtCompletion(error);
        errorCore.RouteText(Utf8(
            "{\"jsonrpc\":\"2.0\",\"id\":\"tracked-error\",\"error\":{\"code\":-32000,\"message\":\"Rejected\",\"data\":{\"daemon_error_code\":\"remote.rejected\",\"daemon_error_kind\":\"conflict\",\"correlation_id\":\"tracked-test\"}}}"));

        Assert.True((await error.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, await errorDisposition);

        var invalidCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-invalid"));
        var invalid = invalidCore.InvokeTracked(
            Descriptor<DownloadChunkRequest, DownloadReadResult>("mcsl.file.download.read"),
            new DownloadChunkRequest(Guid.NewGuid(), 0, 1));
        invalidCore.RouteText(Utf8(
            "{\"jsonrpc\":\"2.0\",\"id\":\"tracked-invalid\",\"result\":{\"session_id\":\"00000000-0000-0000-0000-000000000000\",\"offset\":0,\"length\":1,\"is_final\":false}}"));

        Assert.True((await invalid.Completion).IsErr(out var invalidError));
        Assert.Equal("protocol.result_invalid", invalidError!.Code);
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            invalid.Outcome.Disposition);
    }

    [Fact]
    public async Task TrackedJsonLocalTerminalPathsPreserveAdmissionClassification()
    {
        var descriptor = Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping");

        var closedTransport = new CountingTransport();
        var closedCore = new V2ClientConnectionCore(closedTransport, TimeProvider.System, Timeout);
        closedCore.Close();
        var closed = closedCore.InvokeTracked(descriptor, new EmptyRequest());
        Assert.True((await closed.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, closed.Outcome.Disposition);
        Assert.Equal(0, closedTransport.SendCount);

        using var beforeCancellation = new CancellationTokenSource();
        beforeCancellation.Cancel();
        var beforeCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-before-cancel"));
        var before = beforeCore.InvokeTracked(descriptor, new EmptyRequest(), beforeCancellation.Token);
        var beforeDisposition = ObserveDispositionAtCompletion(before);
        var beforeException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => before.Completion);
        Assert.Equal(beforeCancellation.Token, beforeException.CancellationToken);
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, await beforeDisposition);

        using var afterCancellation = new CancellationTokenSource();
        var afterCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-after-cancel"));
        var after = afterCore.InvokeTracked(descriptor, new EmptyRequest(), afterCancellation.Token);
        var afterDisposition = ObserveDispositionAtCompletion(after);
        afterCancellation.Cancel();
        var afterException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => after.Completion);
        Assert.Equal(afterCancellation.Token, afterException.CancellationToken);
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            await afterDisposition);

        var time = new ManualTimeProvider();
        var timeoutCore = new V2ClientConnectionCore(
            new RecordingTransport(),
            time,
            TimeSpan.FromSeconds(1),
            () => JsonRpcRequestId.FromString("tracked-timeout"));
        var timedOut = timeoutCore.InvokeTracked(descriptor, new EmptyRequest());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await timedOut.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            timedOut.Outcome.Disposition);

        var syncFailure = Core(
            new RecordingTransport(fail: true),
            JsonRpcRequestId.FromString("tracked-sync-failure"))
            .InvokeTracked(descriptor, new EmptyRequest());
        Assert.True((await syncFailure.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            syncFailure.Outcome.Disposition);

        var asyncFailureCore = Core(
            new AsynchronousTextFailureTransport(),
            JsonRpcRequestId.FromString("tracked-async-failure"));
        var asyncFailure = asyncFailureCore.InvokeTracked(descriptor, new EmptyRequest());
        Assert.True((await asyncFailure.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            asyncFailure.Outcome.Disposition);
        await asyncFailureCore.WaitForSendObserversAsync();

        var closeCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-close"));
        var close = closeCore.InvokeTracked(descriptor, new EmptyRequest());
        closeCore.Close();
        Assert.True((await close.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            close.Outcome.Disposition);

        var duplicateCore = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-duplicate"));
        var first = duplicateCore.InvokeTracked(descriptor, new EmptyRequest());
        var duplicate = duplicateCore.InvokeTracked(descriptor, new EmptyRequest());
        await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate.Completion);
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, duplicate.Outcome.Disposition);
        duplicateCore.Close();
        await first.Completion;
    }

    [Fact]
    public async Task TrackedUnitMappingRetainsOutcomeThroughMappedCompletion()
    {
        var core = Core(new RecordingTransport(), JsonRpcRequestId.FromString("tracked-unit"));
        var operation = core.InvokeUnitTracked(
            Descriptor<PathRequest, UnitResult>("mcsl.directory.create"),
            new PathRequest("world/data"));
        var outcome = operation.Outcome;
        var disposition = ObserveDispositionAtCompletion(operation);

        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"id\":\"tracked-unit\",\"result\":{}}"));

        Assert.True((await operation.Completion).IsOk(out _));
        Assert.Same(outcome, operation.Outcome);
        Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, await disposition);
    }

    [Fact]
    public async Task ClosedCompletesOnlyAfterWinningCloseFinishesReentrantCleanup()
    {
        var transport = new ReentrantCloseTransport();
        var core = Core(transport, JsonRpcRequestId.FromString("reentrant-close"));
        var operation = core.InvokeTracked(
            Descriptor<EmptyRequest, PingResult>("mcsl.daemon.ping"),
            new EmptyRequest());
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        var reentrantSawClosed = true;
        transport.OnCancellation = () =>
        {
            core.Close();
            reentrantSawClosed = core.Closed.IsCompleted;
            callbackEntered.TrySetResult();
            if (!releaseCallback.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The reentrant close callback was not released.");
        };

        var winningClose = Task.Run(core.Close);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        core.Close();
        Assert.False(core.Closed.IsCompleted);
        Assert.False(reentrantSawClosed);

        releaseCallback.Set();
        await winningClose.WaitAsync(TimeSpan.FromSeconds(5));
        await core.Closed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(core.Closed.IsCompletedSuccessfully);
        Assert.True((await operation.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            operation.Outcome.Disposition);
        transport.Release();
        await core.WaitForSendObserversAsync();
    }

    private static V2ClientConnectionCore Core(IV2ClientWireTransport transport, JsonRpcRequestId id, Action<V2ClientDiagnostic>? diagnostic = null) =>
        new(transport, TimeProvider.System, Timeout, () => id, diagnostic);

    private static RpcDescriptor<TRequest, TResult> Descriptor<TRequest, TResult>(string method) =>
        Assert.IsType<RpcDescriptor<TRequest, TResult>>(BuiltInProtocolDefinitions.Rpcs.Single(value => value.Method.Value == method));

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static Task<V2ClientInvocationDisposition> ObserveDispositionAtCompletion<TResult>(
        V2ClientInvocationOperation<TResult> operation)
        where TResult : notnull =>
        operation.Completion.ContinueWith(
            _ => operation.Outcome.Disposition,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static DownloadSession DownloadSession(long length = 8, int maximumChunkSize = 4) =>
        new(Guid.NewGuid(), length, new string('b', 64), maximumChunkSize, DateTimeOffset.UtcNow.AddMinutes(5));

    private static byte[] DownloadMetadata(
        JsonRpcRequestId id,
        Guid sessionId,
        long offset,
        int length,
        bool isFinal) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"id\":\"{id.StringValue}\",\"result\":{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{length},\"is_final\":{isFinal.ToString().ToLowerInvariant()}}}}}");

    private static byte[] DownloadFrame(Guid sessionId, long offset, params byte[] payload)
    {
        var frame = new byte[BinaryFrameCodec.HeaderSize + payload.Length];
        Assert.True(BinaryFrameCodec.TryWrite(
            frame,
            new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, sessionId, offset, checked((uint)payload.Length)),
            payload,
            out var error));
        Assert.Equal(BinaryFrameWriteError.None, error);
        return frame;
    }

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

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class AsynchronousTextFailureTransport : IV2ClientWireTransport
    {
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            new(Task.FromException(new IOException("test-only asynchronous transport failure")));

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ReentrantCloseTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Action? OnCancellation { get; set; }

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => OnCancellation?.Invoke());
            return new(_completion.Task);
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        internal void Release() => _completion.TrySetResult();
    }

    private sealed class DownloadTransport : IV2ClientWireTransport
    {
        private ImmutableArray<byte> _text;
        internal int SendCount { get; private set; }
        internal string Text => Encoding.UTF8.GetString(_text.AsSpan());

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            SendCount++;
            _text = utf8Json;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class BlockingDownloadTransport : IV2ClientWireTransport
    {
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            new(Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken));

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class CallbackDownloadTransport(Action callback) : IV2ClientWireTransport
    {
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            callback();
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class SynchronouslyBlockingDownloadTransport : IV2ClientWireTransport
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tokenCanceled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Entered => _entered.Task;
        internal Task TokenCanceled => _tokenCanceled.Task;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            // Avoid CancellationToken.WaitHandle + Register Dispose races under Cancel().
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() =>
            {
                _tokenCanceled.TrySetResult();
                release.TrySetResult();
            });
            _entered.TrySetResult();
            if (cancellationToken.IsCancellationRequested)
                release.TrySetResult();

            if (!release.Task.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The download send did not observe connection cancellation.");

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The download send did not observe connection cancellation.");
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowOnceTimerTimeProvider : TimeProvider
    {
        private int _createCount;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Increment(ref _createCount) == 1)
                throw new InvalidOperationException("test-only timer registration failure");
            return new NoOpTimer();
        }

        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose()
            {
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            new(_completion.Task);
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
        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
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

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            new(_completion.Task);

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

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            new(_completion.Task);

        public void Release() => _completion.TrySetResult();
    }

    private sealed class BinaryCountingTransport : IV2ClientWireTransport
    {
        public int BinarySendCount;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref BinarySendCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationBlockingBinaryTransport : IV2ClientWireTransport
    {
        private readonly ManualResetEventSlim _failedAssertionRelease = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tokenCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public Task TokenCanceled => _tokenCanceled.Task;
        public int BinarySendCount;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref BinarySendCount);
            _entered.TrySetResult();
            var signaled = WaitHandle.WaitAny(
                [cancellationToken.WaitHandle, _failedAssertionRelease.WaitHandle],
                TimeSpan.FromSeconds(10));
            if (signaled == WaitHandle.WaitTimeout)
                throw new TimeoutException("The connection token was not canceled.");
            if (cancellationToken.IsCancellationRequested)
            {
                _tokenCanceled.TrySetResult();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException("The binary send was released after a failed assertion.");
        }

        public void ReleaseForFailedAssertion() => _failedAssertionRelease.Set();
    }

    private sealed class CloseBlockingCancellationBinaryTransport : IV2ClientWireTransport
    {
        private readonly ManualResetEventSlim _releaseClose = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closeBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public Task CloseBlocked => _closeBlocked.Task;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() =>
            {
                _closeBlocked.TrySetResult();
                _releaseClose.Wait();
            });
            _entered.TrySetResult();

            if (!cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The connection token was not canceled.");
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The binary send completed without cancellation.");
        }

        public void ReleaseClose() => _releaseClose.Set();
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        public int CreateTimerCount;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref CreateTimerCount);
            return new TrackingTimer();
        }
    }

    private sealed class BlockingTimerCreationTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        private TrackingTimer? _timer;
        public Task Entered => _entered.Task;
        public bool TimerDisposed => _timer?.Disposed ?? false;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _entered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test timer creation was not released.");
            return _timer = new TrackingTimer();
        }

        public void Release() => _release.Set();
    }

    private sealed class SynchronousTimeoutTimeProvider : TimeProvider
    {
        private TrackingTimer? _timer;
        public bool TimerDisposed => _timer?.Disposed ?? false;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            callback(state);
            return _timer = new TrackingTimer();
        }
    }

    private sealed class TrackingTimer : ITimer
    {
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
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
