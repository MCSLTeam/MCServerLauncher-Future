using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.StartInstance, "*")]
class HandleStartInstance : IAsyncActionHandler<StartInstanceParameter, EmptyActionResult>
{
    public async Task<Result<EmptyActionResult, ActionError>> HandleAsync(StartInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        var eventService = resolver.GetRequiredService<IEventService>();
        var instance = await instanceManager.TryStartInstance(param.Id);

        if (instance is null)
            return HandleBase.Err<EmptyActionResult>(instanceManager.Instances.ContainsKey(param.Id)
                ? ActionRetcode.ProcessError.WithMessage("Cannot start instance process")
                : ActionRetcode.InstanceNotFound.WithMessage(param.Id));

        instance.OnLog -= eventService.OnInstanceLog;
        instance.OnLog += eventService.OnInstanceLog;

        return HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.StopInstance, "*")]
class HandleStopInstance : IActionHandler<StopInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(StopInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        return instanceManager.TryStopInstance(param.Id)
            ? HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult)
            : HandleBase.Err<EmptyActionResult>(instanceManager.Instances.ContainsKey(param.Id)
                ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
    }
}

[ActionHandler(ActionType.SendToInstance, "*")]
class HandleSendToInstance : IActionHandler<SendToInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(SendToInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        return instanceManager.SendToInstance(param.Id, param.Message)
            ? HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult)
            : HandleBase.Err<EmptyActionResult>(instanceManager.Instances.ContainsKey(param.Id)
                ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
    }
}

[ActionHandler(ActionType.GetAllReports, "*")]
class HandleGetAllReports : IAsyncActionHandler<EmptyActionParameter, GetAllReportsResult>
{
    public async Task<Result<GetAllReportsResult, ActionError>> HandleAsync(EmptyActionParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        return HandleBase.Ok(new GetAllReportsResult
        {
            Reports = await instanceManager.GetAllReports()
        });
    }
}

[ActionHandler(ActionType.AddInstance, "*")]
class HandleAddInstance : IAsyncActionHandler<AddInstanceParameter, AddInstanceResult>
{
    public async Task<Result<AddInstanceResult, ActionError>> HandleAsync(AddInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();

        var validateSettingResult = param.Setting.ValidateSetting().MapErr(innerErr =>
            new ActionError(ActionRetcode.InstallationError.WithMessage("Invalid instance factory setting"))
                .WithInner(innerErr));

        var addInstanceResult =
            (await validateSettingResult.MapAsTaskAsync(async _ =>
            {
                var tryAddInstance = await instanceManager.TryAddInstance(param.Setting);
                return tryAddInstance.MapErr(err =>
                    new ActionError(ActionRetcode.InstallationError).WithInner(err));
            })).Flatten();

        return addInstanceResult.Map(config => new AddInstanceResult
        {
            Config = config
        });
    }
}

[ActionHandler(ActionType.RemoveInstance, "*")]
class HandleRemoveInstance : IActionHandler<RemoveInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(RemoveInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        return instanceManager.TryRemoveInstance(param.Id)
            ? HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult)
            : HandleBase.Err<EmptyActionResult>(instanceManager.RunningInstances.ContainsKey(param.Id)
                ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} is running")
                : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
    }
}

[ActionHandler(ActionType.KillInstance, "*")]
class HandleKillInstance : IActionHandler<KillInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(KillInstanceParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        instanceManager.KillInstance(param.Id);
        return HandleBase.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.GetInstanceReport, "*")]
class HandleGetInstanceReport : IAsyncActionHandler<GetInstanceReportParameter, GetInstanceReportResult>
{
    public async Task<Result<GetInstanceReportResult, ActionError>> HandleAsync(GetInstanceReportParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        return HandleBase.Ok(new GetInstanceReportResult
        {
            Report = await instanceManager.GetInstanceReport(param.Id)
        });
    }
}