using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Automation;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteAutomationApplication(IRemoteApplicationInvoker invoker) : IAutomationApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<AutomationGetResult, DaemonError>> GetAsync(CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetAutomation, new EmptyRequest(), cancellationToken);

    public Task<Result<AutomationValidateResult, DaemonError>> ValidateAsync(
        AutomationValidateRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ValidateAutomation, request, cancellationToken);

    public Task<Result<AutomationTestResult, DaemonError>> TestAsync(CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.TestAutomation, new EmptyRequest(), cancellationToken);

    public Task<Result<AutomationApplyResult, DaemonError>> ApplyAsync(
        AutomationApplyRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ApplyAutomation, request, cancellationToken);

    public Task<Result<AutomationApplyResult, DaemonError>> EnableAsync(
        AutomationEnableRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.EnableAutomation, request, cancellationToken);

    public Task<Result<ProvisioningPlanSnapshot, DaemonError>> ConfirmIntentAsync(
        AutomationIntentConfirmRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ConfirmAutomationIntent, request, cancellationToken);

    public Task<Result<AutomationIntentExecuteResult, DaemonError>> ExecuteIntentAsync(
        AutomationIntentExecuteRequest request,
        CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ExecuteAutomationIntent, request, cancellationToken);
}
