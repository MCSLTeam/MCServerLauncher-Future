using MCServerLauncher.Daemon.API.Application;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalDaemonApplication(
    IInstanceApplication instances,
    IFileApplication files,
    ISystemApplication system,
    IEventRuleApplication eventRules) : IDaemonApplication
{
    public IInstanceApplication Instances { get; } = instances;

    public IFileApplication Files { get; } = files;

    public ISystemApplication System { get; } = system;

    public IEventRuleApplication EventRules { get; } = eventRules;
}
