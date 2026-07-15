using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Management;

internal static class InstanceTargetPathValidator
{
    private static readonly string[] ReservedFileNames =
    [
        InstanceConfig.FileName,
        InstanceConfig.FileName + ".bak",
        InstanceInstallMetadataStore.FileName,
        InstanceInstallMetadataStore.FileName + ".bak"
    ];

    public static bool TryResolveTargetFile(
        string workingDirectory,
        string? target,
        out string fullPath,
        out ValidationDaemonError? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(target) ||
            Path.IsPathRooted(target) ||
            IsDriveQualified(target) ||
            !string.Equals(target, Path.GetFileName(target), StringComparison.Ordinal) ||
            target is "." or ".." ||
            target.EndsWith('.') ||
            target.EndsWith(' ') ||
            IsWindowsDeviceName(target) ||
            target.IndexOfAny(['/', '\\', ':']) >= 0 ||
            target.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = new ValidationDaemonError(
                "instance.target.invalid",
                "Instance target must be a contained file name.");
            return false;
        }

        if (IsReservedFileName(target))
        {
            error = new ValidationDaemonError(
                "instance.target.reserved",
                "Instance target conflicts with daemon-owned instance metadata.");
            return false;
        }

        if (!TryResolveContainedPath(workingDirectory, target, out fullPath, out error))
            return false;

        if (IsReservedPath(workingDirectory, fullPath))
        {
            fullPath = string.Empty;
            error = new ValidationDaemonError(
                "instance.target.reserved",
                "Instance target conflicts with daemon-owned instance metadata.");
            return false;
        }

        return true;
    }

    public static bool TryResolveGeneratedPath(
        string workingDirectory,
        string? generatedPath,
        out string fullPath,
        out ValidationDaemonError? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(generatedPath) ||
            Path.IsPathRooted(generatedPath) ||
            IsDriveQualified(generatedPath) ||
            generatedPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment =>
                    segment is "." or ".." ||
                    segment.EndsWith('.') ||
                    segment.EndsWith(' ') ||
                    IsWindowsDeviceName(segment) ||
                    segment.Contains(':') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            error = new ValidationDaemonError(
                "instance.generated_path.invalid",
                "Instance installation metadata contains an invalid generated path.");
            return false;
        }

        if (!TryResolveContainedPath(workingDirectory, generatedPath, out fullPath, out error))
            return false;

        if (IsReservedPath(workingDirectory, fullPath))
        {
            fullPath = string.Empty;
            error = new ValidationDaemonError(
                "instance.generated_path.reserved",
                "Instance installation metadata cannot own daemon-managed instance metadata.");
            return false;
        }

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            error = new ValidationDaemonError(
                "instance.generated_path.invalid",
                "Instance installation metadata cannot target the instance root.");
            return false;
        }

        return true;
    }

    private static bool TryResolveContainedPath(
        string workingDirectory,
        string path,
        out string fullPath,
        out ValidationDaemonError? error)
    {
        try
        {
            fullPath = FileSessionCoordinator.ResolveAndValidatePath(path, workingDirectory);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is
                                           ArgumentException or
                                           IOException or
                                           NotSupportedException or
                                           UnauthorizedAccessException)
        {
            fullPath = string.Empty;
            error = new ValidationDaemonError(
                "instance.path.outside_root",
                "Instance path must remain contained within the instance directory.");
            return false;
        }
    }

    private static bool IsDriveQualified(string path)
    {
        return path.Length >= 2 && path[1] == Path.VolumeSeparatorChar;
    }

    private static bool IsReservedFileName(string fileName)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return ReservedFileNames.Any(candidate => string.Equals(candidate, fileName, comparison));
    }

    private static bool IsWindowsDeviceName(string fileName)
    {
        var separatorIndex = fileName.IndexOf('.');
        var stem = (separatorIndex < 0 ? fileName : fileName[..separatorIndex]).TrimEnd(' ');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               stem[3] is >= '1' and <= '9';
    }

    private static bool IsReservedPath(string workingDirectory, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return ReservedFileNames.Any(fileName => string.Equals(
            Path.GetFullPath(Path.Combine(workingDirectory, fileName)),
            Path.GetFullPath(fullPath),
            comparison));
    }
}
