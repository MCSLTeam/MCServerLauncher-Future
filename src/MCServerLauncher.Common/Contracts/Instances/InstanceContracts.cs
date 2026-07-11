using System.Collections.Immutable;
using System.Text.Json;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Instances;

public sealed record InstanceConfiguration
{
    public InstanceConfiguration(
        Guid instanceId,
        string name,
        string target,
        InstanceType instanceType,
        TargetType targetType,
        string version,
        string inputEncoding,
        string outputEncoding,
        string javaPath,
        ImmutableArray<string> arguments,
        ImmutableDictionary<string, string> environmentVariables,
        JsonElement eventRules)
    {
        InstanceId = instanceId;
        Name = name;
        Target = target;
        InstanceType = instanceType;
        TargetType = targetType;
        Version = version;
        InputEncoding = inputEncoding;
        OutputEncoding = outputEncoding;
        JavaPath = javaPath;
        Arguments = arguments;
        EnvironmentVariables = environmentVariables;
        EventRules = eventRules.Clone();
    }

    public Guid InstanceId { get; }

    public string Name { get; }

    public string Target { get; }

    public InstanceType InstanceType { get; }

    public TargetType TargetType { get; }

    public string Version { get; }

    public string InputEncoding { get; }

    public string OutputEncoding { get; }

    public string JavaPath { get; }

    public ImmutableArray<string> Arguments { get; }

    public ImmutableDictionary<string, string> EnvironmentVariables { get; }

    public JsonElement EventRules { get; }
}

public sealed record InstanceFactoryConfiguration(
    InstanceConfiguration Configuration,
    string Source,
    SourceType SourceType,
    InstanceFactoryMirror Mirror,
    bool UsePostProcess);

public sealed record CreateInstanceRequest(InstanceFactoryConfiguration Setting);

public sealed record CreateInstanceResult(InstanceConfiguration Config);

public sealed record InstanceReference(Guid InstanceId);

public sealed record InstanceCommandRequest(Guid InstanceId, string Command);

public sealed record InstanceCoreReplacementRequest(string UploadedSourcePath, string? PreferredTargetName);

public sealed record UpdateInstanceSettingsRequest(
    Guid InstanceId,
    string Name,
    InstanceType InstanceType,
    string? JavaPath,
    ImmutableArray<string> Arguments,
    string? Version,
    InstanceCoreReplacementRequest? ReplacementCore,
    bool ForceRerunInstaller);

public sealed record InstanceInstallMetadata(
    string InstallerKind,
    string? InstallerSourcePath,
    ImmutableArray<string> GeneratedPaths,
    string? ResolvedLaunchTarget,
    DateTimeOffset InstalledAt);

public sealed record InstanceSettingsResult(
    InstanceConfiguration Config,
    string WorkingDirectory,
    bool CurrentTargetExists,
    bool CanEdit,
    string? EditBlockedReason,
    InstanceInstallMetadata? InstallMetadata);

public sealed record UpdateInstanceSettingsResult(
    InstanceConfiguration Config,
    bool RequiresRestart,
    bool Reinstalled,
    ImmutableArray<string> DeletedGeneratedPaths,
    ImmutableArray<string> PreservedOriginalPaths);

public sealed record InstancePlayer(string Name, Guid Uuid);

public sealed record InstancePerformance(double Cpu, long MemoryBytes);

public sealed record InstanceReport(
    InstanceStatus Status,
    InstanceConfiguration Config,
    ImmutableDictionary<string, string> Properties,
    ImmutableArray<InstancePlayer> Players,
    InstancePerformance PerformanceCounter,
    int? ProcessId);

public sealed record InstanceReportList(ImmutableDictionary<Guid, InstanceReport> Reports);

public sealed record InstanceLogQuery(Guid InstanceId);

public sealed record InstanceLogResult(ImmutableArray<string> Logs);
