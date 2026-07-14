using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using LegacyGetInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.GetInstanceSettingsResult;
using LegacyInstanceInstallMetadata = MCServerLauncher.Common.ProtoType.Action.InstanceInstallMetadata;
using LegacyInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;
using LegacyUpdateInstanceSettingsResult = MCServerLauncher.Common.ProtoType.Action.UpdateInstanceSettingsResult;

namespace MCServerLauncher.Daemon.Remote.Action;

internal static class LegacyInstanceActionMapper
{
    public static InstanceConfiguration ToContract(InstanceFactorySetting setting)
    {
        return InstanceConfigurationMapper.ToContract(setting);
    }

    public static InstanceConfig ToLegacy(InstanceConfiguration config)
    {
        return InstanceConfigurationMapper.ToInstanceConfig(config);
    }

    public static LegacyInstanceReport ToLegacy(
        MCServerLauncher.Common.Contracts.Instances.InstanceReport report)
    {
        return new LegacyInstanceReport(
            report.Status,
            ToLegacy(report.Config),
            report.Properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal),
            report.Players.Select(static player => new Player(player.Name, player.Uuid)).ToArray(),
            new InstancePerformanceCounter(report.PerformanceCounter.Cpu, report.PerformanceCounter.MemoryBytes));
    }

    public static LegacyGetInstanceSettingsResult ToLegacy(InstanceSettingsResult result)
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

    public static LegacyUpdateInstanceSettingsResult ToLegacy(UpdateInstanceSettingsResult result)
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

    private static LegacyInstanceInstallMetadata ToLegacy(InstanceInstallMetadata metadata)
    {
        return new LegacyInstanceInstallMetadata
        {
            InstallerKind = metadata.InstallerKind,
            InstallerSourcePath = metadata.InstallerSourcePath,
            GeneratedPaths = metadata.GeneratedPaths.IsDefault ? [] : metadata.GeneratedPaths.ToArray(),
            ResolvedLaunchTarget = metadata.ResolvedLaunchTarget,
            InstalledAt = metadata.InstalledAt
        };
    }
}
