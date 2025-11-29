using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

internal class HandleInstance : HandleBase
{
    public static ActionHandlerRegistry Register(ActionHandlerRegistry registry)
    {
        return registry
            .Register<StartInstanceParameter>(
                ActionType.StartInstance,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    var eventService = resolver.GetRequiredService<IEventService>();
                    var instance = await instanceManager.TryStartInstance(param.Id);

                    if (instance is null)
                        return Err(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.ProcessError.WithMessage("Cannot start instance process")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));


                    instance.OnLog -= eventService.OnInstanceLog;
                    instance.OnLog += eventService.OnInstanceLog;

                    return Ok();
                }
            )
            .Register<StopInstanceParameter>(
                ActionType.StopInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryStopInstance(param.Id)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                })
            .Register<SendToInstanceParameter>(
                ActionType.SendToInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.SendToInstance(param.Id, param.Message)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.Instances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} not running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                }
            ).Register(
                ActionType.GetAllReports,
                IMatchable.Always(),
                async (ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return Ok(new GetAllReportsResult
                    {
                        Reports = await instanceManager.GetAllReports()
                    });
                })
            .Register<AddInstanceParameter, AddInstanceResult>(
                ActionType.AddInstance,
                IMatchable.Always(), async (param, ctx, resolver, ct) =>
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
                })
            .Register<RemoveInstanceParameter>(
                ActionType.RemoveInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return instanceManager.TryRemoveInstance(param.Id)
                        ? ValueTaskOk()
                        : ValueTaskErr(instanceManager.RunningInstances.ContainsKey(param.Id)
                            ? ActionRetcode.BadInstanceState.WithMessage($"{param.Id} is running")
                            : ActionRetcode.InstanceNotFound.WithMessage(param.Id));
                })
            .Register<KillInstanceParameter>(
                ActionType.KillInstance,
                IMatchable.Always(),
                (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    instanceManager.KillInstance(param.Id);
                    return ValueTaskOk();
                })
            .Register<GetInstanceReportParameter, GetInstanceReportResult>(
                ActionType.GetInstanceReport,
                IMatchable.Always(),
                async (param, ctx, resolver, ct) =>
                {
                    var instanceManager = resolver.GetRequiredService<IInstanceManager>();
                    return Ok(new GetInstanceReportResult
                    {
                        Report = await instanceManager.GetInstanceReport(param.Id)
                    });
                });
    }
}