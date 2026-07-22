using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.Daemon.ApplicationCore.Provisioning;

/// <summary>
/// Orders persisted provisioning-plan reconciliation before recovered operations become terminal.
/// The daemon invokes this once before plugins or transport entrypoints can observe startup state.
/// </summary>
internal sealed class OperationStartupRecovery
{
    private readonly object _gate = new();
    private readonly ILogger<OperationStartupRecovery> _logger;
    private int _completed;

    public OperationStartupRecovery(
        IOperationApplication operations,
        PlanKernel plans,
        ILogger<OperationStartupRecovery>? logger = null)
    {
        Operations = operations as OperationCoordinator
            ?? throw new InvalidOperationException(
                "Operation startup recovery requires the daemon-owned OperationCoordinator.");
        Plans = plans;
        _logger = logger ?? NullLogger<OperationStartupRecovery>.Instance;
    }

    internal OperationCoordinator Operations { get; }

    internal PlanKernel Plans { get; }

    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

    internal void Recover()
    {
        lock (_gate)
        {
            if (IsCompleted)
                return;

            // Persist and publish linked plan state first. Only then may any recovered operation
            // publish Interrupted, so observers can never see terminal operation state paired
            // with an Executing plan.
            try
            {
                Plans.ReconcileExecutingPlans(Operations);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(
                    exception,
                    "Operation startup recovery failed while persisting linked plan reconciliation.");
                throw;
            }

            try
            {
                Operations.PublishInterruptedAfterPlanReconciliation();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(
                    exception,
                    "Operation startup recovery failed while persisting interrupted operation state.");
                throw;
            }
            Volatile.Write(ref _completed, 1);
        }
    }
}
