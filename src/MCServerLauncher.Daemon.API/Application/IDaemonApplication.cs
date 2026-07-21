namespace MCServerLauncher.Daemon.API.Application;

public interface IDaemonApplication
{
    IInstanceApplication Instances { get; }

    IFileApplication Files { get; }

    ISystemApplication System { get; }

    IEventRuleApplication EventRules { get; }

    IOperationApplication Operations { get; }

    IProvisioningApplication Provisioning { get; }
}
