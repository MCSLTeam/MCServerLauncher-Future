using MCServerLauncher.Daemon.API.Application;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalDaemonApplication(
    IInstanceApplication instances,
    IFileApplication files,
    ISystemApplication system,
    IEventRuleApplication eventRules,
    IOperationApplication operations,
    IProvisioningApplication provisioning) : IDaemonApplication
{
    public IInstanceApplication Instances { get; } = instances;

    public IFileApplication Files { get; } = files;

    public ISystemApplication System { get; } = system;

    public IEventRuleApplication EventRules { get; } = eventRules;

    public IOperationApplication Operations { get; } = operations;

    public IProvisioningApplication Provisioning { get; } = provisioning;
}
