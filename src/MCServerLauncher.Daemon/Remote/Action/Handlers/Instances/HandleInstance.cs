using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.ApplicationCore;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Action.Handlers;

[ActionHandler(ActionType.StartInstance, "*")]
internal class HandleStartInstance : IAsyncActionHandler<StartInstanceParameter, EmptyActionResult>
{
    public async Task<Result<EmptyActionResult, ActionError>> HandleAsync(
        StartInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = await resolver.GetRequiredService<IInstanceApplication>()
            .StartInstanceAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct);
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.ProcessError))
            : this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.StopInstance, "*")]
internal class HandleStopInstance : IActionHandler<StopInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        StopInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .StopInstanceAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.BadInstanceState))
            : this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.SendToInstance, "*")]
internal class HandleSendToInstance : IActionHandler<SendToInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        SendToInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .SendCommandAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceCommandRequest(param.Id, param.Message), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.BadInstanceState))
            : this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.GetAllReports, "*")]
internal class HandleGetAllReports : IAsyncActionHandler<EmptyActionParameter, GetAllReportsResult>
{
    public async Task<Result<GetAllReportsResult, ActionError>> HandleAsync(
        EmptyActionParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = await resolver.GetRequiredService<IInstanceApplication>().ListInstanceReportsAsync(ct);
        if (result.IsErr(out var error))
        {
            return this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.InstanceActionError));
        }

        return this.Ok(new GetAllReportsResult
        {
            Reports = result.Unwrap().Reports.ToDictionary(
                pair => pair.Key,
                pair => LegacyInstanceActionMapper.ToLegacy(pair.Value))
        });
    }
}

[ActionHandler(ActionType.AddInstance, "*")]
internal class HandleAddInstance : IAsyncActionHandler<AddInstanceParameter, AddInstanceResult>
{
    public async Task<Result<AddInstanceResult, ActionError>> HandleAsync(
        AddInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var request = new MCServerLauncher.Common.Contracts.Instances.CreateInstanceRequest(
            new MCServerLauncher.Common.Contracts.Instances.InstanceFactoryConfiguration(
                LegacyInstanceActionMapper.ToContract(param.Setting),
                param.Setting.Source,
                param.Setting.SourceType,
                param.Setting.Mirror,
                param.Setting.UsePostProcess));
        var result = await resolver.GetRequiredService<IInstanceApplication>().CreateInstanceAsync(request, ct);
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.InstallationError))
            : this.Ok(new AddInstanceResult { Config = LegacyInstanceActionMapper.ToLegacy(result.Unwrap().Config) });
    }
}

[ActionHandler(ActionType.RemoveInstance, "*")]
internal class HandleRemoveInstance : IActionHandler<RemoveInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        RemoveInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .RemoveInstanceAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.BadInstanceState))
            : this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.KillInstance, "*")]
internal class HandleKillInstance : IActionHandler<KillInstanceParameter, EmptyActionResult>
{
    public Result<EmptyActionResult, ActionError> Handle(
        KillInstanceParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .HaltInstanceAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.ProcessError))
            : this.Ok(ActionHandlerExtensions.EmptyActionResult);
    }
}

[ActionHandler(ActionType.GetInstanceReport, "*")]
internal class HandleGetInstanceReport : IAsyncActionHandler<GetInstanceReportParameter, GetInstanceReportResult>
{
    public async Task<Result<GetInstanceReportResult, ActionError>> HandleAsync(
        GetInstanceReportParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = await resolver.GetRequiredService<IInstanceApplication>()
            .GetInstanceReportAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct);
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.InstanceNotFound))
            : this.Ok(new GetInstanceReportResult { Report = LegacyInstanceActionMapper.ToLegacy(result.Unwrap()) });
    }
}

[ActionHandler(ActionType.GetInstanceLogHistory, "*")]
internal class HandleGetInstanceLogHistory : IActionHandler<GetInstanceLogHistoryParameter, GetInstanceLogHistoryResult>
{
    public Result<GetInstanceLogHistoryResult, ActionError> Handle(
        GetInstanceLogHistoryParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .GetInstanceLogAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceLogQuery(param.Id), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.InstanceNotFound))
            : this.Ok(new GetInstanceLogHistoryResult { Logs = result.Unwrap().Logs.ToArray() });
    }
}

[ActionHandler(ActionType.GetInstanceSettings, "*")]
internal class HandleGetInstanceSettings : IActionHandler<GetInstanceSettingsParameter, GetInstanceSettingsResult>
{
    public Result<GetInstanceSettingsResult, ActionError> Handle(
        GetInstanceSettingsParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var result = resolver.GetRequiredService<IInstanceApplication>()
            .GetInstanceSettingsAsync(new MCServerLauncher.Common.Contracts.Instances.InstanceReference(param.Id), ct)
            .GetAwaiter()
            .GetResult();
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.BadRequest))
            : this.Ok(LegacyInstanceActionMapper.ToLegacy(result.Unwrap()));
    }
}

[ActionHandler(ActionType.UpdateInstanceSettings, "*")]
internal class HandleUpdateInstanceSettings : IAsyncActionHandler<UpdateInstanceSettingsParameter, UpdateInstanceSettingsResult>
{
    public async Task<Result<UpdateInstanceSettingsResult, ActionError>> HandleAsync(
        UpdateInstanceSettingsParameter param,
        WsContext ctx,
        IResolver resolver,
        CancellationToken ct)
    {
        var request = new MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest(
            param.Id,
            param.Name,
            param.InstanceType,
            param.JavaPath,
            param.Arguments.ToImmutableArray(),
            param.Version,
            param.ReplacementCore is null
                ? null
                : new MCServerLauncher.Common.Contracts.Instances.InstanceCoreReplacementRequest(
                    param.ReplacementCore.UploadedSourcePath,
                    param.ReplacementCore.PreferredTargetName),
            param.ForceRerunInstaller);
        var result = await resolver.GetRequiredService<IInstanceApplication>().UpdateInstanceSettingsAsync(request, ct);
        return result.IsErr(out var error)
            ? this.Err(LegacyActionErrorMapper.ToActionError(error!, ActionRetcode.InstallationError))
            : this.Ok(LegacyInstanceActionMapper.ToLegacy(result.Unwrap()));
    }
}
