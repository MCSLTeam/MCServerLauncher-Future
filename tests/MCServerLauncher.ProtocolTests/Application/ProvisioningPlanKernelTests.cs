using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class ProvisioningPlanKernelTests
{
    [Theory]
    [InlineData(PlanRiskClass.Sensitive, false)]
    [InlineData(PlanRiskClass.Destructive, false)]
    [InlineData(PlanRiskClass.Routine, true)]
    public void Put_NonRoutineOrConfirmationRequiredPlanIsPersistedBlocked(
        PlanRiskClass riskClass,
        bool requiresConfirmation)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-blocked-shape-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var put = kernel.Put(
                kind: "provisioning.instance",
                riskClass: riskClass,
                requiredPermissions: ["mcsl.provisioning.execute"],
                requiresConfirmation: requiresConfirmation,
                creatorPrincipal: "owner-blocked",
                unresolved: [],
                idempotencyKey: null,
                expiry: TimeSpan.FromMinutes(15),
                materialize: (planId, planHash, createdAt, expiresAt, payload) => new ProvisioningPlanSnapshot(
                    planId, planHash, "provisioning.instance", PlanStatus.Ready, riskClass,
                    ["mcsl.provisioning.execute"], requiresConfirmation, "owner-blocked", createdAt, expiresAt,
                    [], ProvisioningProviderKind.Vanilla, "blocked-test", "1.21", "server.jar",
                    InstanceFactoryMirror.None, "java", null, payload));

            Assert.True(put.IsOk(out var plan));
            Assert.Equal(PlanStatus.Blocked, plan!.Status);
            var reloaded = new PlanKernel(rootDirectory: root);
            var persisted = reloaded.Get(plan.PlanId);
            Assert.True(persisted.IsOk(out var loaded));
            Assert.Equal(PlanStatus.Blocked, loaded!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Resolve_BlocksWhenSourceMissingAndReadyWhenComplete()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var instances = new StubInstances();
            var operations = new MCServerLauncher.Daemon.ApplicationCore.Operations.OperationCoordinator(
                rootDirectory: Path.Combine(root, "ops"));
            var app = new LocalProvisioningApplication(kernel, instances, operations);

            var blocked = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: "demo",
                MinecraftVersion: "1.21",
                Source: null,
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(blocked.IsOk(out var blockedPlan));
            Assert.Equal(PlanStatus.Blocked, blockedPlan!.Status);
            Assert.NotEmpty(blockedPlan.Unresolved);

            var missingTarget = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: string.Empty,
                MinecraftVersion: string.Empty,
                Source: "server.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(missingTarget.IsOk(out var missingTargetPlan));
            Assert.Equal(PlanStatus.Blocked, missingTargetPlan!.Status);
            Assert.Contains(missingTargetPlan.Unresolved, static fact =>
                fact.Code == "provisioning.instance_name.required" && fact.Field == "instance_name");
            Assert.Contains(missingTargetPlan.Unresolved, static fact =>
                fact.Code == "provisioning.minecraft_version.required" && fact.Field == "minecraft_version");
            var reloadedKernel = new PlanKernel(rootDirectory: root);
            var reloadedMissingTarget = reloadedKernel.Get(missingTargetPlan.PlanId);
            Assert.True(reloadedMissingTarget.IsOk(out var persistedMissingTarget));
            Assert.Equal(PlanStatus.Blocked, persistedMissingTarget!.Status);
            Assert.Equal(string.Empty, persistedMissingTarget.InstanceName);
            Assert.Equal(string.Empty, persistedMissingTarget.MinecraftVersion);

            var ready = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Paper,
                InstanceName: "paper-demo",
                MinecraftVersion: "1.21",
                Source: "paper.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a",
                IdempotencyKey: "idem-paper"), CancellationToken.None);
            Assert.True(ready.IsOk(out var readyPlan));
            Assert.Equal(PlanStatus.Ready, readyPlan!.Status);

            var again = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Paper,
                InstanceName: "paper-demo",
                MinecraftVersion: "1.21",
                Source: "paper.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a",
                IdempotencyKey: "idem-paper"), CancellationToken.None);
            Assert.True(again.IsOk(out var same));
            Assert.Equal(readyPlan.PlanId, same!.PlanId);

            var got = await app.GetPlanAsync(new ProvisioningPlanReference(readyPlan.PlanId, OwnerPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(got.IsOk(out var loaded));
            Assert.Equal(readyPlan.PlanHash, loaded!.PlanHash);

            var foreign = await app.GetPlanAsync(new ProvisioningPlanReference(readyPlan.PlanId, OwnerPrincipal: "owner-b"), CancellationToken.None);
            Assert.True(foreign.IsErr(out var forbidden));
            Assert.Equal("plan.forbidden", forbidden!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Put_ScopesIdempotencyToCreatorAndRejectsIntentMismatch()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-idempotency-scope-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var first = PutReadyPlan(
                kernel,
                creatorPrincipal: "owner-a",
                idempotencyKey: "shared-key");
            var otherPrincipal = PutReadyPlan(
                kernel,
                creatorPrincipal: "owner-b",
                idempotencyKey: "shared-key");

            Assert.True(first.IsOk(out var firstPlan));
            Assert.True(otherPrincipal.IsOk(out var otherPrincipalPlan));
            Assert.NotEqual(firstPlan!.PlanId, otherPrincipalPlan!.PlanId);
            Assert.Equal("owner-a", firstPlan.CreatorPrincipal);
            Assert.Equal("owner-b", otherPrincipalPlan.CreatorPrincipal);

            var mismatchedIntent = PutReadyPlan(
                kernel,
                creatorPrincipal: "owner-a",
                idempotencyKey: "shared-key",
                source: "different-server.jar");
            Assert.True(mismatchedIntent.IsErr(out var mismatchError));
            Assert.Equal("plan.idempotency_conflict", mismatchError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(PlanStatus.Consumed)]
    [InlineData(PlanStatus.Expired)]
    public void Put_ReusesTerminalIdempotencyKeyAndReloads(PlanStatus terminalStatus)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-idempotency-terminal-reuse-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
        try
        {
            var kernel = new PlanKernel(timeProvider: time, rootDirectory: root);
            var first = PutReadyPlan(
                kernel,
                expiry: terminalStatus == PlanStatus.Expired ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15),
                creatorPrincipal: "owner-terminal-reuse",
                idempotencyKey: "reusable-key");
            Assert.True(first.IsOk(out var firstPlan));

            if (terminalStatus == PlanStatus.Consumed)
            {
                Assert.True(kernel.TryBeginExecute(firstPlan!.PlanId, "owner-terminal-reuse").IsOk(out _));
                Assert.True(kernel.CompleteAcceptedExecute(firstPlan.PlanId).IsOk(out _));
            }
            else
            {
                time.Advance(TimeSpan.FromMinutes(2));
            }

            var second = PutReadyPlan(
                kernel,
                creatorPrincipal: "owner-terminal-reuse",
                idempotencyKey: "reusable-key");
            Assert.True(second.IsOk(out var secondPlan));
            Assert.NotEqual(firstPlan!.PlanId, secondPlan!.PlanId);

            var reloaded = new PlanKernel(timeProvider: time, rootDirectory: root);
            var retained = reloaded.Get(secondPlan.PlanId);
            Assert.True(retained.IsOk(out var retainedPlan));
            Assert.Equal(PlanStatus.Ready, retainedPlan!.Status);

            var repeated = PutReadyPlan(
                reloaded,
                creatorPrincipal: "owner-terminal-reuse",
                idempotencyKey: "reusable-key");
            Assert.True(repeated.IsOk(out var repeatedPlan));
            Assert.Equal(secondPlan.PlanId, repeatedPlan!.PlanId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Put_ConcurrentFirstWriterForSamePrincipalAndIntentReturnsOnePlan()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-idempotency-concurrent-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var puts = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => Task.Run(() => PutReadyPlan(
                        kernel,
                        creatorPrincipal: "owner-concurrent",
                        idempotencyKey: "same-intent"))));

            var plans = puts.Select(result =>
            {
                Assert.True(result.IsOk(out var plan));
                return plan!;
            }).ToArray();
            Assert.Single(plans.Select(static plan => plan.PlanId).Distinct());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Put_ComputesCanonicalContentHashFromImmutableTargetFactsAndPayload()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-content-hash-").FullName;
        try
        {
            var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-23T00:00:00Z"));
            var kernel = new PlanKernel(time, root);
            var baseline = PutReadyPlan(
                kernel,
                expiry: TimeSpan.FromMinutes(1),
                creatorPrincipal: "owner-a",
                idempotencyKey: "hash-a",
                payloadJson: """{"z":1,"a":{"y":2,"x":3}}""");
            time.Advance(TimeSpan.FromMinutes(1));
            var sameContent = PutReadyPlan(
                kernel,
                expiry: TimeSpan.FromMinutes(10),
                creatorPrincipal: "owner-b",
                idempotencyKey: "hash-b",
                payloadJson: """{"a":{"x":3,"y":2},"z":1}""");

            Assert.True(baseline.IsOk(out var baselinePlan));
            Assert.True(sameContent.IsOk(out var sameContentPlan));
            Assert.Equal(baselinePlan!.PlanHash, sameContentPlan!.PlanHash);

            var changedTargetFacts = new[]
            {
                PutReadyPlan(kernel, provider: ProvisioningProviderKind.Paper),
                PutReadyPlan(kernel, instanceName: "different-name"),
                PutReadyPlan(kernel, minecraftVersion: "1.21.1"),
                PutReadyPlan(kernel, source: "different-server.jar"),
                PutReadyPlan(kernel, mirror: InstanceFactoryMirror.BmclApi),
                PutReadyPlan(kernel, javaPath: "custom-java"),
                PutReadyPlan(
                    kernel,
                    unresolved: [new ProvisioningUnresolvedFact(
                        "provisioning.choice.required",
                        "An explicit provisioning choice is required.",
                        "choice")]),
            };

            foreach (var changed in changedTargetFacts)
            {
                Assert.True(changed.IsOk(out var changedPlan));
                Assert.NotEqual(baselinePlan.PlanHash, changedPlan!.PlanHash);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ReturnsOperationHandleBeforeProvisioningCompletes()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-immediate-").FullName;
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var instances = new StubInstances(async (request, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Result.Ok<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>(
                    new MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult(request.Setting.Configuration));
            });
            var kernel = new PlanKernel(rootDirectory: root);
            await using var operations = new MCServerLauncher.Daemon.ApplicationCore.Operations.OperationCoordinator(
                rootDirectory: Path.Combine(root, "ops"));
            var app = new LocalProvisioningApplication(kernel, instances, operations);
            var resolved = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: "demo",
                MinecraftVersion: "1.21",
                Source: "server.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(resolved.IsOk(out var plan));

            using var requestCancellation = new CancellationTokenSource();
            var execute = await app.ExecuteAsync(
                new ProvisioningExecuteRequest(plan!.PlanId, "owner-a"),
                requestCancellation.Token);

            Assert.True(execute.IsOk(out var handle));
            Assert.Equal(plan.PlanId, handle!.PlanId);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            requestCancellation.Cancel();

            var running = await operations.GetOperationAsync(
                new OperationReference(handle.OperationId, "owner-a"),
                CancellationToken.None);
            Assert.True(running.IsOk(out var runningSnapshot));
            Assert.Equal(OperationStatus.Running, runningSnapshot!.Status);

            release.TrySetResult();
            OperationSnapshot? completed = null;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                while (true)
                {
                    var current = await operations.GetOperationAsync(
                        new OperationReference(handle.OperationId, "owner-a"),
                        CancellationToken.None);
                    Assert.True(current.IsOk(out completed));
                    if (completed!.Status == OperationStatus.Succeeded)
                        break;
                    await Task.Delay(10, timeout.Token);
                }
            }

            Assert.True(Guid.TryParse(completed!.ResultReference, out _));
            var consumed = kernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Put_PersistenceFailurePublishesNeitherPlanNorIdempotencyClaim()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-admission-failure-").FullName;
        var tempPath = Path.Combine(root, "index.json.tmp");
        Guid candidateId = Guid.Empty;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            Directory.CreateDirectory(tempPath);

            Result<ProvisioningPlanSnapshot, DaemonError> Put() => kernel.Put(
                kind: "provisioning.instance",
                riskClass: PlanRiskClass.Routine,
                requiredPermissions: ["mcsl.provisioning.execute"],
                requiresConfirmation: false,
                creatorPrincipal: "owner-admission",
                unresolved: [],
                idempotencyKey: "admission-key",
                expiry: TimeSpan.FromMinutes(15),
                materialize: (planId, planHash, createdAt, expiresAt, payload) =>
                {
                    candidateId = planId;
                    return new ProvisioningPlanSnapshot(
                        planId, planHash, "provisioning.instance", PlanStatus.Ready, PlanRiskClass.Routine,
                        ["mcsl.provisioning.execute"], false, "owner-admission", createdAt, expiresAt,
                        [], ProvisioningProviderKind.Vanilla, "admission-test", "1.21", "server.jar",
                        InstanceFactoryMirror.None, "java", "admission-key", payload);
                });

            var exception = Record.Exception(() => Put());
            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.NotEqual(Guid.Empty, candidateId);
            var failedCandidateId = candidateId;
            var hidden = kernel.Get(failedCandidateId);
            Assert.True(hidden.IsErr(out var hiddenError));
            Assert.Equal("plan.not_found", hiddenError!.Code);

            Directory.Delete(tempPath);
            var retried = Put();
            Assert.True(retried.IsOk(out var persisted));
            Assert.NotEqual(failedCandidateId, persisted!.PlanId);
            var reloaded = new PlanKernel(rootDirectory: root);
            Assert.True(reloaded.Get(persisted.PlanId).IsOk(out _));
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_RequestCancellationBeforeOperationAcceptanceRestoresReadyPlan()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-cancel-admission-").FullName;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var time = new CallbackTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
            var kernel = new PlanKernel(timeProvider: time, rootDirectory: root);
            await using var operations = new MCServerLauncher.Daemon.ApplicationCore.Operations.OperationCoordinator(
                rootDirectory: Path.Combine(root, "ops"));
            var app = new LocalProvisioningApplication(kernel, new StubInstances(), operations);
            var resolved = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: "demo",
                MinecraftVersion: "1.21",
                Source: "server.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(resolved.IsOk(out var plan));

            // TryBeginExecute reads the clock before it commits Executing. Cancel there so the
            // subsequent operation admission observes cancellation deterministically.
            time.OnNextRead(cancellation.Cancel);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.ExecuteAsync(
                new ProvisioningExecuteRequest(plan!.PlanId, "owner-a"),
                cancellation.Token));

            var restored = kernel.Get(plan!.PlanId);
            Assert.True(restored.IsOk(out var restoredPlan));
            Assert.Equal(PlanStatus.Ready, restoredPlan!.Status);

            var operationList = await operations.ListOperationsAsync(
                new OperationListQuery("owner-a"),
                CancellationToken.None);
            Assert.True(operationList.IsOk(out var operationsForOwner));
            Assert.Empty(operationsForOwner!.Operations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("failure", OperationStatus.Failed)]
    [InlineData("cancel", OperationStatus.Cancelled)]
    public async Task Execute_AcceptedTerminalOutcomeConsumesPlanAndRejectsReplay(
        string mode,
        OperationStatus expectedStatus)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-accepted-terminal-").FullName;
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var instances = new StubInstances(async (request, cancellationToken) =>
            {
                _ = cancellationToken;
                entered.TrySetResult();
                await release.Task;
                return mode == "failure"
                    ? Result.Err<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>(
                        new ValidationDaemonError("provisioning.failed", "Provisioning failed."))
                    : Result.Ok<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>(
                        new MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult(request.Setting.Configuration));
            });
            var kernel = new PlanKernel(rootDirectory: root);
            await using var operations = new OperationCoordinator(rootDirectory: Path.Combine(root, "ops"));
            var app = new LocalProvisioningApplication(kernel, instances, operations);
            var resolved = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: "demo",
                MinecraftVersion: "1.21",
                Source: "server.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(resolved.IsOk(out var plan));

            var execute = await app.ExecuteAsync(
                new ProvisioningExecuteRequest(plan!.PlanId, "owner-a"),
                CancellationToken.None);
            Assert.True(execute.IsOk(out var handle));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            if (mode == "cancel")
            {
                var cancel = await operations.CancelOperationAsync(
                    new OperationCancelRequest(handle!.OperationId, "owner-a"),
                    CancellationToken.None);
                Assert.True(cancel.IsOk(out var requested));
                Assert.True(requested!.CancelRequested);
            }

            release.TrySetResult();
            var terminal = await WaitForTerminalAsync(operations, handle!.OperationId, "owner-a");
            Assert.Equal(expectedStatus, terminal.Status);

            var consumed = kernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);

            var replay = await app.ExecuteAsync(
                new ProvisioningExecuteRequest(plan.PlanId, "owner-a"),
                CancellationToken.None);
            Assert.True(replay.IsErr(out var replayError));
            Assert.Equal("plan.single_flight", replayError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ContinuousPlanPersistenceFailureReconcilesWithoutRestart()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-terminal-persist-").FullName;
        var operationsRoot = Path.Combine(root, "ops");
        var logger = new RecordingLogger<PlanKernel>();
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var instances = new StubInstances(async (request, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Result.Ok<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>(
                    new MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult(request.Setting.Configuration));
            });
            var kernel = new PlanKernel(rootDirectory: root, logger: logger);
            await using var operations = new OperationCoordinator(rootDirectory: operationsRoot);
            var app = new LocalProvisioningApplication(kernel, instances, operations);
            var resolved = await app.ResolveAsync(new ProvisioningResolveRequest(
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: "demo",
                MinecraftVersion: "1.21",
                Source: "server.jar",
                Mirror: InstanceFactoryMirror.None,
                JavaPath: "java",
                CreatorPrincipal: "owner-a"), CancellationToken.None);
            Assert.True(resolved.IsOk(out var plan));

            var execute = await app.ExecuteAsync(
                new ProvisioningExecuteRequest(plan!.PlanId, "owner-a"),
                CancellationToken.None);
            Assert.True(execute.IsOk(out var handle));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // CompleteAcceptedExecute writes this exact temporary path before its atomic replace.
            // A directory at that path keeps both the initial write and its single retry failing.
            Directory.CreateDirectory(Path.Combine(root, "index.json.tmp"));
            release.TrySetResult();

            var retainedSnapshot = await WaitForTerminalAsync(operations, handle!.OperationId, "owner-a");
            Assert.Equal(OperationStatus.Interrupted, retainedSnapshot.Status);
            Assert.False(retainedSnapshot.Cancellable);
            var retained = kernel.Get(plan.PlanId);
            Assert.True(retained.IsOk(out var retainedPlan));
            Assert.Equal(PlanStatus.Executing, retainedPlan!.Status);
            Assert.True(logger.Entries.Count(entry =>
                entry.Exception is IOException or UnauthorizedAccessException) >= 2);
            Assert.Contains(logger.Entries, static entry => entry.Level == LogLevel.Error);

            Directory.Delete(Path.Combine(root, "index.json.tmp"));
            var consumedPlan = await WaitForPlanStatusAsync(kernel, plan.PlanId, PlanStatus.Consumed);
            Assert.Equal(PlanStatus.Consumed, consumedPlan.Status);

            var retainedOperation = await operations.GetOperationAsync(
                new OperationReference(handle.OperationId, "owner-a"),
                CancellationToken.None);
            Assert.True(retainedOperation.IsOk(out var interrupted));
            Assert.Equal(OperationStatus.Interrupted, interrupted!.Status);

            var replay = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(replay.IsErr(out var replayError));
            Assert.Equal("plan.single_flight", replayError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task BeginExecute_IsSingleUseOwnerBoundAndRecoversAbandonedAdmission()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-exec-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var put = PutReadyPlan(kernel);
            Assert.True(put.IsOk(out var plan));
            Assert.True((plan!.ExpiresAt - plan.CreatedAt) >= TimeSpan.FromMinutes(14));

            var forbidden = kernel.TryBeginExecute(plan.PlanId, "owner-b");
            Assert.True(forbidden.IsErr(out _));

            var begin = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(begin.IsOk(out _));
            var again = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(again.IsErr(out _));

            // No accepted operation exists for this durable claim, so restart treats it as an
            // admission abort and reopens it for full revalidation.
            await using var emptyOperations = new OperationCoordinator(
                rootDirectory: Path.Combine(root, "ops-empty"));
            var reloaded = new PlanKernel(rootDirectory: root);
            new OperationStartupRecovery(emptyOperations, reloaded).Recover();
            var recovered = reloaded.Get(plan.PlanId);
            Assert.True(recovered.IsOk(out var recoveredPlan));
            Assert.Equal(PlanStatus.Ready, recoveredPlan!.Status);
            var afterRestart = reloaded.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(afterRestart.IsOk(out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompleteAcceptedExecute_ReturnsExplicitErrorsAndConsumesOnlyExecutingPlan()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-complete-result-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var missing = kernel.CompleteAcceptedExecute(Guid.NewGuid());
            Assert.True(missing.IsErr(out var missingError));
            Assert.Equal("plan.not_found", missingError!.Code);

            var put = PutReadyPlan(kernel);
            Assert.True(put.IsOk(out var plan));
            var notExecuting = kernel.CompleteAcceptedExecute(plan!.PlanId);
            Assert.True(notExecuting.IsErr(out var invalidState));
            Assert.Equal("plan.invalid_state", invalidState!.Code);

            var begin = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(begin.IsOk(out _));
            var completed = kernel.CompleteAcceptedExecute(plan.PlanId);
            Assert.True(completed.IsOk(out _));
            var consumed = kernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);

            var repeated = kernel.CompleteAcceptedExecute(plan.PlanId);
            Assert.True(repeated.IsErr(out var repeatedError));
            Assert.Equal("plan.invalid_state", repeatedError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExecutingPlan_DoesNotExpireAfterItsOriginalTtl()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-executing-expiry-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-22T00:00:00Z"));
        try
        {
            var kernel = new PlanKernel(timeProvider: time, rootDirectory: root);
            var put = PutReadyPlan(kernel, TimeSpan.FromMinutes(1));
            Assert.True(put.IsOk(out var plan));
            var begin = kernel.TryBeginExecute(plan!.PlanId, "owner-a");
            Assert.True(begin.IsOk(out _));

            time.Advance(TimeSpan.FromHours(1));
            var retained = kernel.Get(plan.PlanId);
            Assert.True(retained.IsOk(out var executing));
            Assert.Equal(PlanStatus.Executing, executing!.Status);

            var reloaded = new PlanKernel(timeProvider: time, rootDirectory: root);
            var retainedAfterReload = reloaded.Get(plan.PlanId);
            Assert.True(retainedAfterReload.IsOk(out var reloadedExecuting));
            Assert.Equal(PlanStatus.Executing, reloadedExecuting!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PurgeExpired_LinearizesWithExecuteAdmissionUnderRecordGate()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-expiry-linearization-").FullName;
        var initial = DateTimeOffset.Parse("2026-07-22T00:00:00Z");
        var time = new BlockingTimeProvider(initial);
        try
        {
            var kernel = new PlanKernel(timeProvider: time, rootDirectory: root);
            var put = PutReadyPlan(kernel, TimeSpan.FromMinutes(1));
            Assert.True(put.IsOk(out var plan));
            var expiredAt = plan!.ExpiresAt + TimeSpan.FromSeconds(1);

            var expiryReadEntered = time.ArmNextRead(expiredAt);
            var getTask = Task.Run(() => kernel.Get(plan.PlanId));
            await expiryReadEntered.WaitAsync(TimeSpan.FromSeconds(3));

            var beginInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var beginTask = Task.Run(() =>
            {
                beginInvoked.TrySetResult();
                return kernel.TryBeginExecute(plan.PlanId, "owner-a");
            });
            await beginInvoked.Task.WaitAsync(TimeSpan.FromSeconds(3));

            time.SetUtcNow(expiredAt);
            time.ReleaseBlockedRead();
            var got = await getTask.WaitAsync(TimeSpan.FromSeconds(3));
            var begin = await beginTask.WaitAsync(TimeSpan.FromSeconds(3));

            if (begin.IsOk(out _))
            {
                Assert.True(got.IsOk(out var executing));
                Assert.Equal(PlanStatus.Executing, executing!.Status);
            }
            else
            {
                Assert.True(begin.IsErr(out var beginError));
                Assert.Equal("plan.expired", beginError!.Code);
                Assert.True(got.IsErr(out var getError));
                Assert.Equal("plan.expired", getError!.Code);
            }
        }
        finally
        {
            time.ReleaseBlockedRead();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void Load_InvalidExistingIndexFailsClosed(string content)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-corrupt-index-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.json"), content);

            Assert.Throws<InvalidDataException>(() => new PlanKernel(rootDirectory: root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("ready-unresolved")]
    [InlineData("ready-confirmation")]
    [InlineData("ready-sensitive")]
    [InlineData("blocked-without-blocker")]
    [InlineData("executing-unresolved")]
    [InlineData("executing-confirmation")]
    [InlineData("executing-destructive")]
    [InlineData("consumed-unresolved")]
    [InlineData("blocked-missing-name-without-fact")]
    [InlineData("blocked-name-fact-with-value")]
    [InlineData("blocked-missing-version-without-fact")]
    [InlineData("blocked-version-fact-with-value")]
    public void Load_SemanticallyInvalidStatusShapeFailsClosed(string corruption)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-corrupt-state-").FullName;
        try
        {
            var snapshot = CreateSemanticallyInvalidPlan(corruption);
            File.WriteAllBytes(
                Path.Combine(root, "index.json"),
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    new[] { snapshot },
                    PlanKernelJsonContext.Default.ProvisioningPlanSnapshotArray));

            Assert.Throws<InvalidDataException>(() => new PlanKernel(rootDirectory: root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StartupRecovery_PlanPersistenceFailureFailsClosedAndRetryCompletesBarrier()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-recovery-persist-").FullName;
        var operationRoot = Path.Combine(root, "ops");
        var recoveryLogger = new RecordingLogger<OperationStartupRecovery>();
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var put = PutReadyPlan(kernel);
            Assert.True(put.IsOk(out var plan));
            Assert.True(kernel.TryBeginExecute(plan!.PlanId, "owner-a").IsOk(out _));

            var operationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var createdAt = DateTimeOffset.Parse("2026-07-22T00:00:00Z");
            var running = new OperationSnapshot(
                operationId,
                "provisioning.execute",
                plan.PlanId.ToString("D"),
                "owner-a",
                OperationStatus.Running,
                OperationStage.Installing,
                new OperationProgress(true, null, null, null, null, null, null),
                2,
                createdAt,
                createdAt + TimeSpan.FromSeconds(1),
                null,
                true,
                null,
                null,
                null);
            Directory.CreateDirectory(operationRoot);
            var operationIndexPath = Path.Combine(operationRoot, "index.json");
            File.WriteAllBytes(
                operationIndexPath,
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    new[] { running },
                    OperationPersistenceJsonContext.Default.OperationSnapshotArray));

            await using var recoveredOperations = new OperationCoordinator(rootDirectory: operationRoot);
            var recoveredKernel = new PlanKernel(rootDirectory: root);
            var recovery = new OperationStartupRecovery(
                recoveredOperations,
                recoveredKernel,
                recoveryLogger);
            var planIndexPath = Path.Combine(root, "index.json");
            var planBytesBefore = File.ReadAllBytes(planIndexPath);
            var operationBytesBefore = File.ReadAllBytes(operationIndexPath);
            var planTempPath = planIndexPath + ".tmp";
            Directory.CreateDirectory(planTempPath);

            var exception = Record.Exception(recovery.Recover);
            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.False(recovery.IsCompleted);
            Assert.Contains(recoveryLogger.Entries, entry => ReferenceEquals(entry.Exception, exception));

            var nonTerminal = await recoveredOperations.GetOperationAsync(
                new OperationReference(operationId, "owner-a"),
                CancellationToken.None);
            Assert.True(nonTerminal.IsOk(out var runningAfterFailure));
            Assert.Equal(OperationStatus.Running, runningAfterFailure!.Status);
            var executing = recoveredKernel.Get(plan.PlanId);
            Assert.True(executing.IsOk(out var executingAfterFailure));
            Assert.Equal(PlanStatus.Executing, executingAfterFailure!.Status);
            Assert.Equal(planBytesBefore, File.ReadAllBytes(planIndexPath));
            Assert.Equal(operationBytesBefore, File.ReadAllBytes(operationIndexPath));

            Directory.Delete(planTempPath);
            recovery.Recover();
            Assert.True(recovery.IsCompleted);

            var interrupted = await recoveredOperations.GetOperationAsync(
                new OperationReference(operationId, "owner-a"),
                CancellationToken.None);
            Assert.True(interrupted.IsOk(out var interruptedSnapshot));
            Assert.Equal(OperationStatus.Interrupted, interruptedSnapshot!.Status);
            var consumed = recoveredKernel.Get(plan.PlanId);
            Assert.True(consumed.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(OperationStatus.Queued, OperationStage.Queued, 1L)]
    [InlineData(OperationStatus.Running, OperationStage.Installing, 2L)]
    public async Task Restart_AcceptedNonTerminalOperationConsumesPlanAndRejectsReplay(
        OperationStatus persistedStatus,
        OperationStage persistedStage,
        long persistedVersion)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-reconcile-accepted-").FullName;
        var operationRoot = Path.Combine(root, "ops");
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var put = PutReadyPlan(kernel);
            Assert.True(put.IsOk(out var plan));
            var begin = kernel.TryBeginExecute(plan!.PlanId, "owner-a");
            Assert.True(begin.IsOk(out _));

            var operationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var persistedOperation = $$"""
                [{"operation_id":"{{operationId:D}}","kind":"provisioning.execute","target":"{{plan.PlanId:D}}","owner_principal":"owner-a","status":"{{persistedStatus}}","stage":"{{persistedStage}}","progress":{"indeterminate":true,"completed":null,"total":null,"unit":null,"bytes_transferred":null,"bytes_total":null,"rate":null},"version":{{persistedVersion}},"created_at":"2026-07-22T00:00:00+00:00","updated_at":"2026-07-22T00:00:01+00:00","completed_at":null,"cancellable":true,"error_code":null,"error_message":null,"result_reference":null}]
                """;
            Directory.CreateDirectory(operationRoot);
            await File.WriteAllTextAsync(Path.Combine(operationRoot, "index.json"), persistedOperation);

            await using var recoveredOperations = new OperationCoordinator(rootDirectory: operationRoot);
            var recoveredKernel = new PlanKernel(rootDirectory: root);
            var startupRecovery = new OperationStartupRecovery(recoveredOperations, recoveredKernel);

            var beforeRecovery = await recoveredOperations.GetOperationAsync(
                new OperationReference(operationId, "owner-a"),
                CancellationToken.None);
            Assert.True(beforeRecovery.IsOk(out var nonTerminal));
            Assert.Equal(persistedStatus, nonTerminal!.Status);
            var planBeforeRecovery = recoveredKernel.Get(plan.PlanId);
            Assert.True(planBeforeRecovery.IsOk(out var executingPlan));
            Assert.Equal(PlanStatus.Executing, executingPlan!.Status);
            Assert.False(startupRecovery.IsCompleted);

            startupRecovery.Recover();
            Assert.True(startupRecovery.IsCompleted);
            var interrupted = await recoveredOperations.GetOperationAsync(
                new OperationReference(operationId, "owner-a"),
                CancellationToken.None);
            Assert.True(interrupted.IsOk(out var interruptedSnapshot));
            Assert.Equal(OperationStatus.Interrupted, interruptedSnapshot!.Status);

            var recoveredPlan = recoveredKernel.Get(plan.PlanId);
            Assert.True(recoveredPlan.IsOk(out var consumedPlan));
            Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
            var replay = recoveredKernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(replay.IsErr(out var replayError));
            Assert.Equal("plan.single_flight", replayError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProvisioningPlanSnapshot CreateSemanticallyInvalidPlan(string corruption)
    {
        using var payloadDocument = System.Text.Json.JsonDocument.Parse("{\"kind\":\"provisioning.instance\"}");
        var ready = new ProvisioningPlanSnapshot(
            Guid.NewGuid(),
            new string('a', 64),
            "provisioning.instance",
            PlanStatus.Ready,
            PlanRiskClass.Routine,
            ["mcsl.provisioning.execute"],
            false,
            "owner-corrupt",
            DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-22T00:15:00Z"),
            [],
            ProvisioningProviderKind.Vanilla,
            "corrupt-test",
            "1.21",
            "server.jar",
            InstanceFactoryMirror.None,
            "java",
            null,
            payloadDocument.RootElement.Clone());
        var unresolved = new ProvisioningUnresolvedFact(
            "provisioning.source_required",
            "A source is required.",
            "source");
        var missingName = new ProvisioningUnresolvedFact(
            "provisioning.instance_name.required",
            "Instance name is required.",
            "instance_name");
        var missingVersion = new ProvisioningUnresolvedFact(
            "provisioning.minecraft_version.required",
            "Minecraft version is required.",
            "minecraft_version");

        return corruption switch
        {
            "ready-unresolved" => ready with { Unresolved = [unresolved] },
            "ready-confirmation" => ready with { RequiresConfirmation = true },
            "ready-sensitive" => ready with { RiskClass = PlanRiskClass.Sensitive },
            "blocked-without-blocker" => ready with { Status = PlanStatus.Blocked },
            "executing-unresolved" => ready with { Status = PlanStatus.Executing, Unresolved = [unresolved] },
            "executing-confirmation" => ready with { Status = PlanStatus.Executing, RequiresConfirmation = true },
            "executing-destructive" => ready with { Status = PlanStatus.Executing, RiskClass = PlanRiskClass.Destructive },
            "consumed-unresolved" => ready with { Status = PlanStatus.Consumed, Unresolved = [unresolved] },
            "blocked-missing-name-without-fact" => ready with
            {
                Status = PlanStatus.Blocked,
                RiskClass = PlanRiskClass.Sensitive,
                InstanceName = string.Empty,
            },
            "blocked-name-fact-with-value" => ready with { Status = PlanStatus.Blocked, Unresolved = [missingName] },
            "blocked-missing-version-without-fact" => ready with
            {
                Status = PlanStatus.Blocked,
                RiskClass = PlanRiskClass.Sensitive,
                MinecraftVersion = string.Empty,
            },
            "blocked-version-fact-with-value" => ready with { Status = PlanStatus.Blocked, Unresolved = [missingVersion] },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null),
        };
    }

    private static Result<ProvisioningPlanSnapshot, DaemonError> PutReadyPlan(
        PlanKernel kernel,
        TimeSpan? expiry = null,
        string creatorPrincipal = "owner-a",
        string? idempotencyKey = null,
        ProvisioningProviderKind provider = ProvisioningProviderKind.Vanilla,
        string instanceName = "demo",
        string minecraftVersion = "1.21",
        string source = "server.jar",
        InstanceFactoryMirror mirror = InstanceFactoryMirror.None,
        string? javaPath = "java",
        System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>? unresolved = null,
        string payloadJson = """{"kind":"provisioning.instance"}""")
    {
        using var payloadDocument = JsonDocument.Parse(payloadJson);
        var resolvedUnresolved = unresolved ?? System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>.Empty;
        return kernel.Put(
            kind: "provisioning.instance",
            riskClass: PlanRiskClass.Routine,
            requiredPermissions: ["mcsl.provisioning.execute"],
            requiresConfirmation: false,
            creatorPrincipal: creatorPrincipal,
            unresolved: resolvedUnresolved,
            idempotencyKey: idempotencyKey,
            expiry: expiry ?? TimeSpan.FromMinutes(15),
            materialize: (planId, planHash, createdAt, expiresAt, _) => new ProvisioningPlanSnapshot(
                planId, planHash, "provisioning.instance", PlanStatus.Ready, PlanRiskClass.Routine,
                ["mcsl.provisioning.execute"], false, creatorPrincipal, createdAt, expiresAt,
                resolvedUnresolved, provider, instanceName, minecraftVersion, source,
                mirror, javaPath, idempotencyKey, payloadDocument.RootElement.Clone()));
    }

    private static async Task<OperationSnapshot> WaitForTerminalAsync(
        OperationCoordinator operations,
        Guid operationId,
        string owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var current = await operations.GetOperationAsync(
                new OperationReference(operationId, owner),
                CancellationToken.None);
            Assert.True(current.IsOk(out var snapshot));
            if (snapshot!.Status is OperationStatus.Succeeded
                or OperationStatus.Failed
                or OperationStatus.Cancelled
                or OperationStatus.Interrupted)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<ProvisioningPlanSnapshot> WaitForPlanStatusAsync(
        PlanKernel kernel,
        Guid planId,
        PlanStatus expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var current = kernel.Get(planId);
            Assert.True(current.IsOk(out var snapshot));
            if (snapshot!.Status == expectedStatus)
                return snapshot;

            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubInstances(
        Func<MCServerLauncher.Common.Contracts.Instances.CreateInstanceRequest,
            CancellationToken,
            Task<Result<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>>>? create = null)
        : MCServerLauncher.Daemon.API.Application.IInstanceApplication
    {
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>> CreateInstanceAsync(
            MCServerLauncher.Common.Contracts.Instances.CreateInstanceRequest request,
            CancellationToken cancellationToken) => create?.Invoke(request, cancellationToken)
            ?? throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StartInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StopInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(MCServerLauncher.Common.Contracts.Instances.InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.ConsoleSession, DaemonError>> OpenConsoleAsync(MCServerLauncher.Common.Contracts.Instances.ConsoleOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(MCServerLauncher.Common.Contracts.Instances.ConsoleResizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseConsoleAsync(MCServerLauncher.Common.Contracts.Instances.ConsoleSessionReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteConsoleAsync(Guid sessionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>> GetInstanceReportAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceLogResult, DaemonError>> GetInstanceLogAsync(MCServerLauncher.Common.Contracts.Instances.InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CallbackTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private Action? _nextRead;

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Exchange(ref _nextRead, null)?.Invoke();
            return now;
        }

        internal void OnNextRead(Action callback) =>
            Interlocked.Exchange(ref _nextRead, callback);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class BlockingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = now;
        private DateTimeOffset _blockedValue;
        private TaskCompletionSource? _blockedReadEntered;
        private ManualResetEventSlim? _releaseBlockedRead;
        private bool _blockNextRead;

        public override DateTimeOffset GetUtcNow()
        {
            TaskCompletionSource? entered;
            ManualResetEventSlim? release;
            DateTimeOffset blockedValue;
            lock (_gate)
            {
                if (!_blockNextRead)
                    return _now;

                _blockNextRead = false;
                entered = _blockedReadEntered;
                release = _releaseBlockedRead;
                blockedValue = _blockedValue;
            }

            entered!.TrySetResult();
            if (!release!.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The plan expiry test barrier timed out.");
            return blockedValue;
        }

        internal Task ArmNextRead(DateTimeOffset value)
        {
            lock (_gate)
            {
                _blockedValue = value;
                _blockedReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _releaseBlockedRead = new ManualResetEventSlim();
                _blockNextRead = true;
                return _blockedReadEntered.Task;
            }
        }

        internal void SetUtcNow(DateTimeOffset value)
        {
            lock (_gate)
                _now = value;
        }

        internal void ReleaseBlockedRead()
        {
            ManualResetEventSlim? release;
            lock (_gate)
                release = _releaseBlockedRead;
            release?.Set();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        private readonly List<LogEntry> _entries = [];

        internal ImmutableArray<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                    return [.. _entries];
            }
        }

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
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
