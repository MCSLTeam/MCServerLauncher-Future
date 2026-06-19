using System;
using System.IO;
using MCServerLauncher.Common.ProtoType.Action;
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
        return File.Exists(path) ? FileManager.ReadJson<InstanceInstallMetadata>(path) : null;
    }

    public static void Write(string workingDirectory, InstanceInstallMetadata metadata)
    {
        FileManager.WriteJsonAndBackup(GetPath(workingDirectory), metadata);
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
