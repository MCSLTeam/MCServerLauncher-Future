using System.Text.Json;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalEventRuleApplication : IEventRuleApplication
{
    private readonly IInstanceManager _instanceManager;
    private readonly ILogger<LocalEventRuleApplication> _logger;
    private readonly Action<string, InstanceConfig> _writeConfig;

    public LocalEventRuleApplication(
        IInstanceManager instanceManager,
        ILogger<LocalEventRuleApplication> logger,
        Action<string, InstanceConfig>? writeConfig = null)
    {
        _instanceManager = instanceManager;
        _logger = logger;
        _writeConfig = writeConfig ?? FileManager.WriteJsonAndBackup;
    }

    public async Task<Result<EventRuleSet, DaemonError>> GetEventRulesAsync(
        EventRuleQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var mutation = await _instanceManager.AcquireInstanceMutationAsync(request.InstanceId, cancellationToken);
        if (!_instanceManager.Instances.TryGetValue(request.InstanceId, out var instance))
        {
            return Result.Err<EventRuleSet, DaemonError>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{request.InstanceId}' was not found."));
        }

        var rules = JsonSerializer.SerializeToElement(instance.Config.EventRules, EventRuleJsonContext.Default.EventRuleList);
        return Result.Ok<EventRuleSet, DaemonError>(new EventRuleSet(request.InstanceId, rules));
    }

    public async Task<Result<Unit, DaemonError>> UpdateEventRulesAsync(
        EventRuleUpdateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_instanceManager.Instances.ContainsKey(request.InstanceId))
        {
            return Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{request.InstanceId}' was not found."));
        }

        List<EventRule>? stagedRules;
        try
        {
            stagedRules = JsonSerializer.Deserialize(request.Rules, EventRuleJsonContext.Default.EventRuleList);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Rejected malformed event rules for instance '{InstanceId}'", request.InstanceId);
            return Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("event_rules.invalid", "Event rules are malformed."));
        }

        if (stagedRules is null || stagedRules.Any(static rule => rule is null))
        {
            return Result.Err<Unit, DaemonError>(
                new ValidationDaemonError("event_rules.invalid", "Event rules must be an array."));
        }

        using var mutation = await _instanceManager.AcquireInstanceMutationAsync(request.InstanceId, cancellationToken);
        if (!_instanceManager.Instances.TryGetValue(request.InstanceId, out var instance))
        {
            return Result.Err<Unit, DaemonError>(
                new NotFoundDaemonError("instance.not_found", $"Instance '{request.InstanceId}' was not found."));
        }

        try
        {
            var stagedConfig = CloneConfig(instance.Config);
            stagedConfig.EventRules.Clear();
            stagedConfig.EventRules.AddRange(stagedRules);

            var configPath = Path.Combine(stagedConfig.GetWorkingDirectory(), InstanceConfig.FileName);
            _writeConfig(configPath, stagedConfig);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to persist event rules for instance '{InstanceId}'", request.InstanceId);
            return Result.Err<Unit, DaemonError>(
                new StorageDaemonError("event_rules.persist_failed", "Event rules could not be persisted."));
        }

        instance.Config.EventRules.Clear();
        instance.Config.EventRules.AddRange(stagedRules);
        return Result.Ok<Unit, DaemonError>(Unit.Default);
    }

    private static InstanceConfig CloneConfig(InstanceConfig config)
    {
        return config with
        {
            EventRules = [.. config.EventRules]
        };
    }
}
