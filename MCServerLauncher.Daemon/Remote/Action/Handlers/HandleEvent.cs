using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.SubscribeEvent, "*")]
internal class HandleSubscribeEvent : IActionHandler<SubscribeEventParameter, SubscribeEventResult>
{
    public Result<SubscribeEventResult, ActionError> Handle(SubscribeEventParameter param, WsContext ctx,
        IResolver resolver, CancellationToken ct)
    {
        return ResultExt.Try(wsCtx =>
                wsCtx.SubscribeEvent(param.Type,
                    param.Type.GetEventFilter(param.Filter, DaemonJsonSettings.Settings)
                ), ctx).Map(id => new SubscribeEventResult { Subscriber = id }
            ).MapErr(ex =>
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
        return ResultExt.Try(
                wsCtx => wsCtx.UnsubscribeEvent(param.Subscriber),
                ctx)
            .Map(unit => ActionHandlerExtensions.EmptyActionResult)
            .MapErr(ex =>
                ActionRetcode.ParamError
                    .WithMessage($"Try to unsubscribe a unknown event (subscriber = {param.Subscriber})").ToError()
                    .CauseBy(ex)
            );
    }
}