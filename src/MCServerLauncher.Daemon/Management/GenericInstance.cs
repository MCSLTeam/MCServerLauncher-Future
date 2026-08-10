using System.Diagnostics;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management;

public class GenericInstance : InstanceBase
{
    public GenericInstance(InstanceConfig config) : base(config)
    {
    }

    internal GenericInstance(
        InstanceConfig config,
        Func<InstanceConfig, CancellationToken, Task<Result<ProcessStartInfo, DaemonError>>> startInfoFactory)
        : base(
            config,
            startInfoFactory,
            static (startInfo, instanceType, consoleMode) => new Communicate.InstanceProcess(startInfo, instanceType, consoleMode))
    {
    }
}
