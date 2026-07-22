using System.Collections.Concurrent;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using Microsoft.Extensions.DependencyInjection;
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

            Parallel.For(0, 10_000, index => context.ReportProgress(
                new OperationProgress(false, index, 10_000, "callbacks", index, 10_000, null)));

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
                    return Result.Ok<string, DaemonError>(string.Empty);
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

    [Fact]
    public async Task Recovery_MarksNonTerminalAsInterrupted()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-recover-").FullName;
        try
        {
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var synthetic = """
                [{"operation_id":"11111111-1111-1111-1111-111111111111","kind":"test.recover","target":null,"owner_principal":"owner-d","status":"running","stage":"installing","progress":{"indeterminate":true,"completed":null,"total":null,"unit":null,"bytes_transferred":null,"bytes_total":null,"rate":null},"version":2,"created_at":"2026-01-01T00:00:00+00:00","updated_at":"2026-01-01T00:00:01+00:00","completed_at":null,"cancellable":true,"error_code":null,"error_message":null,"result_reference":null}]
                """;
            await File.WriteAllTextAsync(Path.Combine(root, "index.json"), synthetic);

            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var recovered = await GetAsync(coordinator, id, "owner-d");
            Assert.Equal(OperationStatus.Interrupted, recovered.Status);
            Assert.False(recovered.Cancellable);
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
}
