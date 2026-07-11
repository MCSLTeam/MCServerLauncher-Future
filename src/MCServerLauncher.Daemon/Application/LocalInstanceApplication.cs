using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalInstanceApplication(IInstanceManager instanceManager) : IInstanceApplication
{
    public async Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
        CreateInstanceRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        InstanceFactorySetting setting;
        try
        {
            setting = InstanceContractMapper.ToLegacy(request.Setting);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return Result.Err<CreateInstanceResult, DaemonError>(
                new ValidationDaemonError("instance.invalid", "The instance configuration is invalid."));
        }

        var validation = setting.ValidateSetting();
        if (validation.IsErr(out _))
        {
            return Result.Err<CreateInstanceResult, DaemonError>(
                new ValidationDaemonError("instance.invalid", "The instance configuration is invalid."));
        }

        try
        {
            var result = await instanceManager.TryAddInstance(setting, cancellationToken);
            if (result.IsErr(out _))
            {
                return Result.Err<CreateInstanceResult, DaemonError>(
                    new StorageDaemonError("instance.create_failed", "The instance could not be created."));
            }

            return Result.Ok<CreateInstanceResult, DaemonError>(
                new CreateInstanceResult(InstanceContractMapper.ToContract(result.Unwrap())));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<CreateInstanceResult, DaemonError>(
                new InternalDaemonError("instance.create_failed", "The instance could not be created."));
        }
    }

    public async Task<Result<Unit, DaemonError>> RemoveInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Result.Err<Unit, DaemonError>(InstanceNotFound(request.InstanceId));
        }

        try
        {
            if (await instanceManager.TryRemoveInstance(request.InstanceId, cancellationToken))
            {
                return Result.Ok<Unit, DaemonError>(Unit.Default);
            }

            DaemonError error = instanceManager.RunningInstances.ContainsKey(request.InstanceId)
                ? new ConflictDaemonError("instance.running", "The instance is running.")
                : new StorageDaemonError("instance.remove_failed", "The instance could not be removed.");
            return Result.Err<Unit, DaemonError>(error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.remove_failed", "The instance could not be removed."));
        }
    }

    public async Task<Result<Unit, DaemonError>> StartInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Result.Err<Unit, DaemonError>(InstanceNotFound(request.InstanceId));
        }

        if (instanceManager.RunningInstances.ContainsKey(request.InstanceId))
        {
            return Result.Err<Unit, DaemonError>(
                new ConflictDaemonError("instance.already_running", "The instance is already running."));
        }

        try
        {
            var instance = await instanceManager.TryStartInstance(request.InstanceId, cancellationToken);
            if (instance is not null)
                return Result.Ok<Unit, DaemonError>(Unit.Default);

            return instanceManager.RunningInstances.ContainsKey(request.InstanceId)
                ? Result.Err<Unit, DaemonError>(
                    new ConflictDaemonError("instance.already_running", "The instance is already running."))
                : Result.Err<Unit, DaemonError>(
                    new InternalDaemonError("instance.start_failed", "The instance process could not be started."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.start_failed", "The instance process could not be started."));
        }
    }

    public async Task<Result<Unit, DaemonError>> StopInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Result.Err<Unit, DaemonError>(InstanceNotFound(request.InstanceId));
        }

        try
        {
            return await instanceManager.TryStopInstance(request.InstanceId, cancellationToken)
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(
                    new ConflictDaemonError("instance.not_running", "The instance is not running."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.stop_failed", "The instance could not be stopped."));
        }
    }

    public Task<Result<Unit, DaemonError>> HaltInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // KillInstance is a fire-and-forget signal: missing instance or process is success.
            instanceManager.KillInstance(request.InstanceId);
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }
        catch (Exception)
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.halt_failed", "The instance could not be halted.")));
        }
    }

    public Task<Result<Unit, DaemonError>> SendCommandAsync(
        InstanceCommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                InstanceNotFound(request.InstanceId)));
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("instance.command_empty", "The instance command cannot be empty.")));
        }

        try
        {
            return Task.FromResult(instanceManager.SendToInstance(request.InstanceId, request.Command)
                ? Result.Ok<Unit, DaemonError>(Unit.Default)
                : Result.Err<Unit, DaemonError>(
                    new ConflictDaemonError("instance.not_running", "The instance is not running.")));
        }
        catch (Exception)
        {
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.command_failed", "The command could not be sent to the instance.")));
        }
    }

    public async Task<Result<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>> GetInstanceReportAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var report = await instanceManager.GetInstanceReport(request.InstanceId, cancellationToken);
            if (report is null)
            {
                return Result.Err<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>(
                    InstanceNotFound(request.InstanceId));
            }

            var processId = instanceManager.Instances.TryGetValue(request.InstanceId, out var instance)
                ? instance.ServerProcessId
                : -1;
            return Result.Ok<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>(
                InstanceContractMapper.ToContract(report, processId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<MCServerLauncher.Common.Contracts.Instances.InstanceReport, DaemonError>(
                new InternalDaemonError("instance.report_failed", "The instance report could not be read."));
        }
    }

    public async Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var reports = await instanceManager.GetAllReports(cancellationToken);
            var mapped = reports.ToImmutableDictionary(
                pair => pair.Key,
                pair => InstanceContractMapper.ToContract(
                    pair.Value,
                    instanceManager.Instances.TryGetValue(pair.Key, out var instance) ? instance.ServerProcessId : -1));
            return Result.Ok<InstanceReportList, DaemonError>(new InstanceReportList(mapped));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<InstanceReportList, DaemonError>(
                new InternalDaemonError("instance.reports_failed", "Instance reports could not be read."));
        }
    }

    public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(
        InstanceLogQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!instanceManager.TryGetInstanceLog(request.InstanceId, out var logs))
        {
            return Task.FromResult(Result.Err<InstanceLogResult, DaemonError>(
                InstanceNotFound(request.InstanceId)));
        }

        return Task.FromResult(Result.Ok<InstanceLogResult, DaemonError>(
            new InstanceLogResult(logs.ToImmutableArray())));
    }

    public async Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await instanceManager.GetInstanceSettings(request.InstanceId, cancellationToken);
            return result.IsErr(out _)
                ? Result.Err<InstanceSettingsResult, DaemonError>(InstanceNotFound(request.InstanceId))
                : Result.Ok<InstanceSettingsResult, DaemonError>(InstanceContractMapper.ToContract(result.Unwrap()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<InstanceSettingsResult, DaemonError>(
                new InternalDaemonError("instance.settings_failed", "The instance settings could not be read."));
        }
    }

    public async Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(
        UpdateInstanceSettingsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!instanceManager.Instances.TryGetValue(request.InstanceId, out var instance))
        {
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(InstanceNotFound(request.InstanceId));
        }

        if (instance.Status is not InstanceStatus.Stopped and not InstanceStatus.Crashed)
        {
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                new ConflictDaemonError("instance.running", "The instance must be stopped before updating settings."));
        }

        MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsParameter parameter;
        try
        {
            parameter = InstanceContractMapper.ToLegacy(request);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                new ValidationDaemonError("instance.settings_invalid", "The instance settings are invalid."));
        }

        if (request.ReplacementCore is not null &&
            !InstanceTargetPathValidator.TryResolveTargetFile(
                instance.Config.GetWorkingDirectory(),
                request.ReplacementCore.PreferredTargetName ?? Path.GetFileName(request.ReplacementCore.UploadedSourcePath),
                out _,
                out _))
        {
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                new ValidationDaemonError("instance.settings_invalid", "The instance settings are invalid."));
        }

        try
        {
            var result = await instanceManager.UpdateInstanceSettings(parameter, cancellationToken);
            if (result.IsErr(out var updateError))
            {
                return updateError is InstancePathValidationError
                    ? Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                        new ValidationDaemonError("instance.settings_invalid", "The instance settings are invalid."))
                    : Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                        new StorageDaemonError("instance.settings_persist_failed", "The instance settings could not be updated."));
            }

            return Result.Ok<UpdateInstanceSettingsResult, DaemonError>(
                InstanceContractMapper.ToContract(result.Unwrap()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                new InternalDaemonError("instance.settings_failed", "The instance settings could not be updated."));
        }
    }

    private static NotFoundDaemonError InstanceNotFound(Guid instanceId)
    {
        return new NotFoundDaemonError("instance.not_found", $"Instance '{instanceId}' was not found.");
    }
}
