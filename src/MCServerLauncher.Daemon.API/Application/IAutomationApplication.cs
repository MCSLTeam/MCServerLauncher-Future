using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Application;

/// <summary>
/// Typed operational policies: the applied document with compare-and-swap versioning, structural
/// validation, a dry run against recorded facts, and the confirm/execute pair for service-filed
/// automation intent plans.
/// </summary>
public interface IAutomationApplication
{
    Task<Result<AutomationGetResult, DaemonError>> GetAsync(
        CancellationToken cancellationToken);

    Task<Result<AutomationValidateResult, DaemonError>> ValidateAsync(
        AutomationValidateRequest request,
        CancellationToken cancellationToken);

    Task<Result<AutomationTestResult, DaemonError>> TestAsync(
        CancellationToken cancellationToken);

    Task<Result<AutomationApplyResult, DaemonError>> ApplyAsync(
        AutomationApplyRequest request,
        CancellationToken cancellationToken);

    Task<Result<AutomationApplyResult, DaemonError>> EnableAsync(
        AutomationEnableRequest request,
        CancellationToken cancellationToken);

    Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmIntentAsync(
        AutomationIntentConfirmRequest request,
        CancellationToken cancellationToken);

    Task<Result<AutomationIntentExecuteResult, DaemonError>> ExecuteIntentAsync(
        AutomationIntentExecuteRequest request,
        CancellationToken cancellationToken);
}
