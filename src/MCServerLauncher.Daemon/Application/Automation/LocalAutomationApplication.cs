using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore.Automation;

internal sealed class LocalAutomationApplication(
    AutomationPolicyStore store,
    AutomationEvaluator evaluator,
    PlanKernel planKernel,
    IOperationApplication operations) : IAutomationApplication
{
    private OperationCoordinator Coordinator =>
        operations as OperationCoordinator
        ?? throw new InvalidOperationException("Automation requires OperationCoordinator as IOperationApplication.");

    public Task<Result<AutomationGetResult, DaemonError>> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok<AutomationGetResult, DaemonError>(
            new AutomationGetResult(store.Get()) { Suppressions = evaluator.ActiveSuppressions() }));
    }

    public Task<Result<AutomationValidateResult, DaemonError>> ValidateAsync(
        AutomationValidateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request.PolicySet);
        return Task.FromResult(Result.Ok<AutomationValidateResult, DaemonError>(
            new AutomationValidateResult(AutomationPolicyValidator.Validate(request.PolicySet))));
    }

    public Task<Result<AutomationTestResult, DaemonError>> TestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok<AutomationTestResult, DaemonError>(
            new AutomationTestResult(evaluator.Test())));
    }

    public Task<Result<AutomationApplyResult, DaemonError>> ApplyAsync(
        AutomationApplyRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request.PolicySet);
        var applied = store.Apply(request.PolicySet);
        return Task.FromResult(applied.IsErr(out var error)
            ? Result.Err<AutomationApplyResult, DaemonError>(error!)
            : Result.Ok<AutomationApplyResult, DaemonError>(new AutomationApplyResult(applied.Unwrap())));
    }

    public Task<Result<AutomationApplyResult, DaemonError>> EnableAsync(
        AutomationEnableRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var toggled = store.Enable(request.PolicyId, request.Enabled, request.ExpectedVersion);
        return Task.FromResult(toggled.IsErr(out var error)
            ? Result.Err<AutomationApplyResult, DaemonError>(error!)
            : Result.Ok<AutomationApplyResult, DaemonError>(new AutomationApplyResult(toggled.Unwrap())));
    }

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmIntentAsync(
        AutomationIntentConfirmRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = planKernel.Get(request.PlanId);
        if (current.IsOk(out var snapshot) &&
            !string.Equals(snapshot!.Kind, AutomationIntents.PlanKind, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Err<ProvisioningPlanSnapshot, DaemonError>(
                new ValidationDaemonError("plan.kind_mismatch", "The plan is not an automation intent.")));
        }

        return Task.FromResult(planKernel.Confirm(request.PlanId, request.PlanHash, request.ConfirmerPrincipal));
    }

    public async Task<Result<AutomationIntentExecuteResult, DaemonError>> ExecuteIntentAsync(
        AutomationIntentExecuteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var begin = planKernel.TryBeginExecute(request.PlanId, request.ExecutorPrincipal);
        if (begin.IsErr(out var beginError))
            return Result.Err<AutomationIntentExecuteResult, DaemonError>(beginError!);

        var plan = begin.Unwrap();
        if (!string.Equals(plan.Kind, AutomationIntents.PlanKind, StringComparison.Ordinal))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<AutomationIntentExecuteResult, DaemonError>(
                new ValidationDaemonError("plan.kind_mismatch", "The plan is not an automation intent."));
        }

        if (!AutomationIntents.TryParsePayload(plan.Payload, out var payload, out var deferred))
        {
            planKernel.AbortExecuteAdmission(plan.PlanId);
            return Result.Err<AutomationIntentExecuteResult, DaemonError>(
                new ValidationDaemonError("automation.plan_payload_invalid", "The intent plan payload is invalid."));
        }

        Result<OperationSnapshot, DaemonError> execute;
        try
        {
            execute = await Coordinator.StartAsync(
                kind: AutomationIntents.ExecuteOperationKind,
                target: plan.PlanId.ToString("D"),
                ownerPrincipal: request.ExecutorPrincipal,
                executor: (_, _, ct) => evaluator.ExecuteDeferredAsync(deferred, payload.TargetInstanceId, ct),
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
            return Result.Err<AutomationIntentExecuteResult, DaemonError>(executeError!);
        }

        return Result.Ok<AutomationIntentExecuteResult, DaemonError>(
            new AutomationIntentExecuteResult(plan.PlanId, execute.Unwrap().OperationId));
    }
}

/// <summary>
/// The automation confirmation-plan shape: filing, payload serialization, and parsing. The plan
/// hash canonically covers the payload, so the human confirms the exact deferred action.
/// </summary>
internal static class AutomationIntents
{
    internal const string PlanKind = "automation.intent";
    internal const string ExecuteOperationKind = "automation.intent.execute";

    internal static readonly ImmutableArray<string> IntentPermissions =
        ["mcsl.automation.intent.confirm", "mcsl.automation.intent.execute"];

    internal static Result<string, DaemonError> FilePlan(
        PlanKernel planKernel,
        AutomationPolicy policy,
        ConfirmationPlanAction confirmation,
        Guid? targetInstanceId)
    {
        if (confirmation.Deferred is null or ConfirmationPlanAction)
        {
            return Result.Err<string, DaemonError>(new ValidationDaemonError(
                "automation.deferred_invalid",
                "The confirmation plan needs a non-nested deferred action."));
        }

        var payload = SerializePayload(new AutomationIntentPayload(
            policy.Id,
            targetInstanceId,
            SerializeAction(confirmation.Deferred)));
        var put = planKernel.Put(
            kind: PlanKind,
            riskClass: PlanRiskClass.Sensitive,
            requiredPermissions: IntentPermissions,
            requiresConfirmation: true,
            creatorPrincipal: AutomationEvaluator.ServicePrincipalSubject,
            unresolved: [],
            // One active plan per policy: a policy that keeps firing must not flood the kernel.
            idempotencyKey: $"automation.intent:{policy.Id:D}",
            expiry: null,
            materialize: (planId, planHash, createdAt, expiresAt, _) => new ProvisioningPlanSnapshot(
                PlanId: planId,
                PlanHash: planHash,
                Kind: PlanKind,
                Status: PlanStatus.Blocked,
                RiskClass: PlanRiskClass.Sensitive,
                RequiredPermissions: IntentPermissions,
                RequiresConfirmation: true,
                CreatorPrincipal: AutomationEvaluator.ServicePrincipalSubject,
                CreatedAt: createdAt,
                ExpiresAt: expiresAt,
                Unresolved: [],
                Provider: ProvisioningProviderKind.Vanilla,
                InstanceName: targetInstanceId?.ToString("D") ?? "-",
                MinecraftVersion: "-",
                Source: policy.Id.ToString("D"),
                Mirror: Common.ProtoType.Instance.InstanceFactoryMirror.None,
                JavaPath: null,
                IdempotencyKey: null,
                Payload: payload));
        return put.IsErr(out var error)
            ? Result.Err<string, DaemonError>(error!)
            : Result.Ok<string, DaemonError>(put.Unwrap().PlanId.ToString("D"));
    }

    internal static bool TryParsePayload(
        JsonElement payload,
        out AutomationIntentPayload parsed,
        out AutomationAction deferred)
    {
        try
        {
            var value = payload.Deserialize(AutomationPlanPayloadJsonContext.Default.AutomationIntentPayload);
            if (value is { PolicyId: var policyId } && policyId != Guid.Empty)
            {
                var action = value.Action.Deserialize(ApplicationContractJsonContext.Default.AutomationAction);
                if (action is not null and not ConfirmationPlanAction)
                {
                    parsed = value;
                    deferred = action;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        parsed = default!;
        deferred = default!;
        return false;
    }

    private static JsonElement SerializeAction(AutomationAction action)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, action, ApplicationContractJsonContext.Default.AutomationAction);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement SerializePayload(AutomationIntentPayload payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, payload, AutomationPlanPayloadJsonContext.Default.AutomationIntentPayload);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

/// <summary>
/// Typed intent-plan payload; the plan hash canonically covers it, so confirmation binds the
/// exact deferred action and target.
/// </summary>
internal sealed record AutomationIntentPayload(Guid PolicyId, Guid? TargetInstanceId, JsonElement Action);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AutomationIntentPayload))]
internal partial class AutomationPlanPayloadJsonContext : JsonSerializerContext;
