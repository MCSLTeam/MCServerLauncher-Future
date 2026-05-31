using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Detection;

public enum InstanceVersionDetectionSource
{
    None,
    VersionJson,
    PatchProperties,
    VersionProperties,
    Manifest,
    PomProperties,
    MetaInfVersion,
    PluginYaml,
    BungeeYaml,
    McdReforgedPluginJson,
    VersionInfoPhp,
    ExecutableMetadata,
    BinaryStrings,
    SteamManifest,
    Filename
}

public enum InstanceVersionDetectionConfidence
{
    None,
    Fallback,
    Strong,
    Exact
}

public sealed record InstanceVersionDetectionResult(
    InstanceType OriginalInstanceType,
    InstanceType MatchedInstanceType,
    string? Version,
    string? NormalizedMcVersion,
    InstanceVersionDetectionSource Source,
    InstanceVersionDetectionConfidence Confidence,
    string Evidence
)
{
    public bool IsMatched => Source is not InstanceVersionDetectionSource.None;
    public bool IsTypeChanged => OriginalInstanceType != MatchedInstanceType;

    public static InstanceVersionDetectionResult NoMatch(InstanceType type, string evidence = "No detection rule matched")
    {
        return new InstanceVersionDetectionResult(
            type,
            type,
            null,
            null,
            InstanceVersionDetectionSource.None,
            InstanceVersionDetectionConfidence.None,
            evidence
        );
    }
}
