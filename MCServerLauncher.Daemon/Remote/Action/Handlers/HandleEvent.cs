using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.SubscribeEvent, "*")]
internal class HandleSubscribeEvent : IActionHandler<SubscribeEventParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(SubscribeEventParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return ResultExt.Try(wsCtx =>
                wsCtx.SubscribeEvent(param.Type,
                    param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                ), ctx).Map(unit => ActionHandlerExtensions.EmptyActionResult)
            .MapErr(ex =>
                ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
            );
    }
}

[ActionHandler(ActionType.UnsubscribeEvent, "*")]
internal class HandleUnsubscribeEvent : IActionHandler<UnsubscribeEventParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(UnsubscribeEventParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return ResultExt.Try(wsCtx =>
                wsCtx.UnsubscribeEvent(param.Type,
                    param.Type.GetEventMeta(param.Meta, DaemonJsonSettings.Settings)
                ), ctx).Map(unit => ActionHandlerExtensions.EmptyActionResult)
            .MapErr(ex =>
                ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
            );
    }
}