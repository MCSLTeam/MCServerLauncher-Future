using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.GetEventRules, "*")]
internal class HandleGetEventRules : IActionHandler<GetEventRulesParameter, GetEventRulesResult>
{
    public Result<GetEventRulesResult, ActionError> Handle(GetEventRulesParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        if (!instanceManager.Instances.TryGetValue(param.InstanceId, out var instance))
        {
            return this.Err(ActionRetcode.InstanceNotFound.WithMessage(param.InstanceId));
        }

        return this.Ok(new GetEventRulesResult
        {
            Rules = instance.Config.EventRules
        });
    }
}

[ActionHandler(ActionType.SaveEventRules, "*")]
internal class HandleSaveEventRules : IActionHandler<SaveEventRulesParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(SaveEventRulesParameter param, WsContext ctx, IResolver resolver, CancellationToken ct)
    {
        var instanceManager = resolver.GetRequiredService<IInstanceManager>();
        if (!instanceManager.Instances.TryGetValue(param.InstanceId, out var instance))
        {
            return this.Err(ActionRetcode.InstanceNotFound.WithMessage(param.InstanceId));
        }

        instance.Config.EventRules.Clear();
        instance.Config.EventRules.AddRange(param.Rules);
        
        // Save config to file
        var configPath = System.IO.Path.Combine(instance.Config.GetWorkingDirectory(), InstanceConfig.FileName);
        MCServerLauncher.Daemon.Storage.FileManager.WriteJsonAndBackup(configPath, instance.Config);

        return this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}
