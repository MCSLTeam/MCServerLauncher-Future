using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteEventRuleApplication(IRemoteApplicationInvoker invoker) : IEventRuleApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(EventRuleQuery request, CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetInstanceEventRules, request, cancellationToken);

    public Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(EventRuleUpdateRequest request, CancellationToken cancellationToken) =>
        _invoker.InvokeUnitAsync(BuiltInProtocolDefinitions.UpdateInstanceEventRules, request, cancellationToken);
}
