using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Management;

internal sealed record InstanceInstallMetadataDocument
{
    [JsonRequired]
    public string InstallerKind { get; init; } = null!;

    public string? InstallerSourcePath { get; init; }

    [JsonRequired]
    public string[] GeneratedPaths { get; init; } = [];

    public string? ResolvedLaunchTarget { get; init; }

    public DateTimeOffset InstalledAt { get; init; }
}
