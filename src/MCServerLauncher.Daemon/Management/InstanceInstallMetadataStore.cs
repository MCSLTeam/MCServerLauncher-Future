using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Management;

internal static class InstanceInstallMetadataStore
{
    public const string FileName = "daemon_instance.install.json";

    public static string GetPath(string workingDirectory)
    {
        return Path.Combine(workingDirectory, FileName);
    }

    public static InstanceInstallMetadata? Read(string workingDirectory)
    {
        var path = GetPath(workingDirectory);
        if (!File.Exists(path))
            return null;

        var document = FileManager.ReadJson<InstanceInstallMetadataDocument>(path);
        return document is null
            ? null
            : new InstanceInstallMetadata(
                document.InstallerKind,
                document.InstallerSourcePath,
                (document.GeneratedPaths ?? []).ToImmutableArray(),
                document.ResolvedLaunchTarget,
                document.InstalledAt);
    }

    public static void Write(string workingDirectory, InstanceInstallMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        FileManager.WriteJsonAndBackup(
            GetPath(workingDirectory),
            new InstanceInstallMetadataDocument
            {
                InstallerKind = metadata.InstallerKind,
                InstallerSourcePath = metadata.InstallerSourcePath,
                GeneratedPaths = metadata.GeneratedPaths.IsDefault ? [] : metadata.GeneratedPaths.ToArray(),
                ResolvedLaunchTarget = metadata.ResolvedLaunchTarget,
                InstalledAt = metadata.InstalledAt
            });
    }

    public static void Delete(string workingDirectory)
    {
        var path = GetPath(workingDirectory);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
