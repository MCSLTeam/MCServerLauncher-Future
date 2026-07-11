using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using LegacyGetInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.GetInstanceSettingsResult;
using LegacyInstanceInstallMetadata = MCServerLauncher.Common.ProtoType.Action.InstanceInstallMetadata;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;
using LegacyUpdateInstanceSettingsParameter = MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsParameter;
using LegacyUpdateInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsResult;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal static class InstanceContractMapper
{
    internal static InstanceFactorySetting ToLegacy(InstanceFactoryConfiguration setting)
    {
        var config = setting.Configuration;
        return new InstanceFactorySetting
        {
            Uuid = config.InstanceId,
            Name = config.Name,
            Target = config.Target,
            InstanceType = config.InstanceType,
            TargetType = config.TargetType,
            Version = config.Version,
            InputEncodingWebName = config.InputEncoding,
            OutputEncodingWebName = config.OutputEncoding,
            JavaPath = config.JavaPath,
            Arguments = config.Arguments.ToArray(),
            Env = config.EnvironmentVariables.ToDictionary(
                pair => pair.Key,
                pair => new PlaceHolderString(pair.Value),
                StringComparer.Ordinal),
            EventRules = DeserializeEventRules(config.EventRules),
            Source = setting.Source,
            SourceType = setting.SourceType,
            Mirror = setting.Mirror,
            UsePostProcess = setting.UsePostProcess
        };
    }

    internal static LegacyUpdateInstanceSettingsParameter ToLegacy(UpdateInstanceSettingsRequest request)
    {
        return new LegacyUpdateInstanceSettingsParameter
        {
            Id = request.InstanceId,
            Name = request.Name,
            InstanceType = request.InstanceType,
            JavaPath = request.JavaPath,
            Arguments = request.Arguments.ToArray(),
            Version = request.Version,
            ReplacementCore = request.ReplacementCore is null
                ? null
                : new MCServerLauncher.Common.ProtoType.Action.InstanceCoreReplacementRequest
                {
                    UploadedSourcePath = request.ReplacementCore.UploadedSourcePath,
                    PreferredTargetName = request.ReplacementCore.PreferredTargetName
                },
            ForceRerunInstaller = request.ForceRerunInstaller
        };
    }

    internal static InstanceConfiguration ToContract(InstanceConfig config)
    {
        return new InstanceConfiguration(
            config.Uuid,
            config.Name,
            config.Target,
            config.InstanceType,
            config.TargetType,
            config.Version,
            config.InputEncodingWebName,
            config.OutputEncodingWebName,
            config.JavaPath,
            config.Arguments.ToImmutableArray(),
            config.Env.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.Pattern,
                StringComparer.Ordinal),
            JsonSerializer.SerializeToElement(config.EventRules, EventRuleJsonContext.Default.EventRuleList));
    }

    internal static MCServerLauncher.Common.Contracts.Instances.InstanceReport ToContract(
        LegacyInstanceReport report,
        int processId)
    {
        return new MCServerLauncher.Common.Contracts.Instances.InstanceReport(
            report.Status,
            ToContract(report.Config),
            report.Properties.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            report.Players.Select(player => new InstancePlayer(player.Name, player.Uuid)).ToImmutableArray(),
            new InstancePerformance(report.PerformanceCounter.Cpu, report.PerformanceCounter.Memory),
            processId < 0 ? null : processId);
    }

    internal static LegacyInstanceReport ToLegacy(MCServerLauncher.Common.Contracts.Instances.InstanceReport report)
    {
        return new LegacyInstanceReport(
            report.Status,
            ToLegacy(report.Config),
            report.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal),
            report.Players.Select(player => new Player(player.Name, player.Uuid)).ToArray(),
            new InstancePerformanceCounter(report.PerformanceCounter.Cpu, report.PerformanceCounter.MemoryBytes));
    }

    internal static InstanceSettingsResult ToContract(LegacyGetInstanceSettingsResult result)
    {
        return new InstanceSettingsResult(
            ToContract(result.Config),
            result.WorkingDirectory,
            result.CurrentTargetExists,
            result.CanEdit,
            result.EditBlockedReason,
            result.InstallMetadata is null ? null : ToContract(result.InstallMetadata));
    }

    internal static UpdateInstanceSettingsResult ToContract(LegacyUpdateInstanceSettingsResult result)
    {
        return new UpdateInstanceSettingsResult(
            ToContract(result.Config),
            result.RequiresRestart,
            result.Reinstalled,
            result.DeletedGeneratedPaths.ToImmutableArray(),
            result.PreservedOriginalPaths.ToImmutableArray());
    }

    internal static LegacyGetInstanceSettingsResult ToLegacy(InstanceSettingsResult result)
    {
        return new LegacyGetInstanceSettingsResult
        {
            Config = ToLegacy(result.Config),
            WorkingDirectory = result.WorkingDirectory,
            CurrentTargetExists = result.CurrentTargetExists,
            CanEdit = result.CanEdit,
            EditBlockedReason = result.EditBlockedReason,
            InstallMetadata = result.InstallMetadata is null ? null : ToLegacy(result.InstallMetadata)
        };
    }

    internal static LegacyUpdateInstanceSettingsResult ToLegacy(UpdateInstanceSettingsResult result)
    {
        return new LegacyUpdateInstanceSettingsResult
        {
            Config = ToLegacy(result.Config),
            RequiresRestart = result.RequiresRestart,
            Reinstalled = result.Reinstalled,
            DeletedGeneratedPaths = result.DeletedGeneratedPaths.ToArray(),
            PreservedOriginalPaths = result.PreservedOriginalPaths.ToArray()
        };
    }

    internal static InstanceConfig ToLegacy(InstanceConfiguration config)
    {
        return new InstanceConfig
        {
            Uuid = config.InstanceId,
            Name = config.Name,
            Target = config.Target,
            InstanceType = config.InstanceType,
            TargetType = config.TargetType,
            Version = config.Version,
            InputEncodingWebName = config.InputEncoding,
            OutputEncodingWebName = config.OutputEncoding,
            JavaPath = config.JavaPath,
            Arguments = config.Arguments.ToArray(),
            Env = config.EnvironmentVariables.ToDictionary(
                pair => pair.Key,
                pair => new PlaceHolderString(pair.Value),
                StringComparer.Ordinal),
            EventRules = DeserializeEventRules(config.EventRules)
        };
    }

    internal static LegacyInstanceInstallMetadata ToLegacy(InstanceInstallMetadata metadata)
    {
        return new LegacyInstanceInstallMetadata
        {
            InstallerKind = metadata.InstallerKind,
            InstallerSourcePath = metadata.InstallerSourcePath,
            GeneratedPaths = metadata.GeneratedPaths.ToArray(),
            ResolvedLaunchTarget = metadata.ResolvedLaunchTarget,
            InstalledAt = metadata.InstalledAt
        };
    }

    internal static MCServerLauncher.Common.Contracts.Instances.InstanceInstallMetadata ToContract(
        LegacyInstanceInstallMetadata metadata)
    {
        return new InstanceInstallMetadata(
            metadata.InstallerKind,
            metadata.InstallerSourcePath,
            metadata.GeneratedPaths.ToImmutableArray(),
            metadata.ResolvedLaunchTarget,
            metadata.InstalledAt);
    }

    private static List<EventRule> DeserializeEventRules(JsonElement eventRules)
    {
        return JsonSerializer.Deserialize(eventRules, EventRuleJsonContext.Default.EventRuleList)
               ?? throw new JsonException("Instance event rules must be an array.");
    }
}
