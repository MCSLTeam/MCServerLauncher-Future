using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal class HandleEvent : HandleBase
{
    public static ActionHandlerRegistry Register(ActionHandlerRegistry registry)
    {
        return registry
            .Register<SubscribeEventParameter>(
                ActionType.SubscribeEvent,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(wsCtx =>
                        wsCtx.SubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                        ), ctx).MapErr(ex =>
                        ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
                    ))
            )
            .Register<UnsubscribeEventParameter>(
                ActionType.UnsubscribeEvent,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                    ValueTask.FromResult(ResultExt.Try(wsCtx =>
                        wsCtx.UnsubscribeEvent(param.Type,
                            param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                        ), ctx).MapErr(ex =>
                        ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
                    ))
            );
    }
}