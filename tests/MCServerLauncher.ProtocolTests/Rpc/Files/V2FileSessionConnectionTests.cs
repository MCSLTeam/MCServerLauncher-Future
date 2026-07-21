using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Files;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class V2FileSessionConnectionTests
{
    [Fact]
    public async Task SessionBindings_MissingContextReturnTypedErrorWithoutDirectApplicationFallback()
    {
        var application = new FakeFileApplication();
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("test", "1"));
        BuiltInFileRpcRegistrar.Register(builder, application);
        var catalog = builder.Freeze();
        var sessionId = Guid.NewGuid();
        var cases = new (string Method, object Request)[]
        {
            ("mcsl.file.upload.open", new UploadOpenRequest("x", 1, "hash")),
            ("mcsl.file.upload.close", new FileSessionReference(sessionId)),
            ("mcsl.file.upload.cancel", new FileSessionReference(sessionId)),
            ("mcsl.file.download.open", new DownloadOpenRequest("x")),
            ("mcsl.file.download.read", new DownloadChunkRequest(sessionId, 0, 1)),
            ("mcsl.file.download.close", new FileSessionReference(sessionId))
        };

        foreach (var item in cases)
        {
            var execution = await catalog.Rpcs[new RpcMethod(item.Method)].Binding.InvokeAsync(
                new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn), item.Request, CancellationToken.None);
            Assert.True(execution.Result.IsErr(out _));
            Assert.IsType<InternalDaemonError>(execution.Result.UnwrapErr());
            Assert.Equal("file.session.context_missing", execution.Result.UnwrapErr().Code);
        }

        var direct = await catalog.Rpcs[new RpcMethod("mcsl.directory.create")].Binding.InvokeAsync(
            new ProtocolInvocationContext(ProtocolExecutionOwner.BuiltIn), new PathRequest("x"), CancellationToken.None);
        Assert.True(direct.Result.IsOk(out _));
        Assert.Equal(1, application.CreateDirectoryCalls);
        Assert.Equal(0, application.UploadOpenCalls + application.UploadCloseCalls + application.UploadCancelCalls +
            application.DownloadOpenCalls + application.DownloadReadCalls + application.DownloadCloseCalls);
    }

    [Fact]
    public async Task Attach_ClosedOwnerFailsWithoutCleanupResidue()
    {
        var owner = Owner("mcsl.file.**");
        await owner.AbortAsync();

        var result = V2FileSessionConnection.Attach(new FakeFileApplication(), Catalog(), owner);

        Assert.True(result.IsErr(out _));
        Assert.Equal("file.session.connection_closed", result.UnwrapErr().Code);
        Assert.Equal(0, owner.CleanupRegistrationCount);
    }

    [Fact]
    public async Task OpenUpload_PermissionDeniedBeforeApplication()
    {
        var application = new FakeFileApplication();
        await using var owner = Owner();
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();

        var result = await connection.OpenUploadAsync(new UploadOpenRequest("x", 1, "hash"), CancellationToken.None);

        Assert.True(result.IsErr(out _));
        Assert.IsType<PermissionDaemonError>(result.UnwrapErr());
        Assert.Equal(0, application.UploadOpenCalls);
    }

    [Fact]
    public async Task ExpiredUpload_IsDetachedAndCompensatedExactlyOnce()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-12T00:00:00Z"));
        var application = new FakeFileApplication
        {
            UploadSession = new UploadSession(Guid.NewGuid(), 1024, time.GetUtcNow().AddMinutes(1))
        };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner, time).Unwrap();
        var opened = await connection.OpenUploadAsync(new UploadOpenRequest("x", 1, "hash"), CancellationToken.None);
        Assert.True(opened.IsOk(out _));

        time.Advance(TimeSpan.FromMinutes(1));
        var first = await connection.CloseUploadAsync(application.UploadSession.SessionId, CancellationToken.None);
        var second = await connection.CloseUploadAsync(application.UploadSession.SessionId, CancellationToken.None);

        Assert.Equal("file.session.not_found", first.UnwrapErr().Code);
        Assert.Equal("file.session.not_found", second.UnwrapErr().Code);
        Assert.Equal(1, application.UploadCancelCalls);
        Assert.Equal(0, application.UploadCloseCalls);
    }

    [Fact]
    public async Task DownloadRead_RejectsConcurrentReadAndCloseWithoutCallingApplication()
    {
        var application = new FakeFileApplication();
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var opened = await connection.OpenDownloadAsync(new DownloadOpenRequest("x"), CancellationToken.None);
        Assert.True(opened.IsOk(out _));

        application.BlockRead = true;
        var firstRead = connection.ReadDownloadChunkAsync(
            new DownloadChunkRequest(application.DownloadSession.SessionId, 0, 10), CancellationToken.None);
        await application.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondRead = await connection.ReadDownloadChunkAsync(
            new DownloadChunkRequest(application.DownloadSession.SessionId, 0, 10), CancellationToken.None);
        var close = await connection.CloseDownloadAsync(application.DownloadSession.SessionId, CancellationToken.None);

        Assert.Equal("file.download.read_in_flight", secondRead.UnwrapErr().Code);
        Assert.Equal("file.session.busy", close.UnwrapErr().Code);
        Assert.Equal(1, application.DownloadReadCalls);
        Assert.Equal(0, application.DownloadCloseCalls);

        application.ReleaseRead.TrySetResult();
        Assert.True((await firstRead).IsOk(out _));
    }

    [Fact]
    public async Task DownloadOpen_ReservesConnectionLimitBeforeCallingApplication()
    {
        var application = new FakeFileApplication { BlockOpen = true };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(
            application,
            Catalog(),
            owner,
            downloadSessionLimit: 1).Unwrap();

        var first = connection.OpenDownloadAsync(new DownloadOpenRequest("first"), CancellationToken.None);
        await application.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rejected = await connection.OpenDownloadAsync(new DownloadOpenRequest("second"), CancellationToken.None);

        Assert.True(rejected.IsErr(out _));
        Assert.Equal("file.download.limit", rejected.UnwrapErr().Code);
        Assert.Equal(1, application.DownloadOpenCalls);

        application.ReleaseOpen.TrySetResult();
        var opened = (await first).Unwrap();
        Assert.True((await connection.CloseDownloadAsync(opened.SessionId, CancellationToken.None)).IsOk(out _));
    }

    [Fact]
    public async Task DownloadOpen_ClosedLeaseFreesConnectionReservation()
    {
        var application = new FakeFileApplication();
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(
            application,
            Catalog(),
            owner,
            downloadSessionLimit: 1).Unwrap();

        var first = (await connection.OpenDownloadAsync(new DownloadOpenRequest("first"), CancellationToken.None)).Unwrap();
        Assert.True((await connection.CloseDownloadAsync(first.SessionId, CancellationToken.None)).IsOk(out _));

        var second = await connection.OpenDownloadAsync(new DownloadOpenRequest("second"), CancellationToken.None);

        Assert.True(second.IsOk(out _));
        Assert.Equal(2, application.DownloadOpenCalls);
        Assert.True((await connection.CloseDownloadAsync(second.Unwrap().SessionId, CancellationToken.None)).IsOk(out _));
    }

    [Fact]
    public async Task DownloadOpen_CancellationRollsBackConnectionReservation()
    {
        var application = new FakeFileApplication { BlockOpen = true };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(
            application,
            Catalog(),
            owner,
            downloadSessionLimit: 1).Unwrap();
        using var cancellationSource = new CancellationTokenSource();

        var cancelled = connection.OpenDownloadAsync(new DownloadOpenRequest("cancelled"), cancellationSource.Token);
        await application.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => _ = await cancelled);

        application.BlockOpen = false;
        var reopened = await connection.OpenDownloadAsync(new DownloadOpenRequest("replacement"), CancellationToken.None);

        Assert.True(reopened.IsOk(out _));
        Assert.Equal(2, application.DownloadOpenCalls);
        Assert.True((await connection.CloseDownloadAsync(reopened.Unwrap().SessionId, CancellationToken.None)).IsOk(out _));
    }

    [Fact]
    public async Task DownloadOpen_ErrorRollsBackConnectionReservation()
    {
        var application = new FakeFileApplication
        {
            DownloadOpenError = new StorageDaemonError("file.storage_failed", "Injected open failure.")
        };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(
            application,
            Catalog(),
            owner,
            downloadSessionLimit: 1).Unwrap();

        var failed = await connection.OpenDownloadAsync(new DownloadOpenRequest("failed"), CancellationToken.None);
        application.DownloadOpenError = null;
        var reopened = await connection.OpenDownloadAsync(new DownloadOpenRequest("replacement"), CancellationToken.None);

        Assert.True(failed.IsErr(out _));
        Assert.True(reopened.IsOk(out _));
        Assert.Equal(2, application.DownloadOpenCalls);
        Assert.True((await connection.CloseDownloadAsync(reopened.Unwrap().SessionId, CancellationToken.None)).IsOk(out _));
    }

    [Fact]
    public async Task OwnerDisconnect_CleansEverySessionExactlyOnce()
    {
        var application = new FakeFileApplication();
        var owner = Owner("mcsl.file.**");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        Assert.True((await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).IsOk(out _));
        Assert.True((await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).IsOk(out _));

        await owner.AbortAsync();

        Assert.Equal(1, application.UploadCancelCalls);
        Assert.Equal(1, application.DownloadCloseCalls);
    }

    [Fact]
    public async Task UploadWrite_RejectsSecondChunkBeforeCopyOrApplication()
    {
        var application = new FakeFileApplication { BlockWrite = true };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();

        var first = connection.ReceiveUploadChunkAsync(session.SessionId, 0, new byte[] { 1 }, CancellationToken.None);
        await application.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await connection.ReceiveUploadChunkAsync(session.SessionId, 1, new byte[] { 2 }, CancellationToken.None);

        Assert.Equal("file.upload.chunk_in_flight", second.UnwrapErr().Code);
        Assert.Equal(1, application.UploadWriteCalls);
        application.ReleaseWrite.TrySetResult();
        Assert.True((await first).IsOk(out _));
    }

    [Fact]
    public async Task UploadWrite_RejectsConcurrentCloseUntilWriteCompletes()
    {
        var application = new FakeFileApplication { BlockWrite = true };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(
            new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();

        var write = connection.ReceiveUploadChunkAsync(
            session.SessionId, 0, new byte[] { 1 }, CancellationToken.None);
        await application.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var conflict = await connection.CloseUploadAsync(session.SessionId, CancellationToken.None);

        Assert.IsType<ConflictDaemonError>(conflict.UnwrapErr());
        Assert.Equal("file.upload.chunk_in_flight", conflict.UnwrapErr().Code);
        Assert.Equal(0, application.UploadCloseCalls);

        application.ReleaseWrite.TrySetResult();
        Assert.True((await write).IsOk(out _));
        Assert.True((await connection.CloseUploadAsync(session.SessionId, CancellationToken.None)).IsOk(out _));
        Assert.Equal(1, application.UploadCloseCalls);
    }

    [Fact]
    public async Task UploadWrite_RejectsConcurrentCancelUntilWriteCompletes()
    {
        var application = new FakeFileApplication { BlockWrite = true };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(
            new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();

        var write = connection.ReceiveUploadChunkAsync(
            session.SessionId, 0, new byte[] { 1 }, CancellationToken.None);
        await application.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var conflict = await connection.CancelUploadAsync(session.SessionId, CancellationToken.None);

        Assert.IsType<ConflictDaemonError>(conflict.UnwrapErr());
        Assert.Equal("file.upload.chunk_in_flight", conflict.UnwrapErr().Code);
        Assert.Equal(0, application.UploadCancelCalls);

        application.ReleaseWrite.TrySetResult();
        Assert.True((await write).IsOk(out _));
        Assert.True((await connection.CancelUploadAsync(session.SessionId, CancellationToken.None)).IsOk(out _));
        Assert.Equal(1, application.UploadCancelCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OwnerDisconnect_WaitsForBlockedUploadWriteAndCancelsExactlyOnce(
        bool cancelWrite,
        bool terminalWrite)
    {
        var application = new FakeFileApplication
        {
            BlockWrite = true,
            IgnoreWriteCancellation = true,
            UploadWriteResult = terminalWrite
                ? Result.Err<Unit, DaemonError>(
                    new ValidationDaemonError("file.chunk.offset.invalid", "bad offset"))
                : Result.Ok<Unit, DaemonError>(Unit.Default)
        };
        var owner = Owner("mcsl.file.upload");
        application.UploadWriteException = cancelWrite
            ? new OperationCanceledException(owner.ConnectionToken)
            : null;
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(
            new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();
        var connectionCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = owner.ConnectionToken.Register(() => connectionCancelled.TrySetResult());

        var write = connection.ReceiveUploadChunkAsync(
            session.SessionId, 0, new byte[] { 1 }, owner.ConnectionToken);
        await application.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cleanup = owner.AbortAsync();
        await connectionCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(write.IsCompleted);
        Assert.False(cleanup.IsCompleted);
        Assert.Equal(0, application.UploadCancelCalls);

        application.ReleaseWrite.TrySetResult();
        if (cancelWrite)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        else
        {
            var writeResult = await write;
            if (terminalWrite)
                Assert.Equal("file.chunk.offset.invalid", writeResult.UnwrapErr().Code);
            else
                Assert.True(writeResult.IsOk(out _));
        }
        await cleanup;

        Assert.Equal(1, application.UploadCancelCalls);
        Assert.Equal(CancellationToken.None, Assert.Single(application.CleanupTokens));
        Assert.Equal("file.session.not_found",
            (await connection.CancelUploadAsync(session.SessionId, CancellationToken.None)).UnwrapErr().Code);
    }

    [Fact]
    public async Task UploadWrite_TerminalErrorCancelsAndDetachesExactlyOnce()
    {
        var application = new FakeFileApplication
        {
            UploadWriteResult = Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("file.chunk.offset.invalid", "bad offset"))
        };
        var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();

        var result = await connection.ReceiveUploadChunkAsync(session.SessionId, 7, new byte[] { 1 }, CancellationToken.None);
        await owner.AbortAsync();

        Assert.Equal("file.chunk.offset.invalid", result.UnwrapErr().Code);
        Assert.Equal(1, application.UploadCancelCalls);
        Assert.Equal(CancellationToken.None, Assert.Single(application.CleanupTokens));
    }

    [Fact]
    public async Task UploadWrite_PerSessionMaximumRejectsBeforeApplicationAndTerminates()
    {
        var application = new FakeFileApplication
        {
            UploadSession = new UploadSession(Guid.NewGuid(), 1, DateTimeOffset.MaxValue)
        };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();

        var result = await connection.ReceiveUploadChunkAsync(session.SessionId, 0, new byte[2], CancellationToken.None);

        Assert.Equal("file.chunk.too_large", result.UnwrapErr().Code);
        Assert.Equal(0, application.UploadWriteCalls);
        Assert.Equal(1, application.UploadCancelCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_WhenOwnerClosesBeforeRegistration_CompensatesWithNone(bool upload)
    {
        var application = new FakeFileApplication { BlockOpen = true };
        var owner = Owner("mcsl.file.**");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var opening = upload
            ? AwaitCode(connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None))
            : AwaitCode(connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None));
        await application.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = owner.AbortAsync();
        application.ReleaseOpen.TrySetResult();

        Assert.Equal("file.session.connection_closed", await opening);
        await closing;
        Assert.Equal(upload ? 1 : 0, application.UploadCancelCalls);
        Assert.Equal(upload ? 0 : 1, application.DownloadCloseCalls);
        Assert.All(application.CleanupTokens, static token => Assert.Equal(CancellationToken.None, token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Open_ExpiredOnArrival_CompensatesAndRejectsRegistration(bool upload)
    {
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        var time = new ManualTimeProvider(now);
        var application = new FakeFileApplication
        {
            UploadSession = new UploadSession(Guid.NewGuid(), 10, now),
            DownloadSession = new DownloadSession(Guid.NewGuid(), 10, "hash", 10, now)
        };
        await using var owner = Owner("mcsl.file.**");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner, time).Unwrap();

        var code = upload
            ? await AwaitCode(connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None))
            : await AwaitCode(connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None));

        Assert.Equal("file.session.registration_failed", code);
        Assert.Equal(upload ? 1 : 0, application.UploadCancelCalls);
        Assert.Equal(upload ? 0 : 1, application.DownloadCloseCalls);
    }

    [Fact]
    public async Task DuplicateRegistration_IsRejectedAndCompensated()
    {
        var application = new FakeFileApplication();
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();

        Assert.True((await connection.OpenUploadAsync(new UploadOpenRequest("one", 1, "hash"), CancellationToken.None)).IsOk(out _));
        var duplicate = await connection.OpenUploadAsync(new UploadOpenRequest("two", 1, "hash"), CancellationToken.None);

        Assert.Equal("file.session.registration_failed", duplicate.UnwrapErr().Code);
        Assert.Equal(1, application.UploadCancelCalls);
    }

    [Fact]
    public async Task ForeignAndUnknownSessions_ReturnNotFoundWithoutApplicationCall()
    {
        var firstApplication = new FakeFileApplication();
        var secondApplication = new FakeFileApplication();
        await using var firstOwner = Owner("mcsl.file.upload");
        await using var secondOwner = Owner("mcsl.file.upload");
        var first = V2FileSessionConnection.Attach(firstApplication, Catalog(), firstOwner).Unwrap();
        var second = V2FileSessionConnection.Attach(secondApplication, Catalog(), secondOwner).Unwrap();
        var session = (await first.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).Unwrap();

        Assert.Equal("file.session.not_found", (await second.CloseUploadAsync(session.SessionId, CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal("file.session.not_found", (await second.CancelUploadAsync(Guid.NewGuid(), CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal(0, secondApplication.UploadCloseCalls + secondApplication.UploadCancelCalls);
    }

    [Fact]
    public async Task EndInFlight_RejectsReadAndSecondEndThenCompletesOnce()
    {
        var application = new FakeFileApplication { BlockDownloadClose = true };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();
        var ending = connection.CloseDownloadAsync(session.SessionId, CancellationToken.None);
        await application.DownloadCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var read = await connection.ReadDownloadChunkAsync(new DownloadChunkRequest(session.SessionId, 0, 1), CancellationToken.None);
        var secondEnd = await connection.CloseDownloadAsync(session.SessionId, CancellationToken.None);
        Assert.Equal("file.session.busy", read.UnwrapErr().Code);
        Assert.Equal("file.session.busy", secondEnd.UnwrapErr().Code);
        Assert.Equal(0, application.DownloadReadCalls);
        Assert.Equal(1, application.DownloadCloseCalls);

        application.ReleaseDownloadClose.TrySetResult();
        Assert.True((await ending).IsOk(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadEndInFlight_RejectsThirdChunkWithoutReplacingCleanupWait(bool cancelUpload)
    {
        var application = new FakeFileApplication
        {
            BlockUploadClose = !cancelUpload,
            BlockUploadCancel = cancelUpload
        };
        var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(
            new UploadOpenRequest("u", 10, "hash"), CancellationToken.None)).Unwrap();
        var ending = cancelUpload
            ? connection.CancelUploadAsync(session.SessionId, CancellationToken.None)
            : connection.CloseUploadAsync(session.SessionId, CancellationToken.None);
        var endEntered = cancelUpload
            ? application.UploadCancelEntered.Task
            : application.UploadCloseEntered.Task;
        await endEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var thirdChunk = await connection.ReceiveUploadChunkAsync(
            session.SessionId, 0, new byte[] { 1 }, CancellationToken.None);

        Assert.IsType<ConflictDaemonError>(thirdChunk.UnwrapErr());
        Assert.Equal("file.session.busy", thirdChunk.UnwrapErr().Code);
        Assert.Equal(0, application.UploadWriteCalls);

        var cleanup = owner.AbortAsync();
        Assert.False(cleanup.IsCompleted);

        if (cancelUpload)
            application.ReleaseUploadCancel.TrySetResult();
        else
            application.ReleaseUploadClose.TrySetResult();

        Assert.True((await ending).IsOk(out _));
        await cleanup;
        Assert.Equal(cancelUpload ? 0 : 1, application.UploadCloseCalls);
        Assert.Equal(cancelUpload ? 1 : 0, application.UploadCancelCalls);
    }

    [Fact]
    public async Task CancelledRead_RestoresActiveLeaseForRetry()
    {
        var application = new FakeFileApplication { BlockRead = true };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();
        using var cancellation = new CancellationTokenSource();
        var read = connection.ReadDownloadChunkAsync(new DownloadChunkRequest(session.SessionId, 0, 1), cancellation.Token);
        await application.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);

        application.BlockRead = false;
        Assert.True((await connection.ReadDownloadChunkAsync(
            new DownloadChunkRequest(session.SessionId, 0, 1), CancellationToken.None)).IsOk(out _));
        Assert.Equal(2, application.DownloadReadCalls);
    }

    [Fact]
    public async Task ExplicitEndAndDirectCleanup_WaitsForIdleAndCallsApplicationExactlyOnce()
    {
        var application = new FakeFileApplication { BlockDownloadClose = true };
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();
        var ending = connection.CloseDownloadAsync(session.SessionId, CancellationToken.None);
        await application.DownloadCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = connection.CleanupAsync(CancellationToken.None).AsTask();
        try
        {
            Assert.False(cleanup.IsCompleted);
        }
        finally
        {
            application.ReleaseDownloadClose.TrySetResult();
        }

        Assert.True((await ending).IsOk(out _));
        await cleanup;
        Assert.Equal(1, application.DownloadCloseCalls);
    }

    [Theory]
    [InlineData("file.session.not_found")]
    [InlineData("file.session.expired")]
    public async Task DownloadRead_TerminalErrorsDetachLease(string code)
    {
        var application = new FakeFileApplication();
        application.DownloadReadResults.Enqueue(Result.Err<DownloadChunk, DaemonError>(
            new NotFoundDaemonError(code, "terminal")));
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();
        var request = new DownloadChunkRequest(session.SessionId, 0, 1);

        Assert.Equal(code, (await connection.ReadDownloadChunkAsync(request, CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal("file.session.not_found", (await connection.ReadDownloadChunkAsync(request, CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal(1, application.DownloadReadCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadRead_ValidationAndStorageErrorsRetainLease(bool validation)
    {
        var application = new FakeFileApplication();
        var error = validation
            ? (DaemonError)new ValidationDaemonError("file.chunk.offset.invalid", "invalid")
            : new StorageDaemonError("file.storage_failed", "failed");
        application.DownloadReadResults.Enqueue(Result.Err<DownloadChunk, DaemonError>(error));
        application.DownloadReadResults.Enqueue(Result.Ok<DownloadChunk, DaemonError>(
            new DownloadChunk(0, ImmutableArray.Create((byte)7), true)));
        await using var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();
        var request = new DownloadChunkRequest(session.SessionId, 0, 1);

        Assert.Same(error, (await connection.ReadDownloadChunkAsync(request, CancellationToken.None)).UnwrapErr());
        Assert.True((await connection.ReadDownloadChunkAsync(request, CancellationToken.None)).IsOk(out _));
        Assert.Equal(2, application.DownloadReadCalls);
    }

    [Fact]
    public async Task CatalogPermissionsUseImmutableOwnerSnapshotAcrossSessionMethods()
    {
        var catalog = Catalog();
        Assert.Equal("mcsl.file.upload.open", catalog.Rpcs[new RpcMethod("mcsl.file.upload.open")].Descriptor.Permission.Value);
        Assert.Equal("mcsl.file.download.open", catalog.Rpcs[new RpcMethod("mcsl.file.download.open")].Descriptor.Permission.Value);
        var permissions = new List<string> { "mcsl.file.upload" };
        await using var owner = new V2ConnectionOwner(new NoOpSender(), permissions);
        permissions.Clear();
        permissions.Add("mcsl.file.download");
        Assert.Equal("mcsl.file.upload", Assert.Single(owner.Permissions));
        var application = new FakeFileApplication();
        var connection = V2FileSessionConnection.Attach(application, catalog, owner).Unwrap();

        var closeSession = (await connection.OpenUploadAsync(
            new UploadOpenRequest("close", 1, "hash"), CancellationToken.None)).Unwrap();
        Assert.True((await connection.CloseUploadAsync(closeSession.SessionId, CancellationToken.None)).IsOk(out _));
        application.UploadSession = new UploadSession(Guid.NewGuid(), 1024, DateTimeOffset.MaxValue);
        var cancelSession = (await connection.OpenUploadAsync(
            new UploadOpenRequest("cancel", 1, "hash"), CancellationToken.None)).Unwrap();
        Assert.True((await connection.CancelUploadAsync(cancelSession.SessionId, CancellationToken.None)).IsOk(out _));

        var deniedDownload = await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None);
        Assert.IsType<PermissionDaemonError>(deniedDownload.UnwrapErr());
        Assert.Equal(0, application.DownloadOpenCalls);
        Assert.Equal("file.session.not_found", (await connection.CloseDownloadAsync(Guid.NewGuid(), CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal(0, application.DownloadCloseCalls);
    }

    [Fact]
    public async Task OwnerCleanupWinningRace_DetachesBeforeCallingApplication()
    {
        var application = new FakeFileApplication { BlockDownloadClose = true };
        var owner = Owner("mcsl.file.download");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).Unwrap();

        var cleanup = owner.AbortAsync();
        await application.DownloadCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var explicitClose = await connection.CloseDownloadAsync(session.SessionId, CancellationToken.None);

        Assert.Equal("file.session.not_found", explicitClose.UnwrapErr().Code);
        Assert.Equal(1, application.DownloadCloseCalls);
        application.ReleaseDownloadClose.TrySetResult();
        await cleanup;
        Assert.Equal(1, application.DownloadCloseCalls);
    }

    [Fact]
    public async Task UploadIncompleteRetainsLeaseWhileOtherErrorsAreTerminal()
    {
        var application = new FakeFileApplication
        {
            UploadCloseResult = Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("file.upload.incomplete", "incomplete"))
        };
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).Unwrap();

        Assert.Equal("file.upload.incomplete", (await connection.CloseUploadAsync(session.SessionId, CancellationToken.None)).UnwrapErr().Code);
        application.UploadCloseResult = Result.Err<Unit, DaemonError>(new StorageDaemonError("file.storage_failed", "failed"));
        Assert.Equal("file.storage_failed", (await connection.CloseUploadAsync(session.SessionId, CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal("file.session.not_found", (await connection.CloseUploadAsync(session.SessionId, CancellationToken.None)).UnwrapErr().Code);
        Assert.Equal(2, application.UploadCloseCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadCloseException_CancellationRetainsButUnexpectedThrowTerminates(bool cancellation)
    {
        var application = new FakeFileApplication();
        await using var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        var session = (await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).Unwrap();
        application.UploadCloseException = cancellation
            ? new OperationCanceledException(CancellationToken.None)
            : new IOException("close failed");

        await Assert.ThrowsAnyAsync<Exception>(() => connection.CloseUploadAsync(session.SessionId, CancellationToken.None));
        application.UploadCloseException = null;
        var retry = await connection.CloseUploadAsync(session.SessionId, CancellationToken.None);

        if (cancellation)
            Assert.True(retry.IsOk(out _));
        else
            Assert.Equal("file.session.not_found", retry.UnwrapErr().Code);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RegistrationCompensation_FailureIsSurfaced(bool upload, bool throws)
    {
        var now = DateTimeOffset.Parse("2026-07-12T00:00:00Z");
        var application = new FakeFileApplication
        {
            UploadSession = new UploadSession(Guid.NewGuid(), 1, now),
            DownloadSession = new DownloadSession(Guid.NewGuid(), 1, "hash", 1, now),
            UploadCancelResult = Result.Err<Unit, DaemonError>(new StorageDaemonError("cleanup.failed", "failed")),
            DownloadCloseResult = Result.Err<Unit, DaemonError>(new StorageDaemonError("cleanup.failed", "failed")),
            UploadCancelException = throws ? new IOException("upload cleanup") : null,
            DownloadCloseException = throws ? new IOException("download cleanup") : null
        };
        await using var owner = Owner("mcsl.file.**");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner, new ManualTimeProvider(now)).Unwrap();

        async Task Invoke()
        {
            if (upload)
                _ = await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None);
            else
                _ = await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None);
        }

        if (throws)
            await Assert.ThrowsAsync<IOException>(Invoke);
        else
        {
            var error = upload
                ? (await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).UnwrapErr()
                : (await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).UnwrapErr();
            Assert.Equal("file.session.compensation_failed", error.Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailure_AggregatesAfterAttemptingEveryDetachedLease(bool throws)
    {
        var application = new FakeFileApplication();
        var owner = Owner("mcsl.file.**");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        Assert.True((await connection.OpenUploadAsync(new UploadOpenRequest("u1", 1, "hash"), CancellationToken.None)).IsOk(out _));
        application.UploadSession = new UploadSession(Guid.NewGuid(), 1, DateTimeOffset.MaxValue);
        Assert.True((await connection.OpenUploadAsync(new UploadOpenRequest("u2", 1, "hash"), CancellationToken.None)).IsOk(out _));
        Assert.True((await connection.OpenDownloadAsync(new DownloadOpenRequest("d"), CancellationToken.None)).IsOk(out _));
        application.UploadCancelException = throws ? new IOException("cleanup failed") : null;
        application.UploadCancelResult = throws
            ? Result.Ok<Unit, DaemonError>(Unit.Default)
            : Result.Err<Unit, DaemonError>(new StorageDaemonError("cleanup.failed", "cleanup failed"));

        var failure = await Assert.ThrowsAsync<AggregateException>(() => owner.AbortAsync());

        Assert.Contains("cleanup failed", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, application.UploadCancelCalls);
        Assert.Equal(1, application.DownloadCloseCalls);
        Assert.Equal("file.session.not_found", (await connection.CancelUploadAsync(application.UploadSession.SessionId, CancellationToken.None)).UnwrapErr().Code);
    }

    [Fact]
    public async Task CleanupExpectedNotFound_DoesNotFaultOwnerClose()
    {
        var application = new FakeFileApplication
        {
            UploadCancelResult = Result.Err<Unit, DaemonError>(new NotFoundDaemonError("file.session.not_found", "gone"))
        };
        var owner = Owner("mcsl.file.upload");
        var connection = V2FileSessionConnection.Attach(application, Catalog(), owner).Unwrap();
        Assert.True((await connection.OpenUploadAsync(new UploadOpenRequest("u", 1, "hash"), CancellationToken.None)).IsOk(out _));

        await owner.AbortAsync();

        Assert.Equal(1, application.UploadCancelCalls);
    }

    private static async Task<string> AwaitCode<T>(Task<Result<T, DaemonError>> operation) where T : notnull =>
        (await operation).UnwrapErr().Code;

    private static FrozenProtocolCatalog Catalog()
    {
        var builder = new ProtocolCatalogBuilder(new OpenRpcInfo("test", "1"));
        BuiltInFileRpcRegistrar.Register(builder, new FakeFileApplication());
        return builder.Freeze();
    }

    private static V2ConnectionOwner Owner(params string[] permissions) => new(new NoOpSender(), permissions);

    private sealed class NoOpSender : IV2OutboundSender
    {
        public ValueTask SendAsync(V2OutboundFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CloseAsync(V2ConnectionCloseReason reason, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan value) => _now += value;
    }

    private sealed class FakeFileApplication : IFileApplication
    {
        internal UploadSession UploadSession { get; set; } = new(Guid.NewGuid(), 1024, DateTimeOffset.MaxValue);
        internal DownloadSession DownloadSession { get; set; } = new(Guid.NewGuid(), 10, "hash", 1024, DateTimeOffset.MaxValue);
        internal int UploadOpenCalls { get; private set; }
        internal int UploadCloseCalls { get; private set; }
        internal int UploadCancelCalls { get; private set; }
        internal int UploadWriteCalls { get; private set; }
        internal int DownloadReadCalls { get; private set; }
        internal int DownloadCloseCalls { get; private set; }
        internal int DownloadOpenCalls { get; private set; }
        internal int CreateDirectoryCalls { get; private set; }
        internal bool BlockRead { get; set; }
        internal bool BlockOpen { get; set; }
        internal bool BlockDownloadClose { get; set; }
        internal bool BlockUploadClose { get; set; }
        internal bool BlockUploadCancel { get; set; }
        internal bool BlockWrite { get; set; }
        internal bool IgnoreWriteCancellation { get; set; }
        internal Result<Unit, DaemonError> UploadCloseResult { get; set; } = Result.Ok<Unit, DaemonError>(Unit.Default);
        internal Result<Unit, DaemonError> UploadCancelResult { get; set; } = Result.Ok<Unit, DaemonError>(Unit.Default);
        internal Result<Unit, DaemonError> DownloadCloseResult { get; set; } = Result.Ok<Unit, DaemonError>(Unit.Default);
        internal DaemonError? DownloadOpenError { get; set; }
        internal Result<Unit, DaemonError> UploadWriteResult { get; set; } = Result.Ok<Unit, DaemonError>(Unit.Default);
        internal Exception? UploadWriteException { get; set; }
        internal Exception? UploadCancelException { get; set; }
        internal Exception? UploadCloseException { get; set; }
        internal Exception? DownloadCloseException { get; set; }
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource OpenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DownloadCloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDownloadClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource UploadCloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseUploadClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource UploadCancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseUploadCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<CancellationToken> CleanupTokens { get; } = [];
        internal Queue<Result<DownloadChunk, DaemonError>> DownloadReadResults { get; } = new();

        public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(UploadOpenRequest request, CancellationToken cancellationToken)
        {
            UploadOpenCalls++;
            OpenEntered.TrySetResult();
            if (BlockOpen)
                await ReleaseOpen.Task.WaitAsync(cancellationToken);
            return Result.Ok<UploadSession, DaemonError>(UploadSession);
        }

        public async Task<Result<Unit, DaemonError>> CloseUploadAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            UploadCloseCalls++;
            if (UploadCloseException is not null)
                throw UploadCloseException;
            UploadCloseEntered.TrySetResult();
            if (BlockUploadClose)
                await ReleaseUploadClose.Task.WaitAsync(cancellationToken);
            return UploadCloseResult;
        }

        public async Task<Result<Unit, DaemonError>> CancelUploadAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            UploadCancelCalls++;
            CleanupTokens.Add(cancellationToken);
            if (UploadCancelException is not null)
                throw UploadCancelException;
            UploadCancelEntered.TrySetResult();
            if (BlockUploadCancel)
                await ReleaseUploadCancel.Task.WaitAsync(cancellationToken);
            return UploadCancelResult;
        }

        public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(DownloadOpenRequest request, CancellationToken cancellationToken)
        {
            DownloadOpenCalls++;
            OpenEntered.TrySetResult();
            if (BlockOpen)
                await ReleaseOpen.Task.WaitAsync(cancellationToken);
            if (DownloadOpenError is { } error)
                return Result.Err<DownloadSession, DaemonError>(error);
            return Result.Ok<DownloadSession, DaemonError>(DownloadSession);
        }

        public async Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(DownloadChunkRequest request, CancellationToken cancellationToken)
        {
            DownloadReadCalls++;
            ReadEntered.TrySetResult();
            if (BlockRead)
                await ReleaseRead.Task.WaitAsync(cancellationToken);
            if (DownloadReadResults.TryDequeue(out var result))
                return result;
            return Result.Ok<DownloadChunk, DaemonError>(new DownloadChunk(request.Offset, ImmutableArray.Create((byte)1), false));
        }

        public async Task<Result<Unit, DaemonError>> CloseDownloadAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            DownloadCloseCalls++;
            CleanupTokens.Add(cancellationToken);
            if (DownloadCloseException is not null)
                throw DownloadCloseException;
            DownloadCloseEntered.TrySetResult();
            if (BlockDownloadClose)
                await ReleaseDownloadClose.Task.WaitAsync(cancellationToken);
            return DownloadCloseResult;
        }

        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(PathRequest request, CancellationToken cancellationToken)
        {
            CreateDirectoryCalls++;
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }
        public Task<Result<Unit, DaemonError>> DeleteFileAsync(PathRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameFileAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(PathRenameRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyFileAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(PathTransferRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(UploadChunkRequest request, CancellationToken cancellationToken)
        {
            UploadWriteCalls++;
            WriteEntered.TrySetResult();
            if (BlockWrite)
            {
                if (IgnoreWriteCancellation)
                    await ReleaseWrite.Task;
                else
                    await ReleaseWrite.Task.WaitAsync(cancellationToken);
            }
            if (UploadWriteException is not null)
                throw UploadWriteException;
            return UploadWriteResult;
        }
    }
}
