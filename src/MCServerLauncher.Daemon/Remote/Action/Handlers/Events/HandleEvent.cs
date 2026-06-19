using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Daemon.Serialization;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using System.Text.Json;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
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
                    HandleEventMetaAdapter.GetEventMeta(param.Type, param.Meta)
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
                    HandleEventMetaAdapter.GetEventMeta(param.Type, param.Meta)
                ), ctx).Map(unit => ActionHandlerExtensions.EmptyActionResult)
            .MapErr(ex =>
                ActionRetcode.ParamError.WithMessage($"Event {param.Type} missing meta").ToError().CauseBy(ex)
            );
    }
}

internal static class HandleEventMetaAdapter
{
    public static IEventMeta? GetEventMeta(EventType eventType, JsonElement? meta)
    {
        if (meta is null || meta.Value.ValueKind == JsonValueKind.Null)
            return null;

        return eventType switch
        {
            EventType.InstanceLog => StjJsonSerializer.Deserialize(meta.Value,
                DaemonRpcSerializerContext.Default.InstanceLogEventMeta),
            _ => null
        };
    }
}
