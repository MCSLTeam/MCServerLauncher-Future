using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.DaemonClient.Application;
using MCServerLauncher.DaemonClient.Connection.V2;
using MCServerLauncher.DaemonClient.State;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.DaemonClient.V2;

public sealed class RemoteFileApplicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset FutureExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OrdinaryFileCallsMapEveryFrozenDescriptorAndPreserveRequestsAndCancellation()
    {
        var invoker = new RecordingInvoker();
        await using var owner = new V2ClientConnectionOwner(
            new NeverUsedSessionFactory(), TimeProvider.System, TimeSpan.Zero);
        var application = new RemoteFileApplication(invoker, owner);
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var directoryInfo = new PathRequest("directory-info");
        var fileInfo = new PathRequest("file-info");
        var createDirectory = new PathRequest("create-directory");
        var deleteFile = new PathRequest("delete-file");
        var deleteDirectory = new DeleteDirectoryRequest("delete-directory", true);
        var renameFile = new PathRenameRequest("rename-file", "renamed-file");
        var renameDirectory = new PathRenameRequest("rename-directory", "renamed-directory");
        var moveFile = new PathTransferRequest("move-file", "move-file-target");
        var moveDirectory = new PathTransferRequest("move-directory", "move-directory-target");
        var copyFile = new PathTransferRequest("copy-file", "copy-file-target");
        var copyDirectory = new PathTransferRequest("copy-directory", "copy-directory-target");

        DaemonError[] errors =
        [
            (await application.GetDirectoryInfoAsync(directoryInfo, token)).UnwrapErr(),
            (await application.GetFileInfoAsync(fileInfo, token)).UnwrapErr(),
            (await application.CreateDirectoryAsync(createDirectory, token)).UnwrapErr(),
            (await application.DeleteFileAsync(deleteFile, token)).UnwrapErr(),
            (await application.DeleteDirectoryAsync(deleteDirectory, token)).UnwrapErr(),
            (await application.RenameFileAsync(renameFile, token)).UnwrapErr(),
            (await application.RenameDirectoryAsync(renameDirectory, token)).UnwrapErr(),
            (await application.MoveFileAsync(moveFile, token)).UnwrapErr(),
            (await application.MoveDirectoryAsync(moveDirectory, token)).UnwrapErr(),
            (await application.CopyFileAsync(copyFile, token)).UnwrapErr(),
            (await application.CopyDirectoryAsync(copyDirectory, token)).UnwrapErr()
        ];

        Assert.Equal(
        [
            BuiltInProtocolDefinitions.GetDirectoryInfo,
            BuiltInProtocolDefinitions.GetFileInfo,
            BuiltInProtocolDefinitions.CreateDirectory,
            BuiltInProtocolDefinitions.DeleteFile,
            BuiltInProtocolDefinitions.DeleteDirectory,
            BuiltInProtocolDefinitions.RenameFile,
            BuiltInProtocolDefinitions.RenameDirectory,
            BuiltInProtocolDefinitions.MoveFile,
            BuiltInProtocolDefinitions.MoveDirectory,
            BuiltInProtocolDefinitions.CopyFile,
            BuiltInProtocolDefinitions.CopyDirectory
        ], invoker.Calls.Select(static call => call.Descriptor));
        Assert.Equal(
        [
            directoryInfo, fileInfo, createDirectory, deleteFile, deleteDirectory, renameFile,
            renameDirectory, moveFile, moveDirectory, copyFile, copyDirectory
        ], invoker.Calls.Select(static call => call.Request));
        Assert.All(invoker.Calls, call => Assert.Equal(token, call.CancellationToken));
        Assert.All(errors, error => Assert.Same(invoker.Sentinel, error));
    }

    [Fact]
    public async Task NotReadySessionOpenReturnsTypedTransportError()
    {
        await using var owner = new V2ClientConnectionOwner(
            new NeverUsedSessionFactory(), TimeProvider.System, TimeSpan.Zero);
        var application = new RemoteFileApplication(new V2RemoteApplicationInvoker(owner), owner);

        var upload = await application.OpenUploadAsync(
            new UploadOpenRequest("file", 0, EmptySha256), CancellationToken.None);
        var download = await application.OpenDownloadAsync(new DownloadOpenRequest("file"), CancellationToken.None);

        Assert.Equal("client.not_ready", upload.UnwrapErr().Code);
        Assert.Equal("client.not_ready", download.UnwrapErr().Code);
        Assert.Equal(0, application.CoreEntryCount);
    }

    [Fact]
    public async Task UploadValidatesLocallySequencesAcknowledgedChunksAndClosesOnTheBoundCore()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, length: 4, maximumChunkSize: 2);
        var binaryBefore = harness.Session.Transport.BinaryHistory.Count;

        var invalid = new[]
        {
            await harness.Application.WriteUploadChunkAsync(
                new UploadChunkRequest(id, 1, ImmutableArray.Create<byte>(1)), CancellationToken.None),
            await harness.Application.WriteUploadChunkAsync(
                new UploadChunkRequest(id, 0, default), CancellationToken.None),
            await harness.Application.WriteUploadChunkAsync(
                new UploadChunkRequest(id, 0, ImmutableArray.Create<byte>(1, 2, 3)), CancellationToken.None),
            await harness.Application.WriteUploadChunkAsync(
                new UploadChunkRequest(id, 0, ImmutableArray.Create<byte>(1, 2, 3, 4, 5)), CancellationToken.None)
        };

        Assert.Equal(
            ["file.chunk.offset.invalid", "file.chunk.data.invalid", "file.chunk.too_large", "file.chunk.too_large"],
            invalid.Select(static result => result.UnwrapErr().Code));
        Assert.Equal(binaryBefore, harness.Session.Transport.BinaryHistory.Count);

        await WriteAcceptedAsync(harness, id, 0, 1, 2);
        await WriteAcceptedAsync(harness, id, 2, 3, 4);
        var closing = harness.Application.CloseUploadAsync(id, CancellationToken.None);
        var close = await harness.Session.Transport.NextTextAsync("mcsl.file.upload.close");
        Assert.Equal(id, Params(close).GetProperty("session_id").GetGuid());
        harness.Session.RouteSuccess(close);

        Assert.True((await closing.WaitAsync(Timeout)).IsOk(out _));
        Assert.Equal(0, harness.Application.LeaseCount);
    }

    [Fact]
    public async Task UploadBoundsFailureUsesDeclaredLengthWithoutSendingBinary()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, length: 3, maximumChunkSize: 4);
        await WriteAcceptedAsync(harness, id, 0, 1, 2);
        var binaryBefore = harness.Session.Transport.BinaryHistory.Count;

        var result = await harness.Application.WriteUploadChunkAsync(
            new UploadChunkRequest(id, 2, ImmutableArray.Create<byte>(3, 4)), CancellationToken.None);

        Assert.Equal("file.upload.size_exceeded", result.UnwrapErr().Code);
        Assert.Equal(binaryBefore, harness.Session.Transport.BinaryHistory.Count);
        await CancelUploadAsync(harness, id);
    }

    [Fact]
    public async Task UploadAuthoritativeErrorsFollowTerminalClassificationAndPoisonCleanupUsesCancel()
    {
        await using var terminalHarness = await Harness.CreateAsync();
        var terminalId = Guid.NewGuid();
        await OpenUploadAsync(terminalHarness, terminalId, 1, 1);
        var terminalWrite = terminalHarness.Application.WriteUploadChunkAsync(
            Chunk(terminalId, 0, 1), CancellationToken.None);
        _ = await terminalHarness.Session.Transport.NextBinaryAsync();
        terminalHarness.Session.RouteUploadRejected(
            terminalId, 0, 1, "file.chunk.offset.invalid", "validation");
        var terminalResult = await terminalWrite.WaitAsync(Timeout);
        Assert.Equal("file.chunk.offset.invalid", terminalResult.UnwrapErr().Code);
        Assert.Equal(0, terminalHarness.Application.LeaseCount);
        Assert.Equal(0, terminalHarness.Session.Transport.PendingTextCount);

        await using var retainedHarness = await Harness.CreateAsync();
        var retainedId = Guid.NewGuid();
        await OpenUploadAsync(retainedHarness, retainedId, 1, 1);
        var retainedWrite = retainedHarness.Application.WriteUploadChunkAsync(
            Chunk(retainedId, 0, 1), CancellationToken.None);
        _ = await retainedHarness.Session.Transport.NextBinaryAsync();
        retainedHarness.Session.RouteUploadRejected(
            retainedId, 0, 1, "remote.upload_rejected", "conflict");
        var compensation = await retainedHarness.Session.Transport.NextTextAsync("mcsl.file.upload.cancel");
        Assert.Equal(retainedId, Params(compensation).GetProperty("session_id").GetGuid());
        retainedHarness.Session.RouteSuccess(compensation);
        var retainedResult = await retainedWrite.WaitAsync(Timeout);
        Assert.Equal("remote.upload_rejected", retainedResult.UnwrapErr().Code);
        Assert.Equal(0, retainedHarness.Application.LeaseCount);
    }

    [Fact]
    public async Task UploadIncompleteCloseRetainsLeaseAndCancelRemainsAvailable()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 2, 2);

        var closing = harness.Application.CloseUploadAsync(id, CancellationToken.None);
        var close = await harness.Session.Transport.NextTextAsync("mcsl.file.upload.close");
        harness.Session.RouteError(close, "file.upload.incomplete", "validation");

        Assert.Equal("file.upload.incomplete", (await closing.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.Equal(1, harness.Application.LeaseCount);
        await CancelUploadAsync(harness, id);
        Assert.Equal(0, harness.Application.LeaseCount);
    }

    [Theory]
    [InlineData(false, "file.upload.chunk_in_flight")]
    [InlineData(false, "file.session.busy")]
    [InlineData(false, "file.upload.incomplete")]
    [InlineData(true, "file.upload.chunk_in_flight")]
    [InlineData(true, "file.session.busy")]
    public async Task UploadEndAcquireConflictsRetainLease(bool cancel, string code)
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 1, 1);

        var ending = cancel
            ? harness.Application.CancelUploadAsync(id, CancellationToken.None)
            : harness.Application.CloseUploadAsync(id, CancellationToken.None);
        var method = cancel ? "mcsl.file.upload.cancel" : "mcsl.file.upload.close";
        var request = await harness.Session.Transport.NextTextAsync(method);
        harness.Session.RouteError(request, code, "conflict");

        Assert.Equal(code, (await ending.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.Equal(1, harness.Application.LeaseCount);
        await CancelUploadAsync(harness, id);
    }

    [Fact]
    public async Task SessionKindMismatchIsLocalAndDoesNotEndTheOtherKind()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 0, 1);
        var pendingBefore = harness.Session.Transport.PendingTextCount;

        var result = await harness.Application.CloseDownloadAsync(id, CancellationToken.None);

        Assert.Equal("file.session.kind_mismatch", result.UnwrapErr().Code);
        Assert.Equal(pendingBefore, harness.Session.Transport.PendingTextCount);
        Assert.Equal(1, harness.Application.LeaseCount);
        await CancelUploadAsync(harness, id);
    }

    [Fact]
    public async Task InvalidAndDuplicateUploadOpensInvalidateOnlyTheUnsafeEpoch()
    {
        await using var invalidHarness = await Harness.CreateAsync();
        var invalidId = Guid.NewGuid();
        var invalidSession = invalidHarness.Session;
        var invalidCore = invalidSession.Coordinator.Core;
        var invalidOpen = invalidHarness.Application.OpenUploadAsync(
            new UploadOpenRequest("invalid", 0, EmptySha256), CancellationToken.None);
        var invalidCleanup = invalidHarness.Application.WaitForCoreCleanupAsync(invalidCore);
        var invalidRequest = await invalidSession.Transport.NextTextAsync("mcsl.file.upload.open");
        invalidSession.RouteSuccess(invalidRequest, UploadSessionJson(invalidId, 0));

        Assert.Equal("protocol.upload_session_invalid", (await invalidOpen.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.DoesNotContain(
            invalidSession.Transport.TextHistory,
            static request => request.Method == "mcsl.file.upload.cancel");
        await invalidCleanup.WaitAsync(Timeout);
        Assert.True(invalidCore.Closed.IsCompleted);
        Assert.Equal(0, invalidHarness.Application.LeaseCount);
        Assert.Equal(0, invalidHarness.Application.CoreEntryCount);
        await invalidHarness.AttachReplacementAsync();
        await OpenUploadAsync(invalidHarness, invalidId, 0, 1);
        await CancelUploadAsync(invalidHarness, invalidId);
        Assert.True(invalidHarness.Owner.IsReady);

        await using var duplicateHarness = await Harness.CreateAsync();
        var duplicateId = Guid.NewGuid();
        await OpenUploadAsync(duplicateHarness, duplicateId, 0, 1);
        var duplicateOpen = duplicateHarness.Application.OpenUploadAsync(
            new UploadOpenRequest("duplicate", 0, EmptySha256), CancellationToken.None);
        var duplicateRequest = await duplicateHarness.Session.Transport.NextTextAsync("mcsl.file.upload.open");
        duplicateHarness.Session.RouteSuccess(duplicateRequest, UploadSessionJson(duplicateId, 0));
        var duplicateResult = await duplicateOpen.WaitAsync(Timeout);
        Assert.Equal("protocol.file_session_duplicate", duplicateResult.UnwrapErr().Code);
        var duplicateCore = duplicateHarness.Session.Coordinator.Core;
        await duplicateHarness.Application.WaitForCoreCleanupAsync(duplicateCore).WaitAsync(Timeout);
        Assert.Equal(0, duplicateHarness.Application.LeaseCount);
        Assert.True(duplicateCore.Closed.IsCompleted);
        Assert.Equal(0, duplicateHarness.Session.Transport.PendingTextCount);
        Assert.DoesNotContain(
            duplicateHarness.Session.Transport.TextHistory,
            static request => request.Method == "mcsl.file.upload.cancel");
        await duplicateHarness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(duplicateHarness);
    }

    [Fact]
    public async Task CompensationFailureInvalidatesExactEpochAndReplacementCanContinue()
    {
        using var blocker = new ClosedObserverBlocker();
        var hooks = new RemoteFileApplicationTestHooks { BeforeClosedObserver = blocker.BlockAsync };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 0, 1);
        var oldSession = harness.Session;
        var oldCore = oldSession.Coordinator.Core;
        blocker.Core = oldCore;
        var oldCleanup = harness.Application.WaitForCoreCleanupAsync(oldCore);

        harness.Owner.InvalidateEpoch(
            oldCore,
            new TransportDaemonError("test.cross_core", "replace old upload epoch"));
        await blocker.Reached.Task.WaitAsync(Timeout);

        try
        {
            var poisonedSession = await harness.AttachReplacementAsync();
            var poisonedCore = poisonedSession.Coordinator.Core;
            var opening = harness.Application.OpenUploadAsync(
                new UploadOpenRequest("duplicate", 0, EmptySha256), CancellationToken.None);
            var cleanup = harness.Application.WaitForCoreCleanupAsync(poisonedCore);
            var open = await poisonedSession.Transport.NextTextAsync("mcsl.file.upload.open");
            poisonedSession.RouteSuccess(open, UploadSessionJson(id, maximumChunkSize: 1));
            var compensation = await poisonedSession.Transport.NextTextAsync("mcsl.file.upload.cancel");
            Assert.Equal(id, Params(compensation).GetProperty("session_id").GetGuid());
            poisonedSession.RouteError(compensation, "storage.compensation_failed", "storage");

            Assert.Equal("protocol.file_session_duplicate", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
            Assert.True(poisonedCore.Closed.IsCompleted);
            await cleanup.WaitAsync(Timeout);
            Assert.Equal(1, harness.Application.LeaseCount);
            Assert.DoesNotContain(
                oldSession.Transport.TextHistory,
                static request => request.Method == "mcsl.file.upload.cancel");
            await harness.AttachReplacementAsync();
            await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
        }
        finally
        {
            blocker.Release();
        }

        await oldCleanup.WaitAsync(Timeout);
        Assert.Equal(0, harness.Application.LeaseCount);
    }

    [Fact]
    public async Task InvalidSameCoreDownloadDuplicateNeverClosesBySessionId()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        var oldSession = harness.Session;
        var oldCore = oldSession.Coordinator.Core;
        var cleanup = harness.Application.WaitForCoreCleanupAsync(oldCore);
        var opening = harness.Application.OpenDownloadAsync(
            new DownloadOpenRequest("duplicate"), CancellationToken.None);
        var open = await oldSession.Transport.NextTextAsync("mcsl.file.download.open");
        oldSession.RouteSuccess(open, DownloadSessionJson(id, 1, "invalid", 0));

        Assert.Equal("protocol.file_session_duplicate", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.DoesNotContain(
            oldSession.Transport.TextHistory,
            static request => request.Method == "mcsl.file.download.close");
        await cleanup.WaitAsync(Timeout);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        await harness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Fact]
    public async Task InvalidUniqueDownloadInvalidatesWithoutRegistrationOrHashLeak()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        var invalidSession = harness.Session;
        var core = invalidSession.Coordinator.Core;
        var opening = harness.Application.OpenDownloadAsync(
            new DownloadOpenRequest("invalid"), CancellationToken.None);
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);
        var open = await invalidSession.Transport.NextTextAsync("mcsl.file.download.open");
        invalidSession.RouteSuccess(open, DownloadSessionJson(id, 1, "invalid", 1));

        Assert.Equal("protocol.download_session_invalid", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.DoesNotContain(
            invalidSession.Transport.TextHistory,
            static request => request.Method == "mcsl.file.download.close");
        await cleanup.WaitAsync(Timeout);
        Assert.True(core.Closed.IsCompleted);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(0, harness.Application.DisposedDownloadHashCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
        await harness.AttachReplacementAsync();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        await CloseDownloadAsync(harness, id);
        Assert.True(harness.Owner.IsReady);
    }

    [Fact]
    public async Task MalformedUploadLosingSameCoreCommitRaceInvalidatesWithoutCancelingById()
    {
        var malformedReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMalformed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hookCalls = 0;
        var hooks = new RemoteFileApplicationTestHooks
        {
            BeforeOpenCommit = _ =>
            {
                if (Interlocked.Increment(ref hookCalls) != 1)
                    return ValueTask.CompletedTask;
                malformedReached.TrySetResult();
                return new ValueTask(releaseMalformed.Task);
            }
        };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        var oldSession = harness.Session;
        var core = oldSession.Coordinator.Core;
        var malformedOpening = harness.Application.OpenUploadAsync(
            new UploadOpenRequest("malformed", 0, EmptySha256), CancellationToken.None);
        var malformedOpen = await oldSession.Transport.NextTextAsync("mcsl.file.upload.open");
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);

        try
        {
            oldSession.RouteSuccess(malformedOpen, UploadSessionJson(id, maximumChunkSize: 0));
            await malformedReached.Task.WaitAsync(Timeout);

            var validOpening = harness.Application.OpenUploadAsync(
                new UploadOpenRequest("valid", 0, EmptySha256), CancellationToken.None);
            var validOpen = await oldSession.Transport.NextTextAsync("mcsl.file.upload.open");
            oldSession.RouteSuccess(validOpen, UploadSessionJson(id, maximumChunkSize: 1));
            Assert.Equal(id, (await validOpening.WaitAsync(Timeout)).Unwrap().SessionId);
            Assert.Equal(1, harness.Application.LeaseCount);
        }
        finally
        {
            releaseMalformed.TrySetResult();
        }

        Assert.Equal(
            "protocol.file_session_duplicate",
            (await malformedOpening.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.DoesNotContain(
            oldSession.Transport.TextHistory,
            static request => request.Method == "mcsl.file.upload.cancel");
        await cleanup.WaitAsync(Timeout);
        Assert.True(core.Closed.IsCompleted);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
        await harness.AttachReplacementAsync();
        await OpenUploadAsync(harness, id, 0, 1);
        await CancelUploadAsync(harness, id);
        Assert.True(harness.Owner.IsReady);
    }

    [Fact]
    public async Task MalformedDownloadLosingSameCoreCommitRaceInvalidatesWithoutClosingById()
    {
        var malformedReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMalformed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hookCalls = 0;
        var hooks = new RemoteFileApplicationTestHooks
        {
            BeforeOpenCommit = _ =>
            {
                if (Interlocked.Increment(ref hookCalls) != 1)
                    return ValueTask.CompletedTask;
                malformedReached.TrySetResult();
                return new ValueTask(releaseMalformed.Task);
            }
        };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        var oldSession = harness.Session;
        var core = oldSession.Coordinator.Core;
        var malformedOpening = harness.Application.OpenDownloadAsync(
            new DownloadOpenRequest("malformed"), CancellationToken.None);
        var malformedOpen = await oldSession.Transport.NextTextAsync("mcsl.file.download.open");
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);

        try
        {
            oldSession.RouteSuccess(malformedOpen, DownloadSessionJson(id, 1, "invalid", 1));
            await malformedReached.Task.WaitAsync(Timeout);

            var validOpening = harness.Application.OpenDownloadAsync(
                new DownloadOpenRequest("valid"), CancellationToken.None);
            var validOpen = await oldSession.Transport.NextTextAsync("mcsl.file.download.open");
            oldSession.RouteSuccess(validOpen, DownloadSessionJson(id, 1, Sha256([1]), 1));
            Assert.Equal(id, (await validOpening.WaitAsync(Timeout)).Unwrap().SessionId);
            Assert.Equal(1, harness.Application.LeaseCount);
            Assert.Equal(0, harness.Application.DisposedDownloadHashCount);
        }
        finally
        {
            releaseMalformed.TrySetResult();
        }

        Assert.Equal(
            "protocol.download_session_invalid",
            (await malformedOpening.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.DoesNotContain(
            oldSession.Transport.TextHistory,
            static request => request.Method == "mcsl.file.download.close");
        await cleanup.WaitAsync(Timeout);
        Assert.True(core.Closed.IsCompleted);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.False(harness.Application.TryGetDownloadState(id, out _, out _));
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
        await harness.AttachReplacementAsync();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        await CloseDownloadAsync(harness, id);
        Assert.True(harness.Owner.IsReady);
    }

    [Fact]
    public async Task OpenCloseRacesRejectBeforeCommitAndCleanAfterCommit()
    {
        var commitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hooks = new RemoteFileApplicationTestHooks
        {
            BeforeOpenCommit = _ =>
            {
                commitReached.TrySetResult();
                return new ValueTask(releaseCommit.Task);
            }
        };
        await using var beforeCommit = await Harness.CreateAsync(hooks);
        var opening = beforeCommit.Application.OpenUploadAsync(
            new UploadOpenRequest("raced", 0, EmptySha256), CancellationToken.None);
        var request = await beforeCommit.Session.Transport.NextTextAsync("mcsl.file.upload.open");
        var core = beforeCommit.Session.Coordinator.Core;
        var beforeCleanup = beforeCommit.Application.WaitForCoreCleanupAsync(core);
        try
        {
            beforeCommit.Session.RouteSuccess(request, UploadSessionJson(Guid.NewGuid(), 1));
            await commitReached.Task.WaitAsync(Timeout);
            core.Close();
        }
        finally
        {
            releaseCommit.TrySetResult();
        }
        Assert.Equal("connection.closed", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
        await beforeCleanup.WaitAsync(Timeout);
        Assert.Equal(0, beforeCommit.Application.CoreEntryCount);

        await using var afterCommit = await Harness.CreateAsync();
        var committedId = Guid.NewGuid();
        await OpenUploadAsync(afterCommit, committedId, 0, 1);
        var committedCore = afterCommit.Session.Coordinator.Core;
        var cleanup = afterCommit.Application.WaitForCoreCleanupAsync(committedCore);
        committedCore.Close();
        await cleanup.WaitAsync(Timeout);
        Assert.Equal(0, afterCommit.Application.LeaseCount);
        Assert.Equal(0, afterCommit.Application.CoreEntryCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task OpenCommitHookFailureFinishesAdmissionAndInvalidatesExactEpoch(
        bool download,
        bool cancelHook)
    {
        var hookReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hookCalls = 0;
        V2ClientConnectionCore? observedCore = null;
        var hooks = new RemoteFileApplicationTestHooks
        {
            BeforeOpenCommit = core =>
            {
                if (Interlocked.Increment(ref hookCalls) != 1)
                    return ValueTask.CompletedTask;
                observedCore = core;
                hookReached.TrySetResult();
                return new ValueTask(releaseHook.Task);
            }
        };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        var session = harness.Session;
        var core = session.Coordinator.Core;
        Task opening;
        SentRequest open;
        if (download)
        {
            opening = harness.Application.OpenDownloadAsync(
                new DownloadOpenRequest("hook-failure"), CancellationToken.None);
            open = await session.Transport.NextTextAsync("mcsl.file.download.open");
        }
        else
        {
            opening = harness.Application.OpenUploadAsync(
                new UploadOpenRequest("hook-failure", 0, EmptySha256), CancellationToken.None);
            open = await session.Transport.NextTextAsync("mcsl.file.upload.open");
        }
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);
        var hookException = new InvalidOperationException("open commit hook failed");
        using var hookCancellation = new CancellationTokenSource();
        hookCancellation.Cancel();

        try
        {
            if (download)
                session.RouteSuccess(open, DownloadSessionJson(id, 1, Sha256([1]), 1));
            else
                session.RouteSuccess(open, UploadSessionJson(id, maximumChunkSize: 1));
            await hookReached.Task.WaitAsync(Timeout);
            Assert.Same(core, observedCore);

            if (cancelHook)
            {
                releaseHook.TrySetCanceled(hookCancellation.Token);
                var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
                Assert.Equal(hookCancellation.Token, exception.CancellationToken);
            }
            else
            {
                releaseHook.TrySetException(hookException);
                Assert.Same(hookException, await Assert.ThrowsAsync<InvalidOperationException>(() => opening));
            }
        }
        finally
        {
            releaseHook.TrySetResult();
        }

        await cleanup.WaitAsync(Timeout);
        Assert.True(core.Closed.IsCompleted);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(0, harness.Application.DisposedDownloadHashCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
        Assert.DoesNotContain(
            session.Transport.TextHistory,
            request => request.Method == (download ? "mcsl.file.download.close" : "mcsl.file.upload.cancel"));
        await harness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedObserverHookFailureCompletesAfterLedgerAndHashCleanup(bool cancelHook)
    {
        var hookReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hooks = new RemoteFileApplicationTestHooks
        {
            BeforeClosedObserver = _ =>
            {
                hookReached.TrySetResult();
                return new ValueTask(releaseHook.Task);
            }
        };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        var core = harness.Session.Coordinator.Core;
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);
        var hookException = new InvalidOperationException("closed observer hook failed");
        using var hookCancellation = new CancellationTokenSource();
        hookCancellation.Cancel();

        try
        {
            core.Close();
            await hookReached.Task.WaitAsync(Timeout);
            if (cancelHook)
            {
                releaseHook.TrySetCanceled(hookCancellation.Token);
                var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanup.WaitAsync(Timeout));
                Assert.Equal(hookCancellation.Token, exception.CancellationToken);
            }
            else
            {
                releaseHook.TrySetException(hookException);
                Assert.Same(
                    hookException,
                    await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.WaitAsync(Timeout)));
            }
        }
        finally
        {
            releaseHook.TrySetResult();
        }

        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.False(harness.Application.TryGetDownloadState(id, out _, out _));
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
    }

    [Fact]
    public async Task AmbiguousOpenAndUploadWriteInvalidateTheExactEpochAndReplacementCanContinue()
    {
        await using var openHarness = await Harness.CreateAsync();
        var opening = openHarness.Application.OpenUploadAsync(
            new UploadOpenRequest("ambiguous-open", 1, EmptySha256), CancellationToken.None);
        _ = await openHarness.Session.Transport.NextTextAsync("mcsl.file.upload.open");
        var openCore = openHarness.Session.Coordinator.Core;
        openCore.Close();
        Assert.Equal("connection.closed", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
        await openHarness.Application.WaitForCoreCleanupAsync(openCore).WaitAsync(Timeout);
        await openHarness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(openHarness);

        await using var writeHarness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(writeHarness, id, 1, 1);
        var writing = writeHarness.Application.WriteUploadChunkAsync(Chunk(id, 0, 1), CancellationToken.None);
        _ = await writeHarness.Session.Transport.NextBinaryAsync();
        var writeCore = writeHarness.Session.Coordinator.Core;
        writeCore.Close();
        Assert.Equal("connection.closed", (await writing.WaitAsync(Timeout)).UnwrapErr().Code);
        await writeHarness.Application.WaitForCoreCleanupAsync(writeCore).WaitAsync(Timeout);
        Assert.Equal(0, writeHarness.Application.LeaseCount);
        await writeHarness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(writeHarness);
    }

    [Fact]
    public async Task AmbiguousEndInvalidatesExactEpochAndReplacementCanContinue()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 0, 1);
        var poisonedCore = harness.Session.Coordinator.Core;
        var cleanup = harness.Application.WaitForCoreCleanupAsync(poisonedCore);
        var canceling = harness.Application.CancelUploadAsync(id, CancellationToken.None);
        _ = await harness.Session.Transport.NextTextAsync("mcsl.file.upload.cancel");

        poisonedCore.Close();

        Assert.Equal("connection.closed", (await canceling.WaitAsync(Timeout)).UnwrapErr().Code);
        await cleanup.WaitAsync(Timeout);
        await harness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Fact]
    public async Task DownloadHashAdvancesOnlyForContiguousPrefixAndMatchRemainsClosable()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        byte[] complete = [1, 2, 3, 4];
        await OpenDownloadAsync(harness, id, complete, maximumChunkSize: 4);

        await ReadAsync(harness, id, offset: 2, maximumLength: 2, 3, 4);
        AssertDownloadState(harness.Application, id, 0, hashVerified: false);
        await ReadAsync(harness, id, offset: 0, maximumLength: 2, 1, 2);
        AssertDownloadState(harness.Application, id, 2, hashVerified: false);
        await ReadAsync(harness, id, offset: 0, maximumLength: 2, 1, 2);
        AssertDownloadState(harness.Application, id, 2, hashVerified: false);
        await ReadAsync(harness, id, offset: 1, maximumLength: 2, 2, 3);
        AssertDownloadState(harness.Application, id, 2, hashVerified: false);
        await ReadAsync(harness, id, offset: 2, maximumLength: 2, 3, 4);
        AssertDownloadState(harness.Application, id, 4, hashVerified: true);

        await CloseDownloadAsync(harness, id);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
    }

    [Fact]
    public async Task DownloadMismatchRemovesLeaseDisposesHashAndReplacementCanContinue()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [9, 9], maximumChunkSize: 2);

        var result = await ReadAsync(harness, id, offset: 0, maximumLength: 2, 1, 2);

        Assert.Equal("protocol.download_hash_mismatch", result.UnwrapErr().Code);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        var poisonedCore = harness.Session.Coordinator.Core;
        Assert.True(poisonedCore.Closed.IsCompleted);
        await harness.Application.WaitForCoreCleanupAsync(poisonedCore).WaitAsync(Timeout);
        await harness.AttachReplacementAsync();
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Fact]
    public async Task EmptyDownloadRequiresSuccessfulEmptyFinalReadBeforeVerification()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [], maximumChunkSize: 1);
        AssertDownloadState(harness.Application, id, 0, hashVerified: false);

        var result = await ReadAsync(harness, id, offset: 0, maximumLength: 1);

        Assert.True(result.IsOk(out var chunk));
        Assert.True(chunk!.IsFinal);
        Assert.Empty(chunk.Data);
        AssertDownloadState(harness.Application, id, 0, hashVerified: true);
        await CloseDownloadAsync(harness, id);
    }

    [Fact]
    public async Task DownloadReadRetainsNonterminalErrorsAndRemovesTerminalErrors()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);

        var retainedRead = harness.Application.ReadDownloadChunkAsync(
            new DownloadChunkRequest(id, 0, 1), CancellationToken.None);
        var retainedRequest = await harness.Session.Transport.NextTextAsync("mcsl.file.download.read");
        harness.Session.RouteError(retainedRequest, "remote.read_rejected", "conflict");
        Assert.Equal("remote.read_rejected", (await retainedRead.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.Equal(1, harness.Application.LeaseCount);

        var terminalRead = harness.Application.ReadDownloadChunkAsync(
            new DownloadChunkRequest(id, 0, 1), CancellationToken.None);
        var terminalRequest = await harness.Session.Transport.NextTextAsync("mcsl.file.download.read");
        harness.Session.RouteError(terminalRequest, "file.session.expired", "not_found");
        Assert.Equal("file.session.expired", (await terminalRead.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
    }

    [Fact]
    public async Task CallerCanceledDownloadKeepsBusyCloseRetryableUntilLatePairDrains()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        using var cancellation = new CancellationTokenSource();
        var reading = harness.Application.ReadDownloadChunkAsync(
            new DownloadChunkRequest(id, 0, 1), cancellation.Token);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.download.read");

        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        AssertDownloadState(harness.Application, id, 0, hashVerified: false);
        Assert.Equal(1, harness.Session.Coordinator.Core.AbandonedDownloadCount);

        var busyClose = harness.Application.CloseDownloadAsync(id, CancellationToken.None);
        var busyRequest = await harness.Session.Transport.NextTextAsync("mcsl.file.download.close");
        harness.Session.RouteError(busyRequest, "file.session.busy", "conflict");
        Assert.Equal("file.session.busy", (await busyClose.WaitAsync(Timeout)).UnwrapErr().Code);
        Assert.Equal(1, harness.Application.LeaseCount);
        Assert.False(harness.Session.Coordinator.Core.TryRegisterDownloadSession(
            new DownloadSession(id, 1, Sha256([1]), 1, FutureExpiry),
            out var duplicateRegistration));
        Assert.Equal("file.download.session_duplicate", duplicateRegistration!.Code);

        harness.Session.RouteDownload(request, id, 0, [1], isFinal: true);
        Assert.Equal(0, harness.Session.Coordinator.Core.AbandonedDownloadCount);
        AssertDownloadState(harness.Application, id, 0, hashVerified: false);
        await CloseDownloadAsync(harness, id);
    }

    [Fact]
    public async Task CrossCoreUploadDuplicateCompensatesOnlyNewCoreAndPreservesOldLeaseByIdentity()
    {
        using var blocker = new ClosedObserverBlocker();
        var hooks = new RemoteFileApplicationTestHooks { BeforeClosedObserver = blocker.BlockAsync };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 0, 1);
        var oldSession = harness.Session;
        var oldCore = oldSession.Coordinator.Core;
        blocker.Core = oldCore;
        var oldCleanup = harness.Application.WaitForCoreCleanupAsync(oldCore);

        harness.Owner.InvalidateEpoch(
            oldCore,
            new TransportDaemonError("test.cross_core", "replace old upload epoch"));
        await blocker.Reached.Task.WaitAsync(Timeout);

        try
        {
            var replacement = await harness.AttachReplacementAsync();
            var opening = harness.Application.OpenUploadAsync(
                new UploadOpenRequest("duplicate", 0, EmptySha256), CancellationToken.None);
            var open = await replacement.Transport.NextTextAsync("mcsl.file.upload.open");
            replacement.RouteSuccess(open, UploadSessionJson(id, 0));
            var compensation = await replacement.Transport.NextTextAsync("mcsl.file.upload.cancel");
            Assert.Equal(id, Params(compensation).GetProperty("session_id").GetGuid());
            replacement.RouteSuccess(compensation);

            Assert.Equal("protocol.file_session_duplicate", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
            Assert.Equal(1, harness.Application.LeaseCount);
            Assert.DoesNotContain(
                oldSession.Transport.TextHistory,
                static request => request.Method == "mcsl.file.upload.cancel");
            Assert.True(harness.Owner.IsReady);
        }
        finally
        {
            blocker.Release();
        }

        await oldCleanup.WaitAsync(Timeout);
        Assert.Equal(0, harness.Application.LeaseCount);
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Fact]
    public async Task CrossCoreDownloadDuplicateRemovesOnlyNewRegistrationAndCompensatesNewCore()
    {
        using var blocker = new ClosedObserverBlocker();
        var hooks = new RemoteFileApplicationTestHooks { BeforeClosedObserver = blocker.BlockAsync };
        await using var harness = await Harness.CreateAsync(hooks);
        var id = Guid.NewGuid();
        var hash = Sha256([1]);
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        var oldSession = harness.Session;
        var oldCore = oldSession.Coordinator.Core;
        blocker.Core = oldCore;
        var oldCleanup = harness.Application.WaitForCoreCleanupAsync(oldCore);

        harness.Owner.InvalidateEpoch(
            oldCore,
            new TransportDaemonError("test.cross_core", "replace old download epoch"));
        await blocker.Reached.Task.WaitAsync(Timeout);

        try
        {
            var replacement = await harness.AttachReplacementAsync();
            var opening = harness.Application.OpenDownloadAsync(
                new DownloadOpenRequest("duplicate"), CancellationToken.None);
            var open = await replacement.Transport.NextTextAsync("mcsl.file.download.open");
            replacement.RouteSuccess(open, DownloadSessionJson(id, 1, "invalid", 1));
            var compensation = await replacement.Transport.NextTextAsync("mcsl.file.download.close");
            Assert.Equal(id, Params(compensation).GetProperty("session_id").GetGuid());
            replacement.RouteSuccess(compensation);

            Assert.Equal("protocol.file_session_duplicate", (await opening.WaitAsync(Timeout)).UnwrapErr().Code);
            Assert.Equal(1, harness.Application.LeaseCount);
            Assert.DoesNotContain(
                oldSession.Transport.TextHistory,
                static request => request.Method == "mcsl.file.download.close");
            var descriptor = new DownloadSession(id, 1, hash, 1, FutureExpiry);
            Assert.True(replacement.Coordinator.Core.TryRegisterDownloadSession(descriptor, out var registrationError));
            Assert.Null(registrationError);
            Assert.True(replacement.Coordinator.Core.TryRemoveDownloadSession(id, out var removalError));
            Assert.Null(removalError);
            Assert.True(harness.Owner.IsReady);
        }
        finally
        {
            blocker.Release();
        }

        await oldCleanup.WaitAsync(Timeout);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        await AssertCurrentCoreCanCompleteFileRpcAsync(harness);
    }

    [Fact]
    public async Task CoreCloseDuringReadReleasesQueuedEndDisposesHashOnceAndNeverDisposesGate()
    {
        await using var harness = await Harness.CreateAsync();
        var id = Guid.NewGuid();
        await OpenDownloadAsync(harness, id, [1], maximumChunkSize: 1);
        var core = harness.Session.Coordinator.Core;
        var cleanup = harness.Application.WaitForCoreCleanupAsync(core);
        var reading = harness.Application.ReadDownloadChunkAsync(
            new DownloadChunkRequest(id, 0, 1), CancellationToken.None);
        _ = await harness.Session.Transport.NextTextAsync("mcsl.file.download.read");
        var queuedClose = harness.Application.CloseDownloadAsync(id, CancellationToken.None);

        core.Close();

        Assert.Equal("connection.closed", (await reading.WaitAsync(Timeout)).UnwrapErr().Code);
        var queuedCloseError = (await queuedClose.WaitAsync(Timeout)).UnwrapErr();
        Assert.True(
            queuedCloseError.Code is "file.session.not_found" or "connection.closed",
            $"Unexpected queued close error: {queuedCloseError.Code}");
        Assert.True(queuedClose.IsCompletedSuccessfully);
        await cleanup.WaitAsync(Timeout);
        Assert.True(cleanup.IsCompletedSuccessfully);
        Assert.Equal(1, harness.Application.DisposedDownloadHashCount);
        Assert.Equal(0, harness.Application.LeaseCount);
        Assert.Equal(0, harness.Application.CoreEntryCount);
    }

    [Fact]
    public async Task ClosedCoreEntriesRemainBoundedAcrossReconnects()
    {
        await using var harness = await Harness.CreateAsync();
        var firstId = Guid.NewGuid();
        await OpenDownloadAsync(harness, firstId, [1], maximumChunkSize: 1);
        var firstCore = harness.Session.Coordinator.Core;
        var firstCleanup = harness.Application.WaitForCoreCleanupAsync(firstCore);

        harness.Session.Lose();
        await firstCleanup.WaitAsync(Timeout);
        var replacement = await harness.AttachReplacementAsync();
        Assert.InRange(harness.Application.CoreEntryCount, 0, 1);

        var secondId = Guid.NewGuid();
        await OpenUploadAsync(harness, secondId, 0, 1);
        Assert.Equal(1, harness.Application.CoreEntryCount);
        var secondCleanup = harness.Application.WaitForCoreCleanupAsync(replacement.Coordinator.Core);
        replacement.Lose();
        await secondCleanup.WaitAsync(Timeout);
        Assert.Equal(0, harness.Application.CoreEntryCount);
    }

    private static async Task OpenUploadAsync(
        Harness harness,
        Guid sessionId,
        long length,
        int maximumChunkSize)
    {
        var opening = harness.Application.OpenUploadAsync(
            new UploadOpenRequest("upload", length, EmptySha256), CancellationToken.None);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.upload.open");
        harness.Session.RouteSuccess(request, UploadSessionJson(sessionId, maximumChunkSize));
        Assert.Equal(sessionId, (await opening.WaitAsync(Timeout)).Unwrap().SessionId);
    }

    private static async Task OpenDownloadAsync(
        Harness harness,
        Guid sessionId,
        byte[] expectedBytes,
        int maximumChunkSize)
    {
        var opening = harness.Application.OpenDownloadAsync(
            new DownloadOpenRequest("download"), CancellationToken.None);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.download.open");
        harness.Session.RouteSuccess(
            request,
            DownloadSessionJson(sessionId, expectedBytes.Length, Sha256(expectedBytes), maximumChunkSize));
        Assert.Equal(sessionId, (await opening.WaitAsync(Timeout)).Unwrap().SessionId);
        harness.DownloadLengths.Add(sessionId, expectedBytes.Length);
    }

    private static async Task WriteAcceptedAsync(Harness harness, Guid sessionId, long offset, params byte[] data)
    {
        var writing = harness.Application.WriteUploadChunkAsync(
            new UploadChunkRequest(sessionId, offset, ImmutableArray.Create(data)), CancellationToken.None);
        var frame = await harness.Session.Transport.NextBinaryAsync();
        Assert.True(BinaryFrameCodec.TryRead(frame.AsSpan(), out var parsed));
        Assert.Equal(BinaryFrameKind.UploadChunk, parsed.Header!.Kind);
        Assert.Equal(sessionId, parsed.Header.SessionId);
        Assert.Equal(offset, parsed.Header.Offset);
        Assert.True(frame.AsSpan()[BinaryFrameCodec.HeaderSize..].SequenceEqual(data));
        harness.Session.RouteUploadAccepted(sessionId, offset, data.Length);
        Assert.True((await writing.WaitAsync(Timeout)).IsOk(out _));
    }

    private static async Task<Result<DownloadChunk, DaemonError>> ReadAsync(
        Harness harness,
        Guid sessionId,
        long offset,
        int maximumLength,
        params byte[] data)
    {
        var reading = harness.Application.ReadDownloadChunkAsync(
            new DownloadChunkRequest(sessionId, offset, maximumLength), CancellationToken.None);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.download.read");
        var isFinal = offset + data.Length == GetExpectedLength(harness, sessionId);
        harness.Session.RouteDownload(request, sessionId, offset, data, isFinal);
        return await reading.WaitAsync(Timeout);
    }

    private static long GetExpectedLength(Harness harness, Guid sessionId)
    {
        Assert.True(harness.DownloadLengths.TryGetValue(sessionId, out var length));
        return length;
    }

    private static async Task CancelUploadAsync(Harness harness, Guid sessionId)
    {
        var canceling = harness.Application.CancelUploadAsync(sessionId, CancellationToken.None);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.upload.cancel");
        harness.Session.RouteSuccess(request);
        Assert.True((await canceling.WaitAsync(Timeout)).IsOk(out _));
    }

    private static async Task CloseDownloadAsync(Harness harness, Guid sessionId)
    {
        var closing = harness.Application.CloseDownloadAsync(sessionId, CancellationToken.None);
        var request = await harness.Session.Transport.NextTextAsync("mcsl.file.download.close");
        harness.Session.RouteSuccess(request);
        Assert.True((await closing.WaitAsync(Timeout)).IsOk(out _));
    }

    private static async Task AssertCurrentCoreCanCompleteFileRpcAsync(Harness harness)
    {
        Assert.True(harness.Owner.IsReady);
        var id = Guid.NewGuid();
        await OpenUploadAsync(harness, id, 0, 1);
        await CancelUploadAsync(harness, id);
        Assert.True(harness.Owner.IsReady);
    }

    private static void AssertDownloadState(
        RemoteFileApplication application,
        Guid sessionId,
        long verifiedPrefix,
        bool hashVerified)
    {
        Assert.True(application.TryGetDownloadState(sessionId, out var actualPrefix, out var actualVerified));
        Assert.Equal(verifiedPrefix, actualPrefix);
        Assert.Equal(hashVerified, actualVerified);
    }

    private static UploadChunkRequest Chunk(Guid sessionId, long offset, params byte[] data) =>
        new(sessionId, offset, ImmutableArray.Create(data));

    private static JsonElement Params(SentRequest request)
    {
        using var document = JsonDocument.Parse(request.Json);
        return document.RootElement.GetProperty("params").Clone();
    }

    private static string UploadSessionJson(Guid sessionId, int maximumChunkSize) =>
        $"{{\"session_id\":\"{sessionId:D}\",\"max_chunk_size\":{maximumChunkSize},\"expires_at\":\"{FutureExpiry:O}\"}}";

    private static string DownloadSessionJson(
        Guid sessionId,
        long length,
        string sha256,
        int maximumChunkSize) =>
        $"{{\"session_id\":\"{sessionId:D}\",\"length\":{length},\"sha256\":\"{sha256}\",\"max_chunk_size\":{maximumChunkSize},\"expires_at\":\"{FutureExpiry:O}\"}}";

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static string EmptySha256 { get; } = Sha256([]);

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            ControlledSessionFactory factory,
            V2ClientConnectionOwner owner,
            ControlledSession session,
            RemoteFileApplication application)
        {
            Factory = factory;
            Owner = owner;
            Session = session;
            Application = application;
        }

        internal ControlledSessionFactory Factory { get; }
        internal V2ClientConnectionOwner Owner { get; }
        internal ControlledSession Session { get; private set; }
        internal RemoteFileApplication Application { get; }
        internal Dictionary<Guid, long> DownloadLengths { get; } = [];

        internal static async Task<Harness> CreateAsync(RemoteFileApplicationTestHooks? hooks = null)
        {
            var factory = new ControlledSessionFactory();
            var owner = new V2ClientConnectionOwner(factory, TimeProvider.System, TimeSpan.Zero);
            try
            {
                var connecting = owner.ConnectAsync();
                var session = await factory.NextAsync();
                await session.CompleteReadinessAsync(connecting);
                var application = new RemoteFileApplication(
                    new V2RemoteApplicationInvoker(owner), owner, TimeProvider.System, hooks);
                return new Harness(factory, owner, session, application);
            }
            catch
            {
                await owner.DisposeAsync();
                throw;
            }
        }

        internal async Task<ControlledSession> AttachReplacementAsync()
        {
            var replacement = await Factory.NextAsync();
            await replacement.CompleteReadinessAsync(connecting: null);
            await WaitUntilAsync(() => Owner.IsReady);
            Session = replacement;
            return replacement;
        }

        public ValueTask DisposeAsync() => Owner.DisposeAsync();
    }

    private sealed class ClosedObserverBlocker : IDisposable
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private V2ClientConnectionCore? _core;

        internal TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal V2ClientConnectionCore Core
        {
            set => Volatile.Write(ref _core, value);
        }

        internal ValueTask BlockAsync(V2ClientConnectionCore core)
        {
            if (!ReferenceEquals(core, Volatile.Read(ref _core)))
                return ValueTask.CompletedTask;
            Reached.TrySetResult();
            return new ValueTask(_release.Task);
        }

        internal void Release() => _release.TrySetResult();

        public void Dispose() => _release.TrySetResult();
    }

    private sealed class ControlledSessionFactory : IV2ClientConnectionSessionFactory
    {
        private readonly ConcurrentQueue<ControlledSession> _sessions = new();
        private readonly SemaphoreSlim _available = new(0);

        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null)
        {
            var session = new ControlledSession(mirror, routeEvent, diagnostic);
            _sessions.Enqueue(session);
            _available.Release();
            return session;
        }

        internal async Task<ControlledSession> NextAsync()
        {
            Assert.True(await _available.WaitAsync(Timeout));
            Assert.True(_sessions.TryDequeue(out var session));
            return session!;
        }
    }

    private sealed class ControlledSession : IV2ClientConnectionSession
    {
        private readonly TaskCompletionSource<DaemonError> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ControlledSession(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic)
        {
            Transport = new RecordingTransport();
            Coordinator = new V2ClientConnectionCoordinator(
                Transport,
                mirror,
                TimeProvider.System,
                TimeSpan.FromMinutes(1),
                diagnostic: diagnostic,
                nonCatalogEvent: routeEvent);
        }

        internal RecordingTransport Transport { get; }
        public V2ClientConnectionCoordinator Coordinator { get; }
        public Task<DaemonError> Completion => _completion.Task;

        public Task<Result<Unit, DaemonError>> ConnectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));

        public Task CloseAsync()
        {
            Coordinator.Core.Close();
            _completion.TrySetResult(new TransportDaemonError("connection.closed", "closed"));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal async Task CompleteReadinessAsync(Task<Result<Unit, DaemonError>>? connecting)
        {
            var subscribe = await Transport.NextTextAsync("mcsl.event.subscribe");
            RouteSuccess(subscribe);
            var catalog = await Transport.NextTextAsync("mcsl.instance.catalog.get");
            RouteSuccess(catalog, "{\"version\":0,\"items\":[]}");
            if (connecting is not null)
                Assert.True((await connecting.WaitAsync(Timeout)).IsOk(out _));
        }

        internal void Lose()
        {
            Coordinator.Core.Close();
            _completion.TrySetResult(new TransportDaemonError("connection.epoch_lost", "lost"));
        }

        internal void RouteSuccess(SentRequest request, string result = "{}") =>
            Coordinator.Core.RouteText(Utf8(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"result\":{result}}}"));

        internal void RouteError(SentRequest request, string code, string kind) =>
            Coordinator.Core.RouteText(Utf8(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{request.IdJson},\"error\":{{\"code\":-32000,\"message\":\"Rejected\",\"data\":{{\"daemon_error_code\":\"{code}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"remote-file-test\"}}}}}}"));

        internal void RouteUploadAccepted(Guid sessionId, long offset, int length) =>
            Coordinator.Core.RouteText(Utf8(
                $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{length},\"status\":\"accepted\"}}}}"));

        internal void RouteUploadRejected(
            Guid sessionId,
            long offset,
            int length,
            string code,
            string kind) =>
            Coordinator.Core.RouteText(Utf8(
                $"{{\"jsonrpc\":\"2.0\",\"method\":\"mcsl.file.upload.ack\",\"params\":{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{length},\"status\":\"rejected\",\"error\":{{\"code\":-32000,\"message\":\"Rejected\",\"data\":{{\"daemon_error_code\":\"{code}\",\"daemon_error_kind\":\"{kind}\",\"correlation_id\":\"remote-file-test\"}}}}}}}}"));

        internal void RouteDownload(
            SentRequest request,
            Guid sessionId,
            long offset,
            byte[] data,
            bool isFinal)
        {
            RouteSuccess(
                request,
                $"{{\"session_id\":\"{sessionId:D}\",\"offset\":{offset},\"length\":{data.Length},\"is_final\":{isFinal.ToString().ToLowerInvariant()}}}");
            Coordinator.Core.RouteBinary(DownloadFrame(sessionId, offset, data));
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    }

    private sealed class RecordingTransport : IV2ClientWireTransport
    {
        private readonly ConcurrentQueue<SentRequest> _text = new();
        private readonly ConcurrentQueue<ImmutableArray<byte>> _binary = new();
        private readonly SemaphoreSlim _textAvailable = new(0);
        private readonly SemaphoreSlim _binaryAvailable = new(0);

        internal ConcurrentQueue<SentRequest> TextHistory { get; } = new();
        internal ConcurrentQueue<ImmutableArray<byte>> BinaryHistory { get; } = new();
        internal int PendingTextCount => _text.Count;

        public ValueTask SendTextAsync(ImmutableArray<byte> utf8Json, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = Encoding.UTF8.GetString(utf8Json.AsSpan());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var request = new SentRequest(
                root.GetProperty("method").GetString()!,
                root.GetProperty("id").GetRawText(),
                json);
            _text.Enqueue(request);
            TextHistory.Enqueue(request);
            _textAvailable.Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(ImmutableArray<byte> frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _binary.Enqueue(frame);
            BinaryHistory.Enqueue(frame);
            _binaryAvailable.Release();
            return ValueTask.CompletedTask;
        }

        internal async Task<SentRequest> NextTextAsync(string expectedMethod)
        {
            Assert.True(await _textAvailable.WaitAsync(Timeout));
            Assert.True(_text.TryDequeue(out var request));
            Assert.Equal(expectedMethod, request!.Method);
            return request;
        }

        internal async Task<ImmutableArray<byte>> NextBinaryAsync()
        {
            Assert.True(await _binaryAvailable.WaitAsync(Timeout));
            Assert.True(_binary.TryDequeue(out var frame));
            return frame;
        }
    }

    private sealed class RecordingInvoker : IRemoteApplicationInvoker
    {
        internal List<Call> Calls { get; } = [];
        internal DaemonError Sentinel { get; } = new InternalDaemonError("test.result", "recorded");

        public Task<Result<TResult, DaemonError>> InvokeAsync<TRequest, TResult>(
            RpcDescriptor<TRequest, TResult> descriptor,
            TRequest request,
            CancellationToken cancellationToken)
            where TResult : notnull
        {
            Calls.Add(new(descriptor, request!, cancellationToken));
            return Task.FromResult(Result.Err<TResult, DaemonError>(Sentinel));
        }

        public Task<Result<Unit, DaemonError>> InvokeUnitAsync<TRequest>(
            RpcDescriptor<TRequest, UnitResult> descriptor,
            TRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(new(descriptor, request!, cancellationToken));
            return Task.FromResult(Result.Err<Unit, DaemonError>(Sentinel));
        }
    }

    private sealed class NeverUsedSessionFactory : IV2ClientConnectionSessionFactory
    {
        public IV2ClientConnectionSession Create(
            RemoteInstanceCatalogMirror mirror,
            Action<V2ClientConnectionCoordinator, JsonRpcRemoteEventNotification> routeEvent,
            Action<V2ClientDiagnostic>? diagnostic = null) =>
            throw new InvalidOperationException("This test must not create a physical session.");
    }

    private sealed record SentRequest(string Method, string IdJson, string Json);
    private sealed record Call(RpcDescriptor Descriptor, object Request, CancellationToken CancellationToken);

    private static byte[] DownloadFrame(Guid sessionId, long offset, byte[] payload)
    {
        var frame = new byte[BinaryFrameCodec.HeaderSize + payload.Length];
        Assert.True(BinaryFrameCodec.TryWrite(
            frame,
            new BinaryFrameHeader(
                BinaryFrameKind.DownloadChunk,
                sessionId,
                offset,
                checked((uint)payload.Length)),
            payload,
            out var error));
        Assert.Equal(BinaryFrameWriteError.None, error);
        return frame;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        while (!predicate())
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
