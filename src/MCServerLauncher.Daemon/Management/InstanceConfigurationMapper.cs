using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;
using RuntimeInstanceReport = MCServerLauncher.Common.ProtoType.Instance.InstanceReport;

namespace MCServerLauncher.Daemon.Management;

internal static class InstanceConfigurationMapper
{
    public static InstanceConfiguration ToContract(InstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

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
                static pair => pair.Key,
                static pair => pair.Value.Pattern,
                StringComparer.Ordinal),
            EventRuleDocumentCodec.SerializeToElement(config.EventRules),
            config.ConsoleMode);
    }

    public static InstanceConfig ToInstanceConfig(InstanceConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

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
                static pair => pair.Key,
                static pair => new PlaceHolderString(pair.Value),
                StringComparer.Ordinal),
            EventRules = EventRuleDocumentCodec.DeserializeRequired(config.EventRules),
            ConsoleMode = config.ConsoleMode
        };
    }

    public static InstanceConfiguration WithInstanceId(InstanceConfiguration config, Guid instanceId)
    {
        ArgumentNullException.ThrowIfNull(config);

        return Copy(config, instanceId: instanceId);
    }

    public static InstanceConfiguration WithTarget(
        InstanceConfiguration config,
        string target,
        TargetType targetType)
    {
        ArgumentNullException.ThrowIfNull(config);

        return Copy(config, target: target, targetType: targetType);
    }

    public static ContractInstanceReport ToContract(RuntimeInstanceReport report, int processId)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new ContractInstanceReport(
            report.Status,
            ToContract(report.Config),
            report.Properties.ToImmutableDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal),
            report.Players.Select(static player => new InstancePlayer(player.Name, player.Uuid)).ToImmutableArray(),
            new InstancePerformance(report.PerformanceCounter.Cpu, report.PerformanceCounter.Memory),
            processId < 0 ? null : processId);
    }

    private static InstanceConfiguration Copy(
        InstanceConfiguration config,
        Guid? instanceId = null,
        string? target = null,
        TargetType? targetType = null)
    {
        return new InstanceConfiguration(
            instanceId ?? config.InstanceId,
            config.Name,
            target ?? config.Target,
            config.InstanceType,
            targetType ?? config.TargetType,
            config.Version,
            config.InputEncoding,
            config.OutputEncoding,
            config.JavaPath,
            config.Arguments,
            config.EnvironmentVariables,
            config.EventRules,
            config.ConsoleMode);
    }
}
