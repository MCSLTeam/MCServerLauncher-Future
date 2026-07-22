using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Provisioning;

internal sealed class LocalProvisioningApplication(
    PlanKernel planKernel,
    IInstanceApplication instances,
    IOperationApplication operations) : IProvisioningApplication
{
    private OperationCoordinator Coordinator =>
        operations as OperationCoordinator
        ?? throw new InvalidOperationException("Provisioning requires OperationCoordinator as IOperationApplication.");

    private static readonly ImmutableArray<string> RequiredPermissions =
        ["mcsl.provisioning.resolve", "mcsl.provisioning.get", "mcsl.provisioning.execute"];

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ResolveAsync(
        ProvisioningResolveRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatorPrincipal);

        var unresolved = ImmutableArray.CreateBuilder<ProvisioningUnresolvedFact>();
        if (string.IsNullOrWhiteSpace(request.InstanceName))
            unresolved.Add(new ProvisioningUnresolvedFact("provisioning.instance_name.required", "Instance name is required.", "instance_name"));
        if (string.IsNullOrWhiteSpace(request.MinecraftVersion))
            unresolved.Add(new ProvisioningUnresolvedFact("provisioning.minecraft_version.required", "Minecraft version is required.", "minecraft_version"));
        if (string.IsNullOrWhiteSpace(request.Source))
            unresolved.Add(new ProvisioningUnresolvedFact("provisioning.source.required", "A provider source path or URI is required.", "source"));

        if (!TryMapProvider(request.Provider, out _))
        {
            unresolved.Add(new ProvisioningUnresolvedFact(
                "provisioning.provider.unsupported",
                $"Provider '{request.Provider}' is not supported in Preview-1.",
                "provider"));
        }

        var put = planKernel.Put(
            kind: "provisioning.instance",
            riskClass: PlanRiskClass.Routine,
            requiredPermissions: RequiredPermissions,
            requiresConfirmation: false,
            creatorPrincipal: request.CreatorPrincipal,
            unresolved: unresolved.ToImmutable(),
            idempotencyKey: request.IdempotencyKey,
            expiry: request.Expiry,
            materialize: (planId, planHash, createdAt, expiresAt, payload) => new ProvisioningPlanSnapshot(
                PlanId: planId,
                PlanHash: planHash,
                Kind: "provisioning.instance",
                Status: PlanStatus.Ready,
                RiskClass: PlanRiskClass.Routine,
                RequiredPermissions: RequiredPermissions,
                RequiresConfirmation: false,
                CreatorPrincipal: request.CreatorPrincipal,
                CreatedAt: createdAt,
                ExpiresAt: expiresAt,
                Unresolved: ImmutableArray<ProvisioningUnresolvedFact>.Empty,
                Provider: request.Provider,
                InstanceName: request.InstanceName,
                MinecraftVersion: request.MinecraftVersion,
                Source: request.Source,
                Mirror: request.Mirror,
                JavaPath: request.JavaPath,
                IdempotencyKey: request.IdempotencyKey,
                Payload: payload));

        return Task.FromResult(put);
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> GetPlanAsync(
        ProvisioningPlanReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.OwnerPrincipal))
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new PermissionDaemonError(
                    "auth.subject_required",
                    "Connection subject is required for provisioning ownership binding.")));
        }

        var got = planKernel.Get(request.PlanId);
        if (got.IsErr(out var error))
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(error!));

        var snapshot = got.Unwrap();
        if (!string.Equals(snapshot.CreatorPrincipal, request.OwnerPrincipal, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new PermissionDaemonError("plan.forbidden", "The caller cannot read this plan.")));
        }

        return Task.FromResult(Result.Ok<ProvisioningPlanSnapshot, DaemonError>(snapshot));
    }

    public async Task<Result<ProvisioningExecuteResult, DaemonError>> ExecuteAsync(
        ProvisioningExecuteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var begin = planKernel.TryBeginExecute(request.PlanId, request.ExecutorPrincipal);
        if (begin.IsErr(out var beginError))
            return Result.Err<ProvisioningExecuteResult, DaemonError>(beginError!);

        var plan = begin.Unwrap();
        if (!TryMapProvider(plan.Provider, out var instanceType))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<ProvisioningExecuteResult, DaemonError>(
                new ValidationDaemonError("provisioning.provider.unsupported", "The plan provider is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(plan.Source))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<ProvisioningExecuteResult, DaemonError>(
                new ValidationDaemonError("provisioning.source.required", "The plan source is missing."));
        }

        var factory = BuildFactoryConfiguration(plan, instanceType);
        Result<OperationSnapshot, DaemonError> execute;
        try
        {
            execute = await Coordinator.StartAsync(
                kind: "provisioning.execute",
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: request.ExecutorPrincipal,
                executor: async (_, context, ct) =>
                {
                    context.SetStage(OperationStage.Resolving);
                    context.ReportProgress(new OperationProgress(false, 0, 1, "steps", null, null, null));

                    // CreateInstance dual-path remains the authoritative installer boundary for P1.
                    // Provisioning wraps it as the only Operation-creating entrypoint.
                    var created = await instances.CreateInstanceAsync(new CreateInstanceRequest(factory), ct)
                        .ConfigureAwait(false);
                    if (created.IsErr(out var createError))
                        return Result.Err<string, DaemonError>(createError!);

                    context.SetStage(OperationStage.Finalizing);
                    context.ReportProgress(new OperationProgress(false, 1, 1, "steps", null, null, null));
                    return Result.Ok<string, DaemonError>(created.Unwrap().Config.InstanceId.ToString("D"));
                },
                cancellationToken: cancellationToken,
                terminalCommit: _ => planKernel.CompleteAcceptedExecute(plan.PlanId)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StartAsync throws only before durable operation acceptance. Re-open the plan because
            // no executor can now consume the Executing claim.
            planKernel.AbortExecuteAdmission(plan.PlanId);
            throw;
        }

        if (execute.IsErr(out var executeError))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<ProvisioningExecuteResult, DaemonError>(executeError!);
        }

        var snapshot = execute.Unwrap();
        return Result.Ok<ProvisioningExecuteResult, DaemonError>(
            new ProvisioningExecuteResult(plan.PlanId, snapshot.OperationId));
    }

    private static InstanceFactoryConfiguration BuildFactoryConfiguration(
        ProvisioningPlanSnapshot plan,
        InstanceType instanceType)
    {
        var emptyRules = JsonDocument.Parse("{}").RootElement.Clone();
        var config = new InstanceConfiguration(
            instanceId: Guid.NewGuid(),
            name: plan.InstanceName,
            target: "server.jar",
            instanceType: instanceType,
            targetType: TargetType.Jar,
            version: plan.MinecraftVersion,
            inputEncoding: "utf-8",
            outputEncoding: "utf-8",
            javaPath: string.IsNullOrWhiteSpace(plan.JavaPath) ? "java" : plan.JavaPath!,
            arguments: ImmutableArray<string>.Empty,
            environmentVariables: ImmutableDictionary<string, string>.Empty,
            eventRules: emptyRules);

        return new InstanceFactoryConfiguration(
            Configuration: config,
            Source: plan.Source!,
            SourceType: SourceType.Core,
            Mirror: plan.Mirror,
            UsePostProcess: true);
    }

    private static bool TryMapProvider(ProvisioningProviderKind provider, out InstanceType instanceType)
    {
        instanceType = provider switch
        {
            ProvisioningProviderKind.Vanilla => InstanceType.MCVanilla,
            ProvisioningProviderKind.Paper => InstanceType.MCPaper,
            ProvisioningProviderKind.Fabric => InstanceType.MCFabric,
            ProvisioningProviderKind.Forge => InstanceType.MCForge,
            ProvisioningProviderKind.NeoForge => InstanceType.MCNeoForge,
            ProvisioningProviderKind.Quilt => InstanceType.MCQuilt,
            _ => default,
        };
        return provider is ProvisioningProviderKind.Vanilla
            or ProvisioningProviderKind.Paper
            or ProvisioningProviderKind.Fabric
            or ProvisioningProviderKind.Forge
            or ProvisioningProviderKind.NeoForge
            or ProvisioningProviderKind.Quilt;
    }
}
