using System.Collections.Concurrent;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class OperationCoordinatorTests
{
    [Fact]
    public async Task Start_ReturnsPersistedHandleBeforeBlockedExecutorAndIgnoresRequestCancellation()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-start-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            using var requestCancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var result = await coordinator.StartAsync(
                kind: "test.work",
                target: "t1",
                ownerPrincipal: "owner-a",
                executor: async (_, context, ct) =>
                {
                    context.SetStage(OperationStage.Downloading);
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                    return Result.Ok<string, DaemonError>("result-ref");
                },
                cancellationToken: requestCancellation.Token);

            Assert.True(result.IsOk(out var accepted));
            Assert.Equal(OperationStatus.Queued, accepted!.Status);
            Assert.True(File.Exists(Path.Combine(root, "index.json")));

            if (await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(3))) != entered.Task)
            {
                var stalled = await GetAsync(coordinator, accepted.OperationId, "owner-a");
                Assert.Fail($"Executor did not start. Status={stalled.Status}, Error={stalled.ErrorCode}: {stalled.ErrorMessage}");
            }
            requestCancellation.Cancel();
            var running = await GetAsync(coordinator, accepted.OperationId, "owner-a");
            Assert.Equal(OperationStatus.Running, running.Status);

            release.TrySetResult();
            var completed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-a");
            Assert.Equal(OperationStatus.Succeeded, completed.Status);
            Assert.Equal("result-ref", completed.ResultReference);

            var list = await coordinator.ListOperationsAsync(new OperationListQuery("owner-a"), CancellationToken.None);
            Assert.True(list.IsOk(out var listed));
            Assert.Contains(listed!.Operations, item => item.OperationId == accepted.OperationId);

            var denied = await coordinator.GetOperationAsync(
                new OperationReference(accepted.OperationId, "owner-b"),
                CancellationToken.None);
            Assert.True(denied.IsErr(out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Start_RequestCancellationBeforeAcceptanceCreatesNoOperation()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-pre-cancel-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.StartAsync(
                kind: "test.cancel-before-accept",
                target: null,
                ownerPrincipal: "owner-a",
                executor: static (_, _, _) => Task.FromResult(Result.Ok<string, DaemonError>("unused")),
                cancellationToken: cancellation.Token));

            var list = await coordinator.ListOperationsAsync(new OperationListQuery("owner-a"), CancellationToken.None);
            Assert.True(list.IsOk(out var operations));
            Assert.Empty(operations!.Operations);
            Assert.Equal(0, coordinator.PersistenceWriteCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Start_BlockedFailingPersistenceNeverPublishesCandidate()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-admission-visibility-").FullName;
        using var releaseWriter = new ManualResetEventSlim();
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidateId = Guid.Empty;
        var executorCalled = 0;
        try
        {
            void FailingWriter(byte[] bytes)
            {
                using var document = JsonDocument.Parse(bytes);
                candidateId = document.RootElement[0].GetProperty("operation_id").GetGuid();
                writerEntered.TrySetResult();
                if (!releaseWriter.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The admission persistence barrier timed out.");
                throw new IOException("sensitive admission persistence detail");
            }

            await using var coordinator = new OperationCoordinator(FailingWriter, rootDirectory: root);
            var startTask = Task.Run(async () => await coordinator.StartAsync(
                kind: "test.persist-before-publish",
                target: "candidate",
                ownerPrincipal: "owner-a",
                executor: (_, _, _) =>
                {
                    Interlocked.Increment(ref executorCalled);
                    throw new InvalidOperationException("must not execute");
                },
                cancellationToken: CancellationToken.None));

            await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotEqual(Guid.Empty, candidateId);

            var ownerList = await coordinator.ListOperationsAsync(
                new OperationListQuery("owner-a"),
                CancellationToken.None);
            var adminList = await coordinator.ListOperationsAsync(
                new OperationListQuery("*"),
                CancellationToken.None);
            Assert.True(ownerList.IsOk(out var ownerOperations));
            Assert.True(adminList.IsOk(out var adminOperations));
            Assert.Empty(ownerOperations!.Operations);
            Assert.Empty(adminOperations!.Operations);

            var hiddenCandidate = await coordinator.GetOperationAsync(
                new OperationReference(candidateId, "owner-a"),
                CancellationToken.None);
            Assert.True(hiddenCandidate.IsErr(out var hiddenError));
            Assert.Equal("operation.not_found", hiddenError!.Code);

            releaseWriter.Set();
            var failed = await startTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(failed.IsErr(out var persistError));
            Assert.Equal("operation.persist_failed", persistError!.Code);
            Assert.Equal(0, Volatile.Read(ref executorCalled));

            var afterFailure = await coordinator.ListOperationsAsync(
                new OperationListQuery("*"),
                CancellationToken.None);
            Assert.True(afterFailure.IsOk(out var retained));
            Assert.Empty(retained!.Operations);
        }
        finally
        {
            releaseWriter.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("success", OperationStatus.Succeeded, null)]
    [InlineData("domain", OperationStatus.Failed, "operation.domain_failure")]
    [InlineData("throw", OperationStatus.Failed, "operation.failed")]
    public async Task Execution_MapsTerminalOutcomesAndRedactsThrownExceptions(
        string mode,
        OperationStatus expectedStatus,
        string? expectedErrorCode)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-outcome-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var started = await coordinator.StartAsync(
                kind: "test.outcome",
                target: mode,
                ownerPrincipal: "owner-a",
                executor: (_, _, _) => mode switch
                {
                    "success" => Task.FromResult(Result.Ok<string, DaemonError>("result-ref")),
                    "domain" => Task.FromResult(Result.Err<string, DaemonError>(
                        new ValidationDaemonError("operation.domain_failure", "The domain rejected the work."))),
                    "throw" => throw new InvalidOperationException("sensitive executor detail"),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                },
                cancellationToken: CancellationToken.None);

            Assert.True(started.IsOk(out var accepted));
            var completed = await WaitForTerminalAsync(coordinator, accepted!.OperationId, "owner-a");
            Assert.Equal(expectedStatus, completed.Status);
            Assert.Equal(expectedErrorCode, completed.ErrorCode);
            if (mode == "success")
            {
                Assert.Equal("result-ref", completed.ResultReference);
            }
            else if (mode == "throw")
            {
                Assert.Equal("The operation failed due to an internal error.", completed.ErrorMessage);
                Assert.DoesNotContain("sensitive", completed.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("success", OperationStatus.Succeeded)]
    [InlineData("failure", OperationStatus.Failed)]
    [InlineData("cancel", OperationStatus.Cancelled)]
    public async Task TerminalCommit_PrecedesTerminalVisibilityAndFreezesLateCancellation(
        string mode,
        OperationStatus expectedStatus)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-terminal-commit-").FullName;
        try
        {
            var planKernel = new PlanKernel(rootDirectory: Path.Combine(root, "plans"));
            var plan = PutAndBeginReadyPlan(planKernel);
            await using var coordinator = new OperationCoordinator(rootDirectory: Path.Combine(root, "operations"));
            var executorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            OperationStatus? committedOutcome = null;

            var started = await coordinator.StartAsync(
                kind: "provisioning.execute",
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: "owner-terminal",
                executor: async (_, _, ct) =>
                {
                    _ = ct;
                    executorEntered.TrySetResult();
                    await releaseExecutor.Task;
                    return mode == "failure"
                        ? Result.Err<string, DaemonError>(
                            new ValidationDaemonError("operation.domain_failure", "The domain rejected the work."))
                        : Result.Ok<string, DaemonError>("result-ref");
                },
                cancellationToken: CancellationToken.None,
                terminalCommit: completion =>
                {
                    committedOutcome = completion.Status;
                    commitEntered.TrySetResult();
                    if (!releaseCommit.Task.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The terminal commit test barrier timed out.");

                    return planKernel.CompleteAcceptedExecute(plan.PlanId);
                });
            Assert.True(started.IsOk(out var accepted));
            await executorEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            if (mode == "cancel")
            {
                var cancel = await coordinator.CancelOperationAsync(
                    new OperationCancelRequest(accepted!.OperationId, "owner-terminal"),
                    CancellationToken.None);
                Assert.True(cancel.IsOk(out var requested));
                Assert.True(requested!.CancelRequested);
            }

            releaseExecutor.TrySetResult();
            await commitEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var whileCommitBlocked = await GetAsync(coordinator, accepted!.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Running, whileCommitBlocked.Status);
            Assert.False(whileCommitBlocked.Cancellable);
            var executing = planKernel.Get(plan.PlanId);
            Assert.True(executing.IsOk(out var executingPlan));
            Assert.Equal(PlanStatus.Executing, executingPlan!.Status);

            var lateCancel = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-terminal"),
                CancellationToken.None);
            Assert.True(lateCancel.IsOk(out var lateCancelResult));
            Assert.False(lateCancelResult!.CancelRequested);

            releaseCommit.TrySetResult();
            var completed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-terminal");
            Assert.Equal(expectedStatus, completed.Status);
            Assert.Equal(expectedStatus, committedOutcome);
            var consumed = planKernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TerminalCommitException_WithholdsTerminalAndRecoversLinkedPlanOnRestart()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-terminal-commit-failure-").FullName;
        var operationsRoot = Path.Combine(root, "operations");
        var plansRoot = Path.Combine(root, "plans");
        var logger = new RecordingLogger<OperationCoordinator>();
        try
        {
            var planKernel = new PlanKernel(rootDirectory: plansRoot);
            var plan = PutAndBeginReadyPlan(planKernel);
            var coordinator = new OperationCoordinator(rootDirectory: operationsRoot, logger: logger);
            var commitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "provisioning.execute",
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: "owner-terminal",
                executor: static (_, _, _) => Task.FromResult(Result.Ok<string, DaemonError>("result-ref")),
                cancellationToken: CancellationToken.None,
                terminalCommit: _ =>
                {
                    commitEntered.TrySetResult();
                    throw new InvalidOperationException("sensitive terminal commit detail");
                });
            Assert.True(started.IsOk(out var accepted));
            await commitEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await coordinator.DisposeAsync();

            var retained = await GetAsync(coordinator, accepted!.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Running, retained.Status);
            Assert.False(retained.Cancellable);
            var executing = planKernel.Get(plan.PlanId);
            Assert.True(executing.IsOk(out var executingPlan));
            Assert.Equal(PlanStatus.Executing, executingPlan!.Status);
            Assert.Contains(logger.Entries, entry =>
                entry.Exception is InvalidOperationException exception &&
                exception.Message.Contains("sensitive terminal commit detail", StringComparison.Ordinal));

            await using var recoveredOperations = new OperationCoordinator(rootDirectory: operationsRoot);
            var beforeRecovery = await GetAsync(recoveredOperations, accepted.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Running, beforeRecovery.Status);

            var recoveredKernel = new PlanKernel(rootDirectory: plansRoot);
            var startupRecovery = new OperationStartupRecovery(recoveredOperations, recoveredKernel);
            startupRecovery.Recover();
            var interrupted = await GetAsync(recoveredOperations, accepted.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Interrupted, interrupted.Status);

            var recoveredPlan = recoveredKernel.Get(plan.PlanId);
            Assert.True(recoveredPlan.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TerminalPersistenceFailure_DoesNotPublishTerminalSnapshot()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-terminal-persist-failure-").FullName;
        var operationsRoot = Path.Combine(root, "operations");
        var plansRoot = Path.Combine(root, "plans");
        var logger = new RecordingLogger<OperationCoordinator>();
        try
        {
            var planKernel = new PlanKernel(rootDirectory: plansRoot);
            var plan = PutAndBeginReadyPlan(planKernel);
            var coordinator = new OperationCoordinator(rootDirectory: operationsRoot, logger: logger);
            var executorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var terminalCommitCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "provisioning.execute",
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: "owner-terminal",
                executor: async (_, _, _) =>
                {
                    executorEntered.TrySetResult();
                    await releaseExecutor.Task;
                    return Result.Ok<string, DaemonError>("result-ref");
                },
                cancellationToken: CancellationToken.None,
                terminalCommit: _ =>
                {
                    var committed = planKernel.CompleteAcceptedExecute(plan.PlanId);
                    terminalCommitCompleted.TrySetResult();
                    return committed;
                });
            Assert.True(started.IsOk(out var accepted));
            await executorEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var operationTempPath = Path.Combine(operationsRoot, "index.json.tmp");
            Directory.CreateDirectory(operationTempPath);
            releaseExecutor.TrySetResult();
            await terminalCommitCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await coordinator.DisposeAsync();

            var retained = await GetAsync(coordinator, accepted!.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Running, retained.Status);
            Assert.False(retained.Cancellable);
            var consumed = planKernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
            Assert.Contains(logger.Entries, static entry =>
                entry.Exception is IOException or UnauthorizedAccessException);

            using var persisted = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(operationsRoot, "index.json")));
            Assert.Equal(1, persisted.RootElement.GetArrayLength());
            var persistedOperation = persisted.RootElement[0];
            Assert.Equal("Running", persistedOperation.GetProperty("status").GetString());

            Directory.Delete(operationTempPath);
            await using var recoveredOperations = new OperationCoordinator(rootDirectory: operationsRoot);
            var beforeRecovery = await GetAsync(recoveredOperations, accepted.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Running, beforeRecovery.Status);

            var recoveredKernel = new PlanKernel(rootDirectory: plansRoot);
            var startupRecovery = new OperationStartupRecovery(recoveredOperations, recoveredKernel);
            startupRecovery.Recover();
            var interrupted = await GetAsync(recoveredOperations, accepted.OperationId, "owner-terminal");
            Assert.Equal(OperationStatus.Interrupted, interrupted.Status);

            var retainedConsumedPlan = recoveredKernel.Get(plan.PlanId);
            Assert.True(retainedConsumedPlan.IsOk(out var recoveredPlan));
            Assert.Equal(PlanStatus.Consumed, recoveredPlan!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_IsOwnerBoundIdempotentAndWinsAgainstLateSuccess()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-cancel-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.cancel",
                target: null,
                ownerPrincipal: "owner-b",
                executor: async (_, _, _) =>
                {
                    entered.TrySetResult();
                    await release.Task;
                    return Result.Ok<string, DaemonError>("too-late-success");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var forbidden = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted!.OperationId, "owner-a"),
                CancellationToken.None);
            Assert.True(forbidden.IsErr(out _));

            var cancel = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-b"),
                CancellationToken.None);
            Assert.True(cancel.IsOk(out var requested));
            Assert.True(requested!.CancelRequested);

            var duplicate = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-b"),
                CancellationToken.None);
            Assert.True(duplicate.IsOk(out var duplicateResult));
            Assert.False(duplicateResult!.CancelRequested);

            release.TrySetResult();
            var completed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-b");
            Assert.Equal(OperationStatus.Cancelled, completed.Status);
            Assert.Null(completed.ResultReference);

            var late = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-b"),
                CancellationToken.None);
            Assert.True(late.IsOk(out var lateResult));
            Assert.False(lateResult!.CancelRequested);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_BlockedFailingPersistenceWithholdsStateAndSignalAndLeavesOperationRunnable()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-cancel-persist-failure-").FullName;
        var writer = new OperationIndexBarrierWriter(Path.Combine(root, "index.json"));
        var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var coordinator = new OperationCoordinator(writer.Write, rootDirectory: root);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationSignalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.cancel-persist-failure",
                target: null,
                ownerPrincipal: "owner-cancel-failure",
                executor: async (_, _, cancellationToken) =>
                {
                    cancellationToken.Register(cancellationSignalled.SetResult);
                    entered.TrySetResult();
                    await releaseExecutor.Task;
                    return Result.Ok<string, DaemonError>("completed-without-cancellation");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = await GetAsync(coordinator, accepted!.OperationId, "owner-cancel-failure");

            var writerEntered = writer.Arm(
                snapshot => snapshot.OperationId == accepted.OperationId &&
                    snapshot.Status == OperationStatus.Running &&
                    !snapshot.Cancellable,
                fail: true);
            var cancelTask = Task.Run(() => coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-cancel-failure"),
                CancellationToken.None));
            await writerEntered.WaitAsync(TimeSpan.FromSeconds(3));

            var whileBlocked = await GetAsync(coordinator, accepted.OperationId, "owner-cancel-failure");
            var whileBlockedList = await coordinator.ListOperationsAsync(
                new OperationListQuery("owner-cancel-failure"),
                CancellationToken.None);
            Assert.Equal(before, whileBlocked);
            Assert.True(whileBlockedList.IsOk(out var listed));
            Assert.Equal(before, Assert.Single(listed!.Operations));
            Assert.Equal(before, writer.ReadSingle());
            Assert.False(cancellationSignalled.Task.IsCompleted);

            writer.Release();
            var failedCancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(failedCancel.IsErr(out var persistError));
            Assert.Equal("operation.persist_failed", persistError!.Code);

            var afterFailure = await GetAsync(coordinator, accepted.OperationId, "owner-cancel-failure");
            Assert.Equal(before, afterFailure);
            Assert.Equal(before, writer.ReadSingle());
            Assert.True(afterFailure.Cancellable);
            Assert.False(cancellationSignalled.Task.IsCompleted);

            releaseExecutor.TrySetResult();
            var completed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-cancel-failure");
            Assert.Equal(OperationStatus.Succeeded, completed.Status);
            Assert.Equal("completed-without-cancellation", completed.ResultReference);
        }
        finally
        {
            writer.Release();
            releaseExecutor.TrySetResult();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_BlockedPersistenceLinearizesProgressAndCompletionAfterDurablePublication()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-cancel-linearized-").FullName;
        var writer = new OperationIndexBarrierWriter(Path.Combine(root, "index.json"));
        var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var coordinator = new OperationCoordinator(writer.Write, rootDirectory: root);
            var contextSource = new TaskCompletionSource<IOperationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationSignalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.cancel-linearized",
                target: null,
                ownerPrincipal: "owner-cancel-linearized",
                executor: async (_, context, cancellationToken) =>
                {
                    cancellationToken.Register(cancellationSignalled.SetResult);
                    contextSource.TrySetResult(context);
                    await releaseExecutor.Task;
                    return Result.Ok<string, DaemonError>("late-success");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            var context = await contextSource.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = await GetAsync(coordinator, accepted!.OperationId, "owner-cancel-linearized");

            var writerEntered = writer.Arm(
                snapshot => snapshot.OperationId == accepted.OperationId &&
                    snapshot.Status == OperationStatus.Running &&
                    !snapshot.Cancellable,
                fail: false);
            var cancelTask = Task.Run(() => coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted.OperationId, "owner-cancel-linearized"),
                CancellationToken.None));
            await writerEntered.WaitAsync(TimeSpan.FromSeconds(3));

            var progressTask = Task.Run(() => context.ReportProgress(
                new OperationProgress(false, 1, 2, "steps", null, null, null)));
            releaseExecutor.TrySetResult();
            await Task.Delay(50);

            var whileBlocked = await GetAsync(coordinator, accepted.OperationId, "owner-cancel-linearized");
            Assert.Equal(before, whileBlocked);
            Assert.Equal(before, writer.ReadSingle());
            Assert.False(progressTask.IsCompleted);
            Assert.False(cancellationSignalled.Task.IsCompleted);

            writer.Release();
            var cancelled = await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(cancelled.IsOk(out var cancelResult));
            Assert.True(cancelResult!.CancelRequested);
            await cancellationSignalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await progressTask.WaitAsync(TimeSpan.FromSeconds(3));

            var terminal = await WaitForTerminalAsync(
                coordinator,
                accepted.OperationId,
                "owner-cancel-linearized");
            Assert.Equal(OperationStatus.Cancelled, terminal.Status);
            Assert.Null(terminal.ResultReference);
            Assert.True(terminal.Version > before.Version);
            Assert.Equal(terminal, writer.ReadSingle());
        }
        finally
        {
            writer.Release();
            releaseExecutor.TrySetResult();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_QueuedPersistenceSerializesExecutorStartWithoutStatusOrVersionRegression()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-cancel-queued-").FullName;
        var writer = new OperationIndexBarrierWriter(Path.Combine(root, "index.json"));
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = new BlockingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var executorCalled = 0;
        try
        {
            await using var coordinator = new OperationCoordinator(
                writer.Write,
                rootDirectory: root,
                scopeFactory: scopeFactory);
            var started = await coordinator.StartAsync(
                kind: "test.cancel-queued",
                target: null,
                ownerPrincipal: "owner-cancel-queued",
                executor: (_, _, _) =>
                {
                    Interlocked.Increment(ref executorCalled);
                    return Task.FromResult(Result.Ok<string, DaemonError>("must-not-run"));
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await scopeFactory.ScopeRequested.WaitAsync(TimeSpan.FromSeconds(3));

            var writerEntered = writer.Arm(
                snapshot => snapshot.OperationId == accepted!.OperationId &&
                    snapshot.Status == OperationStatus.Queued &&
                    !snapshot.Cancellable,
                fail: false);
            var cancelTask = Task.Run(() => coordinator.CancelOperationAsync(
                new OperationCancelRequest(accepted!.OperationId, "owner-cancel-queued"),
                CancellationToken.None));
            await writerEntered.WaitAsync(TimeSpan.FromSeconds(3));
            scopeFactory.Release();
            await Task.Delay(50);

            var whileBlocked = await GetAsync(coordinator, accepted.OperationId, "owner-cancel-queued");
            Assert.Equal(OperationStatus.Queued, whileBlocked.Status);
            Assert.Equal(accepted.Version, whileBlocked.Version);
            Assert.True(whileBlocked.Cancellable);
            Assert.Equal(whileBlocked, writer.ReadSingle());
            Assert.Equal(0, Volatile.Read(ref executorCalled));

            writer.Release();
            var cancelled = await cancelTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(cancelled.IsOk(out var cancelResult));
            Assert.True(cancelResult!.CancelRequested);

            var terminal = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-cancel-queued");
            Assert.Equal(OperationStatus.Cancelled, terminal.Status);
            Assert.Equal(accepted.Version + 2, terminal.Version);
            Assert.Equal(0, Volatile.Read(ref executorCalled));
            Assert.Equal(terminal, writer.ReadSingle());
        }
        finally
        {
            writer.Release();
            scopeFactory.Release();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningTransition_BlockedPersistencePublishesOnlyAfterSuccess(bool fail)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-running-durable-").FullName;
        var writer = new OperationIndexBarrierWriter(Path.Combine(root, "index.json"));
        var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var writerEntered = writer.Arm(
                snapshot => snapshot.Status == OperationStatus.Running,
                fail);
            await using var coordinator = new OperationCoordinator(writer.Write, rootDirectory: root);
            var executorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.running-durable",
                target: null,
                ownerPrincipal: "owner-running",
                executor: async (_, _, _) =>
                {
                    executorEntered.TrySetResult();
                    await releaseExecutor.Task;
                    return Result.Ok<string, DaemonError>("done");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await writerEntered.WaitAsync(TimeSpan.FromSeconds(3));

            var whileBlocked = await GetAsync(coordinator, accepted!.OperationId, "owner-running");
            var whileBlockedList = await coordinator.ListOperationsAsync(
                new OperationListQuery("owner-running"),
                CancellationToken.None);
            Assert.Equal(accepted, whileBlocked);
            Assert.True(whileBlockedList.IsOk(out var listed));
            Assert.Equal(accepted, Assert.Single(listed!.Operations));
            Assert.Equal(accepted, writer.ReadSingle());
            Assert.False(executorEntered.Task.IsCompleted);

            writer.Release();
            if (fail)
            {
                var failed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-running");
                Assert.Equal(OperationStatus.Failed, failed.Status);
                Assert.Equal("operation.failed", failed.ErrorCode);
                Assert.False(executorEntered.Task.IsCompleted);
                Assert.Equal(failed, writer.ReadSingle());
            }
            else
            {
                await executorEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var running = await GetAsync(coordinator, accepted.OperationId, "owner-running");
                Assert.Equal(OperationStatus.Running, running.Status);
                Assert.Equal(accepted.Version + 1, running.Version);
                Assert.Equal(running, writer.ReadSingle());

                releaseExecutor.TrySetResult();
                Assert.Equal(
                    OperationStatus.Succeeded,
                    (await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-running")).Status);
            }
        }
        finally
        {
            writer.Release();
            releaseExecutor.TrySetResult();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageTransition_BlockedPersistencePublishesOnlyAfterSuccess(bool fail)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-stage-durable-").FullName;
        var writer = new OperationIndexBarrierWriter(Path.Combine(root, "index.json"));
        var releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var coordinator = new OperationCoordinator(writer.Write, rootDirectory: root);
            var contextSource = new TaskCompletionSource<IOperationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.stage-durable",
                target: null,
                ownerPrincipal: "owner-stage",
                executor: async (_, context, _) =>
                {
                    contextSource.TrySetResult(context);
                    await releaseExecutor.Task;
                    return Result.Ok<string, DaemonError>("done");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            var context = await contextSource.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before = await GetAsync(coordinator, accepted!.OperationId, "owner-stage");
            Assert.Equal(OperationStage.Resolving, before.Stage);

            var writerEntered = writer.Arm(
                snapshot => snapshot.OperationId == accepted.OperationId &&
                    snapshot.Stage == OperationStage.Downloading,
                fail);
            var stageTask = Task.Run(() => context.SetStage(OperationStage.Downloading));
            await writerEntered.WaitAsync(TimeSpan.FromSeconds(3));

            var whileBlocked = await GetAsync(coordinator, accepted.OperationId, "owner-stage");
            var whileBlockedList = await coordinator.ListOperationsAsync(
                new OperationListQuery("owner-stage"),
                CancellationToken.None);
            Assert.Equal(before, whileBlocked);
            Assert.True(whileBlockedList.IsOk(out var listed));
            Assert.Equal(before, Assert.Single(listed!.Operations));
            Assert.Equal(before, writer.ReadSingle());

            writer.Release();
            if (fail)
            {
                await Assert.ThrowsAsync<IOException>(async () =>
                    await stageTask.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(before, await GetAsync(coordinator, accepted.OperationId, "owner-stage"));
                Assert.Equal(before, writer.ReadSingle());
            }
            else
            {
                await stageTask.WaitAsync(TimeSpan.FromSeconds(3));
                var published = await GetAsync(coordinator, accepted.OperationId, "owner-stage");
                Assert.Equal(OperationStage.Downloading, published.Stage);
                Assert.Equal(before.Version + 1, published.Version);
                Assert.Equal(published, writer.ReadSingle());
            }

            releaseExecutor.TrySetResult();
            Assert.Equal(
                OperationStatus.Succeeded,
                (await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-stage")).Status);
        }
        finally
        {
            writer.Release();
            releaseExecutor.TrySetResult();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Execution_CreatesAndDisposesOneAsyncScopePerOperationWithoutContextBleed()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-scopes-").FullName;
        var counters = new ScopeCounters();
        var services = new ServiceCollection();
        services.AddSingleton(counters);
        services.AddScoped<ScopeProbe>();
        await using var provider = services.BuildServiceProvider();
        try
        {
            await using var coordinator = new OperationCoordinator(
                rootDirectory: root,
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>());

            var terminalIds = new List<Guid>();
            foreach (var mode in new[] { "success", "domain", "throw" })
            {
                var started = await coordinator.StartAsync(
                    kind: "test.scope",
                    target: mode,
                    ownerPrincipal: "owner-scope",
                    executor: (scope, _, _) =>
                    {
                        _ = scope.GetRequiredService<ScopeProbe>();
                        return mode switch
                        {
                            "success" => Task.FromResult(Result.Ok<string, DaemonError>("ok")),
                            "domain" => Task.FromResult(Result.Err<string, DaemonError>(
                                new ValidationDaemonError("scope.domain", "domain failure"))),
                            "throw" => throw new InvalidOperationException("scope failure"),
                            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                        };
                    },
                    cancellationToken: CancellationToken.None);
                Assert.True(started.IsOk(out var accepted));
                terminalIds.Add(accepted!.OperationId);
            }

            var cancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelStarted = await coordinator.StartAsync(
                kind: "test.scope",
                target: "cancel",
                ownerPrincipal: "owner-scope",
                executor: async (scope, context, ct) =>
                {
                    _ = context;
                    _ = scope.GetRequiredService<ScopeProbe>();
                    cancelEntered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return Result.Ok<string, DaemonError>("unused");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(cancelStarted.IsOk(out var cancelAccepted));
            terminalIds.Add(cancelAccepted!.OperationId);
            await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var cancel = await coordinator.CancelOperationAsync(
                new OperationCancelRequest(cancelAccepted.OperationId, "owner-scope"),
                CancellationToken.None);
            Assert.True(cancel.IsOk(out var cancelResult));
            Assert.True(cancelResult!.CancelRequested);

            foreach (var operationId in terminalIds)
                _ = await WaitForTerminalAsync(coordinator, operationId, "owner-scope");

            Assert.Equal(4, counters.Created);
            Assert.Equal(4, counters.Disposed);
            Assert.Equal(4, counters.InstanceIds.Distinct().Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Progress_ConcurrentReportsKeepMonotonicVersionsAndCoalescePersistence()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-progress-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
        try
        {
            await using var coordinator = new OperationCoordinator(timeProvider: time, rootDirectory: root);
            var contextSource = new TaskCompletionSource<IOperationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.progress",
                target: null,
                ownerPrincipal: "owner-progress",
                executor: async (_, context, ct) =>
                {
                    contextSource.TrySetResult(context);
                    await release.Task.WaitAsync(ct);
                    return Result.Ok<string, DaemonError>("done");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            var context = await contextSource.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var writesBeforeBurst = coordinator.PersistenceWriteCount;
            using var stopReaders = new CancellationTokenSource();
            var readersStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startedReaders = 0;
            var readerObservations = 0;
            var readers = Enumerable.Range(0, 4).Select(async _ =>
            {
                var previousVersion = accepted!.Version;
                if (Interlocked.Increment(ref startedReaders) == 4)
                    readersStarted.TrySetResult();

                while (!stopReaders.IsCancellationRequested)
                {
                    var observed = await GetAsync(coordinator, accepted.OperationId, "owner-progress");
                    Assert.True(
                        observed.Version >= previousVersion,
                        $"Operation version regressed from {previousVersion} to {observed.Version}.");
                    previousVersion = observed.Version;
                    Interlocked.Increment(ref readerObservations);
                    await Task.Yield();
                }
            }).ToArray();
            await readersStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Parallel.For(0, 10_000, index => context.ReportProgress(
                new OperationProgress(false, index, 10_000, "callbacks", index, 10_000, null)));

            stopReaders.Cancel();
            await Task.WhenAll(readers);
            Assert.True(Volatile.Read(ref readerObservations) >= 4);

            var current = await GetAsync(coordinator, accepted!.OperationId, "owner-progress");
            Assert.True(current.Version >= 10_002);
            Assert.Equal(10_000, current.Progress.Total);
            Assert.InRange(current.Progress.Completed!.Value, 0, 9_999);
            Assert.Equal(writesBeforeBurst, coordinator.PersistenceWriteCount);

            time.Advance(TimeSpan.FromMilliseconds(200));
            context.ReportProgress(new OperationProgress(false, 10_000, 10_000, "callbacks", 10_000, 10_000, null));
            Assert.Equal(writesBeforeBurst + 1, coordinator.PersistenceWriteCount);

            release.TrySetResult();
            var completed = await WaitForTerminalAsync(coordinator, accepted.OperationId, "owner-progress");
            Assert.Equal(OperationStatus.Succeeded, completed.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WeightedChildren_AggregateIntoParentProgress()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-weight-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            OperationSnapshot? observed = null;
            var result = await coordinator.StartAsync(
                kind: "test.weight",
                target: null,
                ownerPrincipal: "owner-c",
                executor: async (_, context, ct) =>
                {
                    var a = context.CreateChild("a", 1);
                    var b = context.CreateChild("b", 3);
                    a.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
                    var nested = b.CreateChild("nested", 1);
                    nested.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
                    observed = await GetAsync(coordinator, context.OperationId, "owner-c");
                    return Result.Ok<string, DaemonError>("done");
                },
                cancellationToken: CancellationToken.None);

            Assert.True(result.IsOk(out var accepted));
            _ = await WaitForTerminalAsync(coordinator, accepted!.OperationId, "owner-c");
            Assert.NotNull(observed);
            Assert.False(observed!.Progress.Indeterminate);
            Assert.Equal(1, observed.Progress.Total);
            Assert.Equal(1d, observed.Progress.Completed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MainTokenAdminPrincipal_SeesAllOperations()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-admin-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var owned = await coordinator.StartAsync(
                kind: "test.work",
                target: "t1",
                ownerPrincipal: "owner-a",
                executor: static (_, _, _) => Task.FromResult(Result.Ok<string, DaemonError>("ok")),
                cancellationToken: CancellationToken.None);
            Assert.True(owned.IsOk(out var accepted));

            var list = await coordinator.ListOperationsAsync(new OperationListQuery("*"), CancellationToken.None);
            Assert.True(list.IsOk(out var listed));
            Assert.Contains(listed!.Operations, item => item.OperationId == accepted!.OperationId);

            var get = await coordinator.GetOperationAsync(
                new OperationReference(accepted!.OperationId, "*"),
                CancellationToken.None);
            Assert.True(get.IsOk(out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void LoadIndex_InvalidExistingIndexFailsClosed(string content)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-corrupt-index-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.json"), content);

            Assert.Throws<InvalidDataException>(() => new OperationCoordinator(rootDirectory: root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("succeeded-missing-result")]
    [InlineData("succeeded-error")]
    [InlineData("failed-missing-code")]
    [InlineData("failed-missing-message")]
    [InlineData("failed-result")]
    [InlineData("cancelled-wrong-code")]
    [InlineData("cancelled-missing-message")]
    [InlineData("cancelled-result")]
    [InlineData("interrupted-wrong-code")]
    [InlineData("interrupted-missing-message")]
    [InlineData("interrupted-result")]
    [InlineData("completed-after-updated")]
    [InlineData("terminal-cancellable")]
    [InlineData("queued-running-stage")]
    [InlineData("running-queued-stage")]
    [InlineData("nonterminal-error")]
    [InlineData("nonterminal-result")]
    public void LoadIndex_SemanticallyInvalidExistingIndexFailsClosed(string corruption)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-corrupt-state-").FullName;
        try
        {
            var snapshot = CreateSemanticallyInvalidSnapshot(corruption);
            File.WriteAllBytes(
                Path.Combine(root, "index.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new[] { snapshot },
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray));

            Assert.Throws<InvalidDataException>(() => new OperationCoordinator(rootDirectory: root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadIndex_ConfiguredRetentionPrunesByAgeAndExactByteCapBeforeVisibility()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-startup-retention-").FullName;
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        var time = new ManualTimeProvider(now);
        try
        {
            var expired = CreateTerminalSnapshot(
                Guid.Parse("33333333-3333-3333-3333-333333333331"),
                now - TimeSpan.FromDays(2));
            var capPruned = CreateTerminalSnapshot(
                Guid.Parse("33333333-3333-3333-3333-333333333332"),
                now - TimeSpan.FromHours(2));
            var retained = CreateTerminalSnapshot(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                now - TimeSpan.FromHours(1));
            var retainedBytes = JsonSerializer.SerializeToUtf8Bytes(
                new[] { retained },
                OperationPersistenceJsonContext.Default.OperationSnapshotArray);
            var config = new DaemonOperationsConfig
            {
                RetentionDays = 1,
                MaximumBytes = retainedBytes.LongLength,
            };
            File.WriteAllBytes(
                Path.Combine(root, "index.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new[] { expired, capPruned, retained },
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray));

            await using var coordinator = new OperationCoordinator(
                timeProvider: time,
                rootDirectory: root,
                config: config);

            var expiredResult = await coordinator.GetOperationAsync(
                new OperationReference(expired.OperationId, "owner-retention"),
                CancellationToken.None);
            var capPrunedResult = await coordinator.GetOperationAsync(
                new OperationReference(capPruned.OperationId, "owner-retention"),
                CancellationToken.None);
            var retainedResult = await coordinator.GetOperationAsync(
                new OperationReference(retained.OperationId, "owner-retention"),
                CancellationToken.None);
            Assert.True(expiredResult.IsErr(out _));
            Assert.True(capPrunedResult.IsErr(out _));
            Assert.True(retainedResult.IsOk(out _));
            Assert.InRange(new FileInfo(Path.Combine(root, "index.json")).Length, 2, config.MaximumBytes);
            Assert.Equal(1, coordinator.PersistenceWriteCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_ConfiguredByteCapConvergesOversizedNonTerminalIndex()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-recovery-retention-").FullName;
        var operationsRoot = Path.Combine(root, "operations");
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        var time = new ManualTimeProvider(now);
        try
        {
            Directory.CreateDirectory(operationsRoot);
            var first = CreateRunningSnapshot(
                Guid.Parse("44444444-4444-4444-4444-444444444441"),
                now - TimeSpan.FromMinutes(2));
            var second = CreateRunningSnapshot(
                Guid.Parse("44444444-4444-4444-4444-444444444442"),
                now - TimeSpan.FromMinutes(1));
            var retainedInterrupted = ToStartupInterrupted(second, now);
            var retainedBytes = JsonSerializer.SerializeToUtf8Bytes(
                new[] { retainedInterrupted },
                OperationPersistenceJsonContext.Default.OperationSnapshotArray);
            var config = new DaemonOperationsConfig
            {
                RetentionDays = 7,
                MaximumBytes = retainedBytes.LongLength,
            };
            var indexPath = Path.Combine(operationsRoot, "index.json");
            File.WriteAllBytes(
                indexPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    new[] { first, second },
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray));

            await using var coordinator = new OperationCoordinator(
                timeProvider: time,
                rootDirectory: operationsRoot,
                config: config);
            Assert.True(new FileInfo(indexPath).Length > config.MaximumBytes);
            Assert.Equal(OperationStatus.Running, (await GetAsync(
                coordinator, first.OperationId, "owner-retention")).Status);
            Assert.Equal(OperationStatus.Running, (await GetAsync(
                coordinator, second.OperationId, "owner-retention")).Status);

            var plans = new PlanKernel(rootDirectory: Path.Combine(root, "plans"));
            new OperationStartupRecovery(coordinator, plans).Recover();

            var pruned = await coordinator.GetOperationAsync(
                new OperationReference(first.OperationId, "owner-retention"),
                CancellationToken.None);
            Assert.True(pruned.IsErr(out _));
            var interrupted = await GetAsync(coordinator, second.OperationId, "owner-retention");
            Assert.Equal(OperationStatus.Interrupted, interrupted.Status);
            Assert.InRange(new FileInfo(indexPath).Length, 2, config.MaximumBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_BatchesQueuedAndRunningAsInterruptedAfterExplicitHandshake()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-recover-").FullName;
        try
        {
            var runningId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var queuedId = Guid.Parse("11111111-1111-1111-1111-222222222222");
            var synthetic = """
                [
                  {"operation_id":"11111111-1111-1111-1111-111111111111","kind":"test.recover","target":null,"owner_principal":"owner-d","status":"running","stage":"installing","progress":{"indeterminate":true,"completed":null,"total":null,"unit":null,"bytes_transferred":null,"bytes_total":null,"rate":null},"version":2,"created_at":"2026-01-01T00:00:00+00:00","updated_at":"2026-01-01T00:00:01+00:00","completed_at":null,"cancellable":true,"error_code":null,"error_message":null,"result_reference":null},
                  {"operation_id":"11111111-1111-1111-1111-222222222222","kind":"test.recover","target":null,"owner_principal":"owner-d","status":"queued","stage":"queued","progress":{"indeterminate":true,"completed":null,"total":null,"unit":null,"bytes_transferred":null,"bytes_total":null,"rate":null},"version":1,"created_at":"2026-01-01T00:00:02+00:00","updated_at":"2026-01-01T00:00:02+00:00","completed_at":null,"cancellable":true,"error_code":null,"error_message":null,"result_reference":null}
                ]
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "index.json"), synthetic);

            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var runningBeforeRecovery = await GetAsync(coordinator, runningId, "owner-d");
            var queuedBeforeRecovery = await GetAsync(coordinator, queuedId, "owner-d");
            Assert.Equal(OperationStatus.Running, runningBeforeRecovery.Status);
            Assert.Equal(OperationStatus.Queued, queuedBeforeRecovery.Status);
            var writesBeforeRecovery = coordinator.PersistenceWriteCount;

            var emptyPlans = new PlanKernel(rootDirectory: Path.Combine(root, "plans"));
            new OperationStartupRecovery(coordinator, emptyPlans).Recover();
            Assert.Equal(writesBeforeRecovery + 1, coordinator.PersistenceWriteCount);
            var recoveredRunning = await GetAsync(coordinator, runningId, "owner-d");
            var recoveredQueued = await GetAsync(coordinator, queuedId, "owner-d");
            Assert.Equal(OperationStatus.Interrupted, recoveredRunning.Status);
            Assert.Equal(OperationStatus.Interrupted, recoveredQueued.Status);
            Assert.False(recoveredRunning.Cancellable);
            Assert.False(recoveredQueued.Cancellable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_CancelsAndPersistsRunningOperations()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-shutdown-").FullName;
        try
        {
            var coordinator = new OperationCoordinator(rootDirectory: root);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = await coordinator.StartAsync(
                kind: "test.shutdown",
                target: null,
                ownerPrincipal: "owner-shutdown",
                executor: async (_, _, ct) =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return Result.Ok<string, DaemonError>("unused");
                },
                cancellationToken: CancellationToken.None);
            Assert.True(started.IsOk(out var accepted));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await coordinator.DisposeAsync();

            await using var reloaded = new OperationCoordinator(rootDirectory: root);
            var emptyPlans = new PlanKernel(rootDirectory: Path.Combine(root, "plans"));
            new OperationStartupRecovery(reloaded, emptyPlans).Recover();
            var completed = await GetAsync(reloaded, accepted!.OperationId, "owner-shutdown");
            Assert.Equal(OperationStatus.Cancelled, completed.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NoOpContext_DoesNotThrow()
    {
        var context = NoOpOperationContext.Instance;
        context.SetStage(OperationStage.Downloading);
        context.ReportProgress(new OperationProgress(true, null, null, null, null, null, null));
        Assert.Same(context, context.CreateChild("x", 1));
        Assert.Equal(Guid.Empty, context.OperationId);
    }

    private static OperationSnapshot CreateTerminalSnapshot(Guid operationId, DateTimeOffset completedAt) =>
        new(
            operationId,
            "test.retention",
            "retention-target",
            "owner-retention",
            OperationStatus.Succeeded,
            OperationStage.Succeeded,
            new OperationProgress(false, 1, 1, "steps", null, null, null),
            3,
            completedAt - TimeSpan.FromMinutes(1),
            completedAt,
            completedAt,
            false,
            null,
            null,
            new string('r', 64));

    private static OperationSnapshot CreateRunningSnapshot(Guid operationId, DateTimeOffset createdAt) =>
        new(
            operationId,
            "test.retention-recovery",
            "retention-target",
            "owner-retention",
            OperationStatus.Running,
            OperationStage.Installing,
            new OperationProgress(true, null, null, "steps", null, null, null),
            2,
            createdAt,
            createdAt + TimeSpan.FromSeconds(1),
            null,
            true,
            null,
            null,
            null);

    private static OperationSnapshot ToStartupInterrupted(OperationSnapshot snapshot, DateTimeOffset now) =>
        snapshot with
        {
            Status = OperationStatus.Interrupted,
            Stage = OperationStage.Interrupted,
            CompletedAt = now,
            Cancellable = false,
            ErrorCode = "operation.interrupted",
            ErrorMessage = "The operation was interrupted by daemon restart.",
            ResultReference = null,
            Version = snapshot.Version + 1,
            UpdatedAt = now,
        };

    private static OperationSnapshot CreateSemanticallyInvalidSnapshot(string corruption)
    {
        var now = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        var succeeded = CreateTerminalSnapshot(Guid.NewGuid(), now);
        var failed = succeeded with
        {
            Status = OperationStatus.Failed,
            Stage = OperationStage.Failed,
            ErrorCode = "operation.failed",
            ErrorMessage = "The operation failed.",
            ResultReference = null,
        };
        var cancelled = failed with
        {
            Status = OperationStatus.Cancelled,
            Stage = OperationStage.Cancelled,
            ErrorCode = "operation.cancelled",
            ErrorMessage = "The operation was cancelled.",
        };
        var interrupted = cancelled with
        {
            Status = OperationStatus.Interrupted,
            Stage = OperationStage.Interrupted,
            ErrorCode = "operation.interrupted",
            ErrorMessage = "The operation was interrupted.",
        };
        var running = CreateRunningSnapshot(Guid.NewGuid(), now - TimeSpan.FromMinutes(1));
        var queued = running with
        {
            Status = OperationStatus.Queued,
            Stage = OperationStage.Queued,
            Version = 1,
            UpdatedAt = running.CreatedAt,
        };

        return corruption switch
        {
            "succeeded-missing-result" => succeeded with { ResultReference = null },
            "succeeded-error" => succeeded with { ErrorCode = "operation.failed", ErrorMessage = "Unexpected error." },
            "failed-missing-code" => failed with { ErrorCode = null },
            "failed-missing-message" => failed with { ErrorMessage = null },
            "failed-result" => failed with { ResultReference = "unexpected" },
            "cancelled-wrong-code" => cancelled with { ErrorCode = "operation.failed" },
            "cancelled-missing-message" => cancelled with { ErrorMessage = null },
            "cancelled-result" => cancelled with { ResultReference = "unexpected" },
            "interrupted-wrong-code" => interrupted with { ErrorCode = "operation.failed" },
            "interrupted-missing-message" => interrupted with { ErrorMessage = null },
            "interrupted-result" => interrupted with { ResultReference = "unexpected" },
            "completed-after-updated" => succeeded with { UpdatedAt = succeeded.CompletedAt!.Value - TimeSpan.FromSeconds(1) },
            "terminal-cancellable" => succeeded with { Cancellable = true },
            "queued-running-stage" => queued with { Stage = OperationStage.Resolving },
            "running-queued-stage" => running with { Stage = OperationStage.Queued },
            "nonterminal-error" => running with { ErrorCode = "operation.failed", ErrorMessage = "Unexpected error." },
            "nonterminal-result" => running with { ResultReference = "unexpected" },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null),
        };
    }

    private static ProvisioningPlanSnapshot PutAndBeginReadyPlan(PlanKernel kernel)
    {
        var put = kernel.Put(
            kind: "provisioning.instance",
            riskClass: PlanRiskClass.Routine,
            requiredPermissions: ["mcsl.provisioning.execute"],
            requiresConfirmation: false,
            creatorPrincipal: "owner-terminal",
            unresolved: System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>.Empty,
            idempotencyKey: null,
            expiry: TimeSpan.FromMinutes(15),
            materialize: (planId, planHash, createdAt, expiresAt, payload) => new ProvisioningPlanSnapshot(
                planId, planHash, "provisioning.instance", PlanStatus.Ready, PlanRiskClass.Routine,
                ["mcsl.provisioning.execute"], false, "owner-terminal", createdAt, expiresAt,
                System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>.Empty,
                ProvisioningProviderKind.Vanilla, "demo", "1.21", "server.jar",
                InstanceFactoryMirror.None, "java", null, payload));
        Assert.True(put.IsOk(out var plan));
        var begin = kernel.TryBeginExecute(plan!.PlanId, "owner-terminal");
        Assert.True(begin.IsOk(out _));
        return plan;
    }

    private static async Task<OperationSnapshot> GetAsync(
        OperationCoordinator coordinator,
        Guid operationId,
        string owner)
    {
        var result = await coordinator.GetOperationAsync(
            new OperationReference(operationId, owner),
            CancellationToken.None);
        Assert.True(result.IsOk(out var snapshot));
        return snapshot!;
    }

    private static async Task<OperationSnapshot> WaitForTerminalAsync(
        OperationCoordinator coordinator,
        Guid operationId,
        string owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = await GetAsync(coordinator, operationId, owner);
            if (snapshot.Status is OperationStatus.Succeeded
                or OperationStatus.Failed
                or OperationStatus.Cancelled
                or OperationStatus.Interrupted)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ScopeCounters
    {
        internal int Created;
        internal int Disposed;
        internal ConcurrentBag<Guid> InstanceIds { get; } = [];
    }

    private sealed class ScopeProbe : IAsyncDisposable
    {
        private readonly ScopeCounters _counters;
        private int _disposed;

        public ScopeProbe(ScopeCounters counters)
        {
            _counters = counters;
            Interlocked.Increment(ref counters.Created);
            counters.InstanceIds.Add(Guid.NewGuid());
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Increment(ref _counters.Disposed);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class OperationIndexBarrierWriter(string indexPath)
    {
        private readonly object _gate = new();
        private Func<OperationSnapshot, bool>? _predicate;
        private TaskCompletionSource? _entered;
        private ManualResetEventSlim? _release;
        private bool _fail;

        internal Task Arm(Func<OperationSnapshot, bool> predicate, bool fail)
        {
            lock (_gate)
            {
                Assert.Null(_predicate);
                _predicate = predicate;
                _fail = fail;
                _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _release = new ManualResetEventSlim();
                return _entered.Task;
            }
        }

        internal void Write(byte[] bytes)
        {
            var snapshots = JsonSerializer.Deserialize(
                    bytes,
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray)
                ?? throw new InvalidDataException("The test writer received a null operation index.");
            TaskCompletionSource? entered = null;
            ManualResetEventSlim? release = null;
            var fail = false;
            lock (_gate)
            {
                if (_predicate is not null && snapshots.Any(_predicate))
                {
                    _predicate = null;
                    entered = _entered;
                    release = _release;
                    fail = _fail;
                }
            }

            if (entered is not null)
            {
                entered.TrySetResult();
                if (!release!.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The operation index writer barrier timed out.");
                if (fail)
                    throw new IOException("Injected operation index persistence failure.");
            }

            File.WriteAllBytes(indexPath, bytes);
        }

        internal OperationSnapshot ReadSingle()
        {
            var snapshots = JsonSerializer.Deserialize(
                    File.ReadAllBytes(indexPath),
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray)
                ?? throw new InvalidDataException("The persisted test operation index was null.");
            return Assert.Single(snapshots);
        }

        internal void Release()
        {
            lock (_gate)
                _release?.Set();
        }
    }

    private sealed class BlockingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly TaskCompletionSource _scopeRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task ScopeRequested => _scopeRequested.Task;

        public IServiceScope CreateScope()
        {
            _scopeRequested.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The operation scope barrier timed out.");
            return inner.CreateScope();
        }

        internal void Release() => _release.Set();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();

        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
                Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
