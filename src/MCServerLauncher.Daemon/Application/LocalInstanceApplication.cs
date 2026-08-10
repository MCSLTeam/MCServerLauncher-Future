using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Plugins;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalInstanceApplication(
    IInstanceManager instanceManager,
    PluginExtensionPointRuntimeRegistry? extensionPointRuntimeRegistry = null) : IInstanceApplication
{
    private readonly ConsoleSessionCoordinator _consoles = new(instanceManager);

    internal ConsoleSessionCoordinator Consoles => _consoles;

    public async Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(
        CreateInstanceRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var pipelineResult = await ApplyInstanceCreatePipelineAsync(request.Setting, cancellationToken).ConfigureAwait(false);
            if (pipelineResult.IsErr(out var pipelineError))
                return Result.Err<CreateInstanceResult, DaemonError>(pipelineError!);

            var setting = pipelineResult.Unwrap();
            var validation = setting.ValidateSetting();
            if (validation.IsErr(out var validationError))
                return Result.Err<CreateInstanceResult, DaemonError>(validationError!);

            var result = await instanceManager.TryAddInstance(setting, cancellationToken);
            if (result.IsErr(out var createError))
                return Result.Err<CreateInstanceResult, DaemonError>(createError!);

            return Result.Ok<CreateInstanceResult, DaemonError>(
                new CreateInstanceResult(result.Unwrap()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance creation failure.");
            return Result.Err<CreateInstanceResult, DaemonError>(
                new InternalDaemonError("instance.create_failed", "The instance could not be created."));
        }
    }

    private async Task<Result<InstanceFactoryConfiguration, DaemonError>> ApplyInstanceCreatePipelineAsync(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken)
    {
        if (extensionPointRuntimeRegistry is null)
            return Result.Ok<InstanceFactoryConfiguration, DaemonError>(setting);

        var context = new PluginInstanceCreatePipelineContext(
            setting.Configuration.InstanceType.ToString(),
            CreatePipelineInputs(setting));
        var pipelineResult = await extensionPointRuntimeRegistry.ExecuteInstanceCreatePipelineAsync(context, cancellationToken)
            .ConfigureAwait(false);
        if (pipelineResult.IsErr(out var pipelineError))
            return Result.Err<InstanceFactoryConfiguration, DaemonError>(pipelineError!);

        return ApplyInstanceCreatePipelineOutputs(setting, pipelineResult.Unwrap());
    }

    private static IReadOnlyDictionary<string, string> CreatePipelineInputs(InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["instanceId"] = config.InstanceId.ToString("D"),
            ["name"] = config.Name,
            ["target"] = config.Target,
            ["instanceType"] = config.InstanceType.ToString(),
            ["targetType"] = config.TargetType.ToString(),
            ["version"] = config.Version,
            ["inputEncoding"] = config.InputEncoding,
            ["outputEncoding"] = config.OutputEncoding,
            ["javaPath"] = config.JavaPath,
            ["consoleMode"] = config.ConsoleMode.ToString(),
            ["source"] = setting.Source,
            ["sourceType"] = setting.SourceType.ToString(),
            ["mirror"] = setting.Mirror.ToString(),
            ["usePostProcess"] = setting.UsePostProcess ? "true" : "false"
        };
    }

    private static Result<InstanceFactoryConfiguration, DaemonError> ApplyInstanceCreatePipelineOutputs(
        InstanceFactoryConfiguration setting,
        IReadOnlyDictionary<string, string> outputs)
    {
        var config = setting.Configuration;
        if (!TryParseEnum(outputs, "consoleMode", config.ConsoleMode, out ConsoleMode consoleMode, out var consoleModeError))
            return Result.Err<InstanceFactoryConfiguration, DaemonError>(consoleModeError!);
        if (!TryParseEnum(outputs, "sourceType", setting.SourceType, out SourceType sourceType, out var sourceTypeError))
            return Result.Err<InstanceFactoryConfiguration, DaemonError>(sourceTypeError!);
        if (!TryParseBool(outputs, "usePostProcess", setting.UsePostProcess, out var usePostProcess, out var boolError))
            return Result.Err<InstanceFactoryConfiguration, DaemonError>(boolError!);

        var updatedConfig = new InstanceConfiguration(
            config.InstanceId,
            GetOutput(outputs, "name", config.Name),
            GetOutput(outputs, "target", config.Target),
            config.InstanceType,
            config.TargetType,
            GetOutput(outputs, "version", config.Version),
            GetOutput(outputs, "inputEncoding", config.InputEncoding),
            GetOutput(outputs, "outputEncoding", config.OutputEncoding),
            GetOutput(outputs, "javaPath", config.JavaPath),
            config.Arguments,
            config.EnvironmentVariables,
            config.EventRules,
            consoleMode);

        return Result.Ok<InstanceFactoryConfiguration, DaemonError>(new InstanceFactoryConfiguration(
            updatedConfig,
            GetOutput(outputs, "source", setting.Source),
            sourceType,
            setting.Mirror,
            usePostProcess));
    }

    private static string GetOutput(IReadOnlyDictionary<string, string> outputs, string key, string fallback) =>
        outputs.TryGetValue(key, out var value) ? value : fallback;

    private static bool TryParseEnum<TEnum>(
        IReadOnlyDictionary<string, string> outputs,
        string key,
        TEnum fallback,
        out TEnum value,
        out DaemonError? error)
        where TEnum : struct, Enum
    {
        if (!outputs.TryGetValue(key, out var text))
        {
            value = fallback;
            error = null;
            return true;
        }

        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out value))
        {
            error = null;
            return true;
        }

        error = new ValidationDaemonError("instance.pipeline_output.invalid", $"Pipeline output '{key}' has an unsupported value.");
        return false;
    }

    private static bool TryParseBool(
        IReadOnlyDictionary<string, string> outputs,
        string key,
        bool fallback,
        out bool value,
        out DaemonError? error)
    {
        if (!outputs.TryGetValue(key, out var text))
        {
            value = fallback;
            error = null;
            return true;
        }

        if (bool.TryParse(text, out value))
        {
            error = null;
            return true;
        }

        error = new ValidationDaemonError("instance.pipeline_output.invalid", $"Pipeline output '{key}' has an unsupported value.");
        return false;
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
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance removal failure for {InstanceId}.", request.InstanceId);
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
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance start failure for {InstanceId}.", request.InstanceId);
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
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance stop failure for {InstanceId}.", request.InstanceId);
            return Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.stop_failed", "The instance could not be stopped."));
        }
    }

    public async Task<Result<Unit, DaemonError>> HaltInstanceAsync(
        InstanceReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Halt commits under the same per-instance mutation gate as start/stop.
            // Missing instances or processes remain idempotent success.
            await instanceManager.KillInstanceAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            return Result.Ok<Unit, DaemonError>(Unit.Default);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance halt failure for {InstanceId}.", request.InstanceId);
            return Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.halt_failed", "The instance could not be halted."));
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
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance command failure for {InstanceId}.", request.InstanceId);
            return Task.FromResult(Result.Err<Unit, DaemonError>(
                new InternalDaemonError("instance.command_failed", "The command could not be sent to the instance.")));
        }
    }

    public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(
        ConsoleOpenRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Default open without connection fan-out; connection layer should call Consoles.Open with a handler.
        return Task.FromResult(_consoles.Open(request, static (_, _, _) => Task.CompletedTask));
    }

    public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(
        ConsoleResizeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_consoles.Resize(request));
    }

    public Task<Result<Unit, DaemonError>> CloseConsoleAsync(
        ConsoleSessionReference request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_consoles.Close(request));
    }

    public Task<Result<Unit, DaemonError>> WriteConsoleAsync(
        Guid sessionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_consoles.Write(sessionId, data));
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
                InstanceConfigurationMapper.ToContract(report, processId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance report failure for {InstanceId}.", request.InstanceId);
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
                pair => InstanceConfigurationMapper.ToContract(
                    pair.Value,
                    instanceManager.Instances.TryGetValue(pair.Key, out var instance) ? instance.ServerProcessId : -1));
            return Result.Ok<InstanceReportList, DaemonError>(new InstanceReportList(mapped));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected instance report-list failure.");
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
            return result.IsErr(out var error)
                ? Result.Err<InstanceSettingsResult, DaemonError>(error!)
                : Result.Ok<InstanceSettingsResult, DaemonError>(result.Unwrap());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected settings read failure for {InstanceId}.", request.InstanceId);
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
            var result = await instanceManager.UpdateInstanceSettings(request, cancellationToken);
            if (result.IsErr(out var updateError))
                return Result.Err<UpdateInstanceSettingsResult, DaemonError>(updateError!);

            return Result.Ok<UpdateInstanceSettingsResult, DaemonError>(
                result.Unwrap());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[LocalInstanceApplication] Unexpected settings update failure for {InstanceId}.", request.InstanceId);
            return Result.Err<UpdateInstanceSettingsResult, DaemonError>(
                new InternalDaemonError("instance.settings_failed", "The instance settings could not be updated."));
        }
    }

    private static NotFoundDaemonError InstanceNotFound(Guid instanceId)
    {
        return new NotFoundDaemonError("instance.not_found", $"Instance '{instanceId}' was not found.");
    }
}
