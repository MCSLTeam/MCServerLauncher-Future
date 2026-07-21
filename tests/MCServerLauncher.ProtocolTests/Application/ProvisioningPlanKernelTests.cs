using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class ProvisioningPlanKernelTests
{
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

            var got = await app.GetPlanAsync(new ProvisioningPlanReference(readyPlan.PlanId), CancellationToken.None);
            Assert.True(got.IsOk(out var loaded));
            Assert.Equal(readyPlan.PlanHash, loaded!.PlanHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void BeginExecute_IsSingleUseAndOwnerBound()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plans-exec-").FullName;
        try
        {
            var kernel = new PlanKernel(rootDirectory: root);
            var put = kernel.Put(
                kind: "provisioning.instance",
                riskClass: PlanRiskClass.Routine,
                requiredPermissions: ["mcsl.provisioning.execute"],
                requiresConfirmation: false,
                creatorPrincipal: "owner-a",
                unresolved: System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>.Empty,
                idempotencyKey: null,
                expiry: TimeSpan.FromMinutes(15),
                materialize: (planId, planHash, createdAt, expiresAt, payload) => new ProvisioningPlanSnapshot(
                    planId, planHash, "provisioning.instance", PlanStatus.Ready, PlanRiskClass.Routine,
                    ["mcsl.provisioning.execute"], false, "owner-a", createdAt, expiresAt,
                    System.Collections.Immutable.ImmutableArray<ProvisioningUnresolvedFact>.Empty,
                    ProvisioningProviderKind.Vanilla, "demo", "1.21", "server.jar",
                    InstanceFactoryMirror.None, "java", null, payload));
            Assert.True(put.IsOk(out var plan));
            Assert.True((plan!.ExpiresAt - plan.CreatedAt) >= TimeSpan.FromMinutes(14));

            var forbidden = kernel.TryBeginExecute(plan.PlanId, "owner-b");
            Assert.True(forbidden.IsErr(out _));

            var begin = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(begin.IsOk(out _));
            var again = kernel.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(again.IsErr(out _));

            // Restart must not reopen an Executing plan as Ready.
            var reloaded = new PlanKernel(rootDirectory: root);
            var afterRestart = reloaded.TryBeginExecute(plan.PlanId, "owner-a");
            Assert.True(afterRestart.IsErr(out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubInstances : MCServerLauncher.Daemon.API.Application.IInstanceApplication
    {
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.CreateInstanceResult, DaemonError>> CreateInstanceAsync(
            MCServerLauncher.Common.Contracts.Instances.CreateInstanceRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StartInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> StopInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(MCServerLauncher.Common.Contracts.Instances.InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>> GetInstanceReportAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceLogResult, DaemonError>> GetInstanceLogAsync(MCServerLauncher.Common.Contracts.Instances.InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(MCServerLauncher.Common.Contracts.Instances.InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
