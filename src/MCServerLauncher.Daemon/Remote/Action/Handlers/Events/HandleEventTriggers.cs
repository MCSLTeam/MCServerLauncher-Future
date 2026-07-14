using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.GetEventRules, "*")]
internal sealed class HandleGetEventRules : IActionHandler<GetEventRulesParameter, GetEventRulesResult>
{
    public Result<GetEventRulesResult, ActionError> Handle(
        GetEventRulesParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var application = resolver.GetRequiredService<IDaemonApplication>();
        var result = application.EventRules.GetEventRulesAsync(new EventRuleQuery(param.InstanceId), ct)
            .GetAwaiter()
            .GetResult();
        if (result.IsErr(out var error))
            return this.Err(error is null ? ActionRetcode.UnexpectedError : ToActionRetcode(error));

        var ruleSet = result.Unwrap();
        try
        {
            var rules = EventRuleDocumentCodec.Deserialize(ruleSet.Rules);
            return rules is null
                ? this.Err(ActionRetcode.ParamError.WithMessage("Event rules were null"))
                : this.Ok(new GetEventRulesResult { Rules = rules });
        }
        catch (JsonException)
        {
            return this.Err(ActionRetcode.ParamError.WithMessage("Event rules were malformed"));
        }
    }

    private static ActionRetcode ToActionRetcode(DaemonError error)
    {
        return error.Kind switch
        {
            DaemonErrorKind.NotFound => ActionRetcode.InstanceNotFound.WithMessage(error.Message),
            DaemonErrorKind.Validation => ActionRetcode.ParamError.WithMessage(error.Message),
            DaemonErrorKind.Storage => ActionRetcode.FileError.WithMessage(error.Message),
            _ => ActionRetcode.UnexpectedError.WithMessage(error.Message)
        };
    }
}

[ActionHandler(ActionType.SaveEventRules, "*")]
internal sealed class HandleSaveEventRules : IActionHandler<SaveEventRulesParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        SaveEventRulesParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var application = resolver.GetRequiredService<IDaemonApplication>();
        var rules = EventRuleDocumentCodec.SerializeToElement(param.Rules);
        var result = application.EventRules.UpdateEventRulesAsync(new EventRuleUpdateRequest(param.InstanceId, rules), ct)
            .GetAwaiter()
            .GetResult();
        if (result.IsErr(out var error))
            return this.Err(error is null ? ActionRetcode.UnexpectedError : ToActionRetcode(error));

        return this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }

    private static ActionRetcode ToActionRetcode(DaemonError error)
    {
        return error.Kind switch
        {
            DaemonErrorKind.NotFound => ActionRetcode.InstanceNotFound.WithMessage(error.Message),
            DaemonErrorKind.Validation => ActionRetcode.ParamError.WithMessage(error.Message),
            DaemonErrorKind.Storage => ActionRetcode.FileError.WithMessage(error.Message),
            _ => ActionRetcode.UnexpectedError.WithMessage(error.Message)
        };
    }
}
