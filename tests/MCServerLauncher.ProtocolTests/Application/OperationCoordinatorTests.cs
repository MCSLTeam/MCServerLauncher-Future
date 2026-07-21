using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class OperationCoordinatorTests
{
    [Fact]
    public async Task Execute_SucceedsAndIsQueryable()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var result = await coordinator.ExecuteAsync(
                kind: "test.work",
                target: "t1",
                ownerPrincipal: "owner-a",
                executor: async (context, ct) =>
                {
                    context.SetStage(OperationStage.Downloading);
                    context.ReportProgress(new OperationProgress(false, 1, 2, "items", null, null, null));
                    await Task.Delay(10, ct);
                    return Result.Ok<string, DaemonError>("result-ref");
                },
                cancellationToken: CancellationToken.None);

            Assert.True(result.IsOk(out var snapshot));
            Assert.Equal(OperationStatus.Succeeded, snapshot!.Status);
            Assert.Equal("result-ref", snapshot.ResultReference);

            var get = await coordinator.GetOperationAsync(new OperationReference(snapshot.OperationId, "owner-a"), CancellationToken.None);
            Assert.True(get.IsOk(out var loaded));
            Assert.Equal(snapshot.OperationId, loaded!.OperationId);

            var list = await coordinator.ListOperationsAsync(new OperationListQuery("owner-a"), CancellationToken.None);
            Assert.True(list.IsOk(out var listed));
            Assert.Contains(listed!.Operations, item => item.OperationId == snapshot.OperationId);

            var denied = await coordinator.GetOperationAsync(new OperationReference(snapshot.OperationId, "owner-b"), CancellationToken.None);
            Assert.True(denied.IsErr(out _));

            var emptyList = await coordinator.ListOperationsAsync(new OperationListQuery(null), CancellationToken.None);
            Assert.True(emptyList.IsOk(out var empty));
            Assert.Empty(empty!.Operations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancel_CooperativelyStopsRunningWork()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-ops-cancel-").FullName;
        try
        {
            await using var coordinator = new OperationCoordinator(rootDirectory: root);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executeTask = coordinator.ExecuteAsync(
                kind: "test.cancel",
                target: null,
                ownerPrincipal: "owner-b",
                executor: async (context, ct) =>
                {
                    context.SetStage(OperationStage.Installing);
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return Result.Ok<string, DaemonError>(string.Empty);
                },
                cancellationToken: CancellationToken.None);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var list = await coordinator.ListOperationsAsync(new OperationListQuery("owner-b"), CancellationToken.None);
            Assert.True(list.IsOk(out var listed));
            var id = Assert.Single(listed!.Operations).OperationId;

            var forbidden = await coordinator.CancelOperationAsync(new OperationCancelRequest(id, "owner-a"), CancellationToken.None);
            Assert.True(forbidden.IsErr(out _));

            var cancel = await coordinator.CancelOperationAsync(new OperationCancelRequest(id, "owner-b"), CancellationToken.None);
            Assert.True(cancel.IsOk(out var cancelResult));
            Assert.True(cancelResult!.CancelRequested);

            var executed = await executeTask.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(executed.IsErr(out _));

            var get = await coordinator.GetOperationAsync(new OperationReference(id, "owner-b"), CancellationToken.None);
            Assert.True(get.IsOk(out var snapshot));
            Assert.Equal(OperationStatus.Cancelled, snapshot!.Status);
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
            var result = await coordinator.ExecuteAsync(
                kind: "test.weight",
                target: null,
                ownerPrincipal: "owner-c",
                executor: async (context, ct) =>
                {
                    var a = context.CreateChild("a", 1);
                    var b = context.CreateChild("b", 3);
                    a.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
                    var nested = b.CreateChild("nested", 1);
                    nested.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
                    var mid = await coordinator.GetOperationAsync(new OperationReference(context.OperationId, "owner-c"), ct);
                    Assert.True(mid.IsOk(out observed));
                    return Result.Ok<string, DaemonError>(string.Empty);
                },
                cancellationToken: CancellationToken.None);

            Assert.True(result.IsOk(out _));
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
            var recovered = await coordinator.GetOperationAsync(new OperationReference(id, "owner-d"), CancellationToken.None);
            Assert.True(recovered.IsOk(out var snapshot));
            Assert.Equal(OperationStatus.Interrupted, snapshot!.Status);
            Assert.False(snapshot.Cancellable);
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
}
