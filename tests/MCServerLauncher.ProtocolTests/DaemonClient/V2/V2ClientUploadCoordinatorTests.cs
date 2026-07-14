using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.DaemonClient.Connection.V2;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class V2ClientUploadCoordinatorTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ValidationRejectsInvalidRequestsWithoutAdmissionOrTransport()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var session = Guid.NewGuid();
        var cases = new (UploadChunkRequest Request, int Maximum, string Code)[]
        {
            (new(session, 0, default), 1, "file.chunk.data.invalid"),
            (new(Guid.Empty, 0, ImmutableArray<byte>.Empty), 1, "file.session.invalid"),
            (new(session, -1, ImmutableArray<byte>.Empty), 1, "file.chunk.offset.invalid"),
            (new(session, 0, ImmutableArray<byte>.Empty), 0, "file.chunk.size.invalid"),
            (new(session, 0, ImmutableArray<byte>.Empty), (int)BinaryFrameCodec.DefaultMaximumChunkSize + 1, "file.chunk.size.invalid"),
            (new(session, 0, ImmutableArray.Create<byte>(1, 2)), 1, "file.chunk.size.invalid")
        };

        foreach (var item in cases)
        {
            var result = await core.SendUploadChunkAsync(item.Request, item.Maximum, CancellationToken.None);
            Assert.True(result.IsErr(out var error));
            Assert.IsType<ValidationDaemonError>(error);
            Assert.Equal(item.Code, error!.Code);
        }

        Assert.Equal(0, transport.BinarySendCount);
        Assert.Equal(0, core.UploadPendingCount);
    }

    [Fact]
    public async Task BinaryFrameHasGoldenHeaderOpcodeAndPayload()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var session = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var payload = ImmutableArray.Create<byte>(0x10, 0x20, 0x30, 0x40);
        const long offset = 0x0102030405060708;

        var task = core.SendUploadChunkAsync(new(session, offset, payload), payload.Length, CancellationToken.None);
        var frame = Assert.Single(transport.Frames);

        Assert.Equal(BinaryFrameCodec.HeaderSize + payload.Length, frame.Length);
        Assert.Equal(BinaryFrameCodec.CurrentVersion, frame[0]);
        Assert.Equal((byte)BinaryFrameKind.UploadChunk, frame[1]);
        Assert.Equal(0, frame[2]);
        Assert.Equal(0, frame[3]);
        Assert.Equal(session, new Guid(frame.AsSpan().Slice(4, 16), bigEndian: true));
        Assert.Equal(offset, BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan().Slice(20, 8)));
        Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan().Slice(28, 4)));
        Assert.True(frame.AsSpan()[BinaryFrameCodec.HeaderSize..].SequenceEqual(payload.AsSpan()));

        core.RouteText(Accepted(session, offset, payload.Length));
        Assert.True((await task).IsOk(out _));
    }

    [Fact]
    public async Task ZeroAndExactMaximumPayloadsAreValid()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var emptySession = Guid.NewGuid();
        var empty = core.SendUploadChunkAsync(
            new(emptySession, 0, ImmutableArray<byte>.Empty),
            1,
            CancellationToken.None);
        core.RouteText(Accepted(emptySession, 0, 0));
        Assert.True((await empty).IsOk(out _));

        var maximum = (int)BinaryFrameCodec.DefaultMaximumChunkSize;
        var maximumSession = Guid.NewGuid();
        var data = ImmutableArray.Create(new byte[maximum]);
        var exact = core.SendUploadChunkAsync(new(maximumSession, 0, data), maximum, CancellationToken.None);
        var frame = transport.Frames[^1];
        Assert.Equal(BinaryFrameCodec.HeaderSize + maximum, frame.Length);
        Assert.True(BinaryFrameCodec.TryRead(frame.AsSpan(), out var header, out var error));
        Assert.Equal(BinaryFrameReadError.None, error);
        Assert.Equal((uint)maximum, header!.PayloadLength);
        core.RouteText(Accepted(maximumSession, 0, maximum));
        Assert.True((await exact).IsOk(out _));
    }

    [Fact]
    public async Task SameSessionIsSingleFlightAndAcceptedAcknowledgementMakesItAvailable()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var session = Guid.NewGuid();
        var first = core.SendUploadChunkAsync(Chunk(session, 0, 1), 4, CancellationToken.None);

        var conflict = await core.SendUploadChunkAsync(Chunk(session, 1, 2), 4, CancellationToken.None);
        Assert.True(conflict.IsErr(out var conflictError));
        Assert.IsType<ConflictDaemonError>(conflictError);
        Assert.Equal("file.upload.chunk_pending", conflictError!.Code);

        core.RouteText(Accepted(session, 0, 1));
        Assert.True((await first).IsOk(out _));
        var retry = core.SendUploadChunkAsync(Chunk(session, 1, 2), 4, CancellationToken.None);
        core.RouteText(Accepted(session, 1, 1));
        Assert.True((await retry).IsOk(out _));
        Assert.Equal(2, transport.BinarySendCount);
    }

    [Fact]
    public async Task DifferentSessionsProceedIndependently()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var first = core.SendUploadChunkAsync(Chunk(firstSession, 0, 1), 1, CancellationToken.None);
        var second = core.SendUploadChunkAsync(Chunk(secondSession, 7, 2), 1, CancellationToken.None);

        Assert.Equal(2, transport.BinarySendCount);
        Assert.Equal(2, core.UploadPendingCount);
        core.RouteText(Accepted(secondSession, 7, 1));
        core.RouteText(Accepted(firstSession, 0, 1));
        Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.IsOk(out _)));
    }

    [Theory]
    [InlineData("validation", typeof(ValidationDaemonError))]
    [InlineData("not_found", typeof(NotFoundDaemonError))]
    [InlineData("conflict", typeof(ConflictDaemonError))]
    [InlineData("permission", typeof(PermissionDaemonError))]
    [InlineData("storage", typeof(StorageDaemonError))]
    [InlineData("transport", typeof(TransportDaemonError))]
    [InlineData("internal", typeof(InternalDaemonError))]
    public async Task RejectedAcknowledgementMapsEveryWireErrorAndPoisonsSession(
        string wireKind,
        Type expectedType)
    {
        var core = Core(new BinaryTransport());
        var session = Guid.NewGuid();
        var task = core.SendUploadChunkAsync(Chunk(session, 3, 1), 1, CancellationToken.None);

        core.RouteText(Rejected(session, 3, 1, wireKind));
        var result = await task;
        Assert.True(result.IsErr(out var error));
        Assert.IsType(expectedType, error);
        Assert.Equal("remote.upload_rejected", error!.Code);

        var retry = await core.SendUploadChunkAsync(Chunk(session, 4, 2), 1, CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
        Assert.Equal("file.upload.session_poisoned", poisoned!.Code);
    }

    [Fact]
    public async Task MalformedKnownAcknowledgementIsProtocolFaultNotUnknownNotification()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var core = Core(new BinaryTransport(), diagnostic: diagnostics.Add);
        var session = Guid.NewGuid();
        var task = core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);

        core.RouteText(Utf8("{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{}}"));

        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
        Assert.DoesNotContain(diagnostics, value => value.Kind == V2ClientDiagnosticKind.UnknownNotification);
        Assert.False(task.IsCompleted);
        core.Close();
        Assert.True((await task).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
    }

    [Fact]
    public async Task SameSessionTupleMismatchPoisonsAndFaultsProtocol()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var core = Core(new BinaryTransport(), diagnostic: diagnostics.Add);
        var session = Guid.NewGuid();
        var task = core.SendUploadChunkAsync(Chunk(session, 5, 1, 2), 2, CancellationToken.None);

        core.RouteText(Accepted(session, 6, 2));

        Assert.True((await task).IsErr(out var error));
        Assert.IsType<TransportDaemonError>(error);
        Assert.Equal("protocol.upload_ack_mismatch", error!.Code);
        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
        var retry = await core.SendUploadChunkAsync(Chunk(session, 7, 3), 2, CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
    }

    [Fact]
    public async Task WrongDuplicateAndLateAcknowledgementsAreDiagnosticOnly()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var core = Core(new BinaryTransport(), diagnostic: diagnostics.Add);
        var session = Guid.NewGuid();
        var task = core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);

        core.RouteText(Accepted(Guid.NewGuid(), 0, 1));
        Assert.False(task.IsCompleted);
        core.RouteText(Accepted(session, 0, 1));
        Assert.True((await task).IsOk(out _));
        core.RouteText(Accepted(session, 0, 1));

        using var cancellation = new CancellationTokenSource();
        var lateSession = Guid.NewGuid();
        var late = core.SendUploadChunkAsync(Chunk(lateSession, 0, 1), 1, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => late);
        core.RouteText(Accepted(lateSession, 0, 1));

        Assert.True(diagnostics.Count(value => value.Kind == V2ClientDiagnosticKind.UnknownNotification) >= 3);
        Assert.DoesNotContain(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
    }

    [Fact]
    public async Task InternalAcknowledgementWinsBeforeObserverAndCanArriveSynchronouslyDuringSend()
    {
        V2ClientConnectionCore? core = null;
        Task<Result<Unit, DaemonError>>? upload = null;
        var observerSawCompletion = false;
        var sawLifetime = false;
        var session = Guid.NewGuid();
        var transport = new BinaryTransport((_, _) =>
        {
            sawLifetime = core!.ActiveSendLifetimeCount == 1 && core.SendObserverCount == 1;
            core.RouteText(Accepted(session, 0, 1));
            return ValueTask.CompletedTask;
        });
        core = new V2ClientConnectionCore(
            transport,
            TimeProvider.System,
            RequestTimeout,
            uploadAcknowledgement: _ => observerSawCompletion = upload?.IsCompleted ?? true);

        upload = core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);

        Assert.True((await upload).IsOk(out _));
        Assert.True(sawLifetime);
        Assert.True(observerSawCompletion);
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task CallerCancellationBeforeAdmissionDoesNotPoisonAndAfterAdmissionDoes()
    {
        var transport = new BinaryTransport();
        var core = Core(transport);
        var session = Guid.NewGuid();
        using var before = new CancellationTokenSource();
        before.Cancel();

        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, before.Token));
        Assert.Equal(before.Token, canceled.CancellationToken);
        Assert.Equal(0, transport.BinarySendCount);

        using var after = new CancellationTokenSource();
        var admitted = core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, after.Token);
        var transportToken = Assert.Single(transport.BinaryTokens);
        after.Cancel();
        canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admitted);
        Assert.Equal(after.Token, canceled.CancellationToken);
        Assert.False(transportToken.IsCancellationRequested);

        var retry = await core.SendUploadChunkAsync(Chunk(session, 1, 2), 1, CancellationToken.None);
        Assert.True(retry.IsErr(out var error));
        Assert.IsType<ConflictDaemonError>(error);
        core.Close();
        Assert.True(transportToken.IsCancellationRequested);
    }

    [Fact]
    public async Task TimeoutUsesManualTimeProviderAndPoisonsWithoutCancelingSend()
    {
        var time = new ManualTimeProvider();
        var transport = new BinaryTransport();
        var core = new V2ClientConnectionCore(transport, time, TimeSpan.FromSeconds(3));
        var session = Guid.NewGuid();
        var task = core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);
        var transportToken = Assert.Single(transport.BinaryTokens);

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True((await task).IsErr(out var error));
        Assert.Equal("request.timeout", error!.Code);
        Assert.False(transportToken.IsCancellationRequested);
        var retry = await core.SendUploadChunkAsync(Chunk(session, 1, 2), 1, CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynchronousAndAsynchronousSendFailurePoisonAndTerminateEpoch(bool synchronous)
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var transport = new BinaryTransport((_, _) =>
        {
            if (synchronous)
                throw new IOException("test send failure");
            return new ValueTask(Task.FromException(new IOException("test send failure")));
        });
        var core = Core(transport, diagnostic: diagnostics.Add);
        var session = Guid.NewGuid();

        var result = await core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);
        Assert.True(result.IsErr(out var error));
        Assert.Equal("transport.send_failed", error!.Code);
        await core.WaitForSendObserversAsync();
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
        var retry = await core.SendUploadChunkAsync(Chunk(session, 1, 2), 1, CancellationToken.None);
        Assert.True(retry.IsErr(out var poisoned));
        Assert.IsType<ConflictDaemonError>(poisoned);
    }

    [Fact]
    public async Task CloseCompletesPendingThenConnectionCancellationTerminatesAndDrainsSend()
    {
        var diagnostics = new List<V2ClientDiagnostic>();
        var transport = new ConnectionCanceledTransport();
        var core = Core(transport, diagnostic: diagnostics.Add);
        var task = core.SendUploadChunkAsync(Chunk(Guid.NewGuid(), 0, 1), 1, CancellationToken.None);
        Assert.Equal(1, core.UploadPendingCount);
        Assert.Equal(1, core.ActiveSendLifetimeCount);

        core.Close();
        Assert.True((await task).IsErr(out var error));
        Assert.Equal("connection.closed", error!.Code);
        await core.WaitForSendObserversAsync();

        Assert.Equal(0, core.UploadPendingCount);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
    }

    [Fact]
    public async Task PoisonIsConfinedToOneCoreEpoch()
    {
        var session = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var first = Core(new BinaryTransport());
        var poisoned = first.SendUploadChunkAsync(Chunk(session, 0, 1), 1, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poisoned);

        var second = Core(new BinaryTransport());
        var upload = second.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);
        second.RouteText(Accepted(session, 0, 1));
        Assert.True((await upload).IsOk(out _));
    }

    [Fact]
    public async Task AcknowledgementWinsCallerResultWhenTransportThenThrowsButEpochStillTerminates()
    {
        V2ClientConnectionCore? core = null;
        var diagnostics = new List<V2ClientDiagnostic>();
        var session = Guid.NewGuid();
        var transport = new BinaryTransport((_, _) =>
        {
            core!.RouteText(Accepted(session, 0, 1));
            throw new IOException("throw after acknowledgement");
        });
        core = Core(transport, diagnostic: diagnostics.Add);

        var result = await core.SendUploadChunkAsync(Chunk(session, 0, 1), 1, CancellationToken.None);

        Assert.True(result.IsOk(out _));
        Assert.Contains(diagnostics, value => value.Kind == V2ClientDiagnosticKind.ProtocolFault);
        Assert.Equal(0, core.SendObserverCount);
        Assert.Equal(0, core.ActiveSendLifetimeCount);
    }

    [Fact]
    public async Task TrackedUploadPreAdmissionPathsPublishNotAdmittedBeforeCompletion()
    {
        var validationTransport = new BinaryTransport();
        var validationCore = Core(validationTransport);
        var validation = validationCore.SendUploadChunkTracked(
            new UploadChunkRequest(Guid.NewGuid(), 0, default),
            1,
            CancellationToken.None);
        Assert.True((await validation.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, validation.Outcome.Disposition);
        Assert.Equal(0, validationTransport.BinarySendCount);

        var closedTransport = new BinaryTransport();
        var closedCore = Core(closedTransport);
        closedCore.Close();
        var closed = closedCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            CancellationToken.None);
        Assert.True((await closed.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, closed.Outcome.Disposition);
        Assert.Equal(0, closedTransport.BinarySendCount);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceledTransport = new BinaryTransport();
        var canceledCore = Core(canceledTransport);
        var canceled = canceledCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            cancellation.Token);
        var canceledDisposition = ObserveDispositionAtCompletion(canceled);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.Completion);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, await canceledDisposition);
        Assert.Equal(0, canceledTransport.BinarySendCount);

        var conflictTransport = new BinaryTransport();
        var conflictCore = Core(conflictTransport);
        var conflictSession = Guid.NewGuid();
        var first = conflictCore.SendUploadChunkTracked(
            Chunk(conflictSession, 0, 1),
            1,
            CancellationToken.None);
        var conflict = conflictCore.SendUploadChunkTracked(
            Chunk(conflictSession, 1, 2),
            1,
            CancellationToken.None);
        Assert.True((await conflict.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.NotAdmitted, conflict.Outcome.Disposition);
        Assert.Equal(1, conflictTransport.BinarySendCount);
        conflictCore.Close();
        await first.Completion;
    }

    [Fact]
    public async Task TrackedUploadAcknowledgementsDistinguishAuthoritativeResponseFromTupleMismatch()
    {
        var acceptedCore = Core(new BinaryTransport());
        var acceptedSession = Guid.NewGuid();
        var accepted = acceptedCore.SendUploadChunkTracked(
            Chunk(acceptedSession, 0, 1),
            1,
            CancellationToken.None);
        var acceptedDisposition = ObserveDispositionAtCompletion(accepted);
        acceptedCore.RouteText(Accepted(acceptedSession, 0, 1));
        Assert.True((await accepted.Completion).IsOk(out _));
        Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, await acceptedDisposition);

        var rejectedCore = Core(new BinaryTransport());
        var rejectedSession = Guid.NewGuid();
        var rejected = rejectedCore.SendUploadChunkTracked(
            Chunk(rejectedSession, 0, 1),
            1,
            CancellationToken.None);
        var rejectedDisposition = ObserveDispositionAtCompletion(rejected);
        rejectedCore.RouteText(Rejected(rejectedSession, 0, 1, "conflict"));
        Assert.True((await rejected.Completion).IsErr(out _));
        Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, await rejectedDisposition);

        var mismatchCore = Core(new BinaryTransport());
        var mismatchSession = Guid.NewGuid();
        var mismatch = mismatchCore.SendUploadChunkTracked(
            Chunk(mismatchSession, 0, 1),
            1,
            CancellationToken.None);
        mismatchCore.RouteText(Accepted(mismatchSession, 1, 1));
        Assert.True((await mismatch.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            mismatch.Outcome.Disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmissionPublicationPrecedesAcknowledgementVisibility(bool tupleMismatch)
    {
        using var admissionGate = new V2ClientUploadAdmissionTestGate();
        var transport = new BinaryTransport();
        var core = new V2ClientConnectionCore(
            transport,
            TimeProvider.System,
            RequestTimeout,
            admissionGate);
        var session = Guid.NewGuid();
        var invocation = Task.Factory.StartNew(
            () => core.SendUploadChunkTracked(
                Chunk(session, 0, 1),
                1,
                CancellationToken.None),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task? route = null;
        try
        {
            await admissionGate.PublicationReached.WaitAsync(TimeSpan.FromSeconds(5));

            var acknowledgementOffset = tupleMismatch ? 1 : 0;
            route = Task.Factory.StartNew(
                () => core.RouteText(Accepted(session, acknowledgementOffset, 1)),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            await admissionGate.RouteAttempted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(invocation.IsCompleted);
            Assert.False(route.IsCompleted);

            admissionGate.ReleasePublication();
            var operation = await invocation.WaitAsync(TimeSpan.FromSeconds(5));
            await route.WaitAsync(TimeSpan.FromSeconds(5));
            var legacyResult = await operation.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            if (tupleMismatch)
            {
                Assert.True(legacyResult.IsErr(out var error));
                Assert.Equal("protocol.upload_ack_mismatch", error!.Code);
                Assert.Equal(
                    V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
                    operation.Outcome.Disposition);
            }
            else
            {
                Assert.True(legacyResult.IsOk(out _));
                Assert.Equal(V2ClientInvocationDisposition.ResponseReceived, operation.Outcome.Disposition);
            }

            await core.WaitForSendObserversAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, transport.BinarySendCount);
            Assert.Equal(0, core.UploadPendingCount);
            Assert.Equal(0, core.SendObserverCount);
            Assert.Equal(0, core.ActiveSendLifetimeCount);
        }
        finally
        {
            admissionGate.ReleasePublication();
        }
    }

    [Fact]
    public async Task TrackedUploadLocalTerminalPathsRemainAmbiguousAfterSend()
    {
        using var cancellation = new CancellationTokenSource();
        var canceledCore = Core(new BinaryTransport());
        var canceled = canceledCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            cancellation.Token);
        var canceledDisposition = ObserveDispositionAtCompletion(canceled);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.Completion);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            await canceledDisposition);

        var time = new ManualTimeProvider();
        var timeoutCore = new V2ClientConnectionCore(
            new BinaryTransport(),
            time,
            TimeSpan.FromSeconds(1));
        var timedOut = timeoutCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await timedOut.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            timedOut.Outcome.Disposition);

        var syncFailureCore = Core(new BinaryTransport((_, _) =>
            throw new IOException("test-only synchronous send failure")));
        var syncFailure = syncFailureCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            CancellationToken.None);
        Assert.True((await syncFailure.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            syncFailure.Outcome.Disposition);

        var asyncFailureCore = Core(new BinaryTransport((_, _) =>
            new ValueTask(Task.FromException(new IOException("test-only asynchronous send failure")))));
        var asyncFailure = asyncFailureCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            CancellationToken.None);
        Assert.True((await asyncFailure.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            asyncFailure.Outcome.Disposition);
        await asyncFailureCore.WaitForSendObserversAsync();

        var closeCore = Core(new BinaryTransport());
        var closed = closeCore.SendUploadChunkTracked(
            Chunk(Guid.NewGuid(), 0, 1),
            1,
            CancellationToken.None);
        closeCore.Close();
        Assert.True((await closed.Completion).IsErr(out _));
        Assert.Equal(
            V2ClientInvocationDisposition.AdmittedWithoutAuthoritativeResponse,
            closed.Outcome.Disposition);
    }

    private static V2ClientConnectionCore Core(
        IV2ClientWireTransport transport,
        Action<V2ClientDiagnostic>? diagnostic = null) =>
        new(transport, TimeProvider.System, RequestTimeout, diagnostic: diagnostic);

    private static UploadChunkRequest Chunk(Guid sessionId, long offset, params byte[] data) =>
        new(sessionId, offset, ImmutableArray.Create(data));

    private static byte[] Accepted(Guid sessionId, long offset, int length) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{length},\"status\":\"accepted\"}}}}");

    private static byte[] Rejected(Guid sessionId, long offset, int length, string wireKind) =>
        Utf8($"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{length},\"status\":\"rejected\",\"error\":{{\"code\":-32000,\"message\":\"Rejected\",\"data\":{{\"daemon_error_code\":\"remote.upload_rejected\",\"daemon_error_kind\":\"{wireKind}\",\"correlation_id\":\"upload-test\"}}}}}}}}");

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static Task<V2ClientInvocationDisposition> ObserveDispositionAtCompletion<TResult>(
        V2ClientInvocationOperation<TResult> operation)
        where TResult : notnull =>
        operation.Completion.ContinueWith(
            _ => operation.Outcome.Disposition,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class BinaryTransport(
        Func<ImmutableArray<byte>, CancellationToken, ValueTask>? binarySend = null)
        : IV2ClientWireTransport
    {
        internal List<ImmutableArray<byte>> Frames { get; } = [];
        internal List<CancellationToken> BinaryTokens { get; } = [];
        internal int BinarySendCount => Frames.Count;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken)
        {
            Frames.Add(frame);
            BinaryTokens.Add(cancellationToken);
            return binarySend?.Invoke(frame, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }

    private sealed class ConnectionCanceledTransport : IV2ClientWireTransport
    {
        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
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

        internal void Advance(TimeSpan elapsed)
        {
            foreach (var timer in _timers.ToArray())
                timer.Advance(elapsed);
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan remaining) : ITimer
        {
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                remaining = dueTime;
                return !_disposed;
            }

            internal void Advance(TimeSpan elapsed)
            {
                remaining -= elapsed;
                if (!_disposed && remaining <= TimeSpan.Zero)
                {
                    _disposed = true;
                    callback(state);
                }
            }

            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
