using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Utils.LazyCell;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal class HandleMisc : HandleBase
{
    public static ActionHandlerRegistry Register(ActionHandlerRegistry registry)
    {
        return registry
            .Register(
                ActionType.Ping,
                IMatchable.Always(),
                (ctx, resolver, ct) => ValueTaskOk(new PingResult
                {
                    Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }))
            .Register(
                ActionType.GetSystemInfo,
                IMatchable.Always(),
                async (ctx, resolver, ct) => Ok(new GetSystemInfoResult
                    { Info = await resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>().Value })
            )
            .Register(
                ActionType.GetPermissions,
                IMatchable.Always(),
                (ctx, resolver, ct) => ValueTaskOk(new GetPermissionsResult
                {
                    Permissions = ctx.Permissions.PermissionList.Select(p => p.ToString()).ToArray()
                }))
            .Register(
                ActionType.GetJavaList,
                Permission.Of("mcsl.daemon.java_list"),
                async (ctx, resolver, ct) => Ok(new GetJavaListResult
                {
                    JavaList = await resolver.GetRequiredService<IAsyncTimedLazyCell<JavaInfo[]>>().Value
                })
            );
    }
}