using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.Ping, "*")]
class HandlePing : IActionHandler<EmptyActionParameter, PingResult>
{
    public Result<PingResult, ActionError> Handle(EmptyActionParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return HandleBase.Ok(new PingResult
        {
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }
}

[ActionHandler(ActionType.GetSystemInfo, "*")]
class HandleGetSystemInfo : IAsyncActionHandler<EmptyActionParameter, GetSystemInfoResult>
{
    public async Task<Result<GetSystemInfoResult, ActionError>> HandleAsync(EmptyActionParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return HandleBase.Ok(new GetSystemInfoResult
        { 
            Info = await resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>().Value 
        });
    }
}

[ActionHandler(ActionType.GetPermissions, "*")]
class HandleGetPermissions : IActionHandler<EmptyActionParameter, GetPermissionsResult>
{
    public Result<GetPermissionsResult, ActionError> Handle(EmptyActionParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return HandleBase.Ok(new GetPermissionsResult
        {
            Permissions = ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
        });
    }
}

[ActionHandler(ActionType.GetJavaList, "mcsl.daemon.java_list")]
class HandleGetJavaList : IAsyncActionHandler<EmptyActionParameter, GetJavaListResult>
{
    public async Task<Result<GetJavaListResult, ActionError>> HandleAsync(EmptyActionParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        return HandleBase.Ok(new GetJavaListResult
        {
            JavaList = await resolver.GetRequiredService<IAsyncTimedLazyCell<JavaInfo[]>>().Value
        });
    }
}