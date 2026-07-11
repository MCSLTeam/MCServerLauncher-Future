using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.ApplicationCore;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.Ping, "*")]
internal class HandlePing : IActionHandler<EmptyActionParameter, PingResult>
{
    public Result<PingResult, ActionError> Handle(
        EmptyActionParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        return this.Ok(new PingResult
        {
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }
}

[ActionHandler(ActionType.GetSystemInfo, "*")]
internal class HandleGetSystemInfo : IAsyncActionHandler<EmptyActionParameter, GetSystemInfoResult>
{
    public async Task<Result<GetSystemInfoResult, ActionError>> HandleAsync(
        EmptyActionParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var systemInfo = await resolver.GetRequiredService<LegacySystemActionAdapter>().GetSystemInfoAsync();
        return this.Ok(new GetSystemInfoResult { Info = systemInfo });
    }
}

[ActionHandler(ActionType.GetPermissions, "*")]
internal class HandleGetPermissions : IActionHandler<EmptyActionParameter, GetPermissionsResult>
{
    public Result<GetPermissionsResult, ActionError> Handle(
        EmptyActionParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        return this.Ok(new GetPermissionsResult
        {
            Permissions = ctx.Permissions.PermissionList.Select(permission => permission.ToString()).ToArray()
        });
    }
}

[ActionHandler(ActionType.GetJavaList, "mcsl.daemon.java_list")]
internal class HandleGetJavaList : IAsyncActionHandler<EmptyActionParameter, GetJavaListResult>
{
    public async Task<Result<GetJavaListResult, ActionError>> HandleAsync(
        EmptyActionParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var javaRuntimes = await resolver.GetRequiredService<LegacySystemActionAdapter>().ListJavaRuntimesAsync();
        return this.Ok(new GetJavaListResult { JavaList = javaRuntimes });
    }
}
