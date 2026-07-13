using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using RustyOptions;

namespace MCServerLauncher.DaemonClient.Application;

internal sealed class RemoteSystemApplication(IRemoteApplicationInvoker invoker) : ISystemApplication
{
    private readonly IRemoteApplicationInvoker _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    public Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.GetSystemInfo, new EmptyRequest(), cancellationToken);

    public Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(CancellationToken cancellationToken) =>
        _invoker.InvokeAsync(BuiltInProtocolDefinitions.ListJavaRuntimes, new EmptyRequest(), cancellationToken);
}
