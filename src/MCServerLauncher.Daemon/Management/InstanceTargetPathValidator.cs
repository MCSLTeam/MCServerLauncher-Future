using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Management;

internal sealed class InstancePathValidationError(string message) : MCServerLauncher.Daemon.Utils.Error(message);

internal static class InstanceTargetPathValidator
{
    public static bool TryResolveTargetFile(
        string workingDirectory,
        string? target,
        out string fullPath,
        out InstancePathValidationError? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(target) ||
            Path.IsPathRooted(target) ||
            IsDriveQualified(target) ||
            !string.Equals(target, Path.GetFileName(target), StringComparison.Ordinal) ||
            target is "." or ".." ||
            target.IndexOfAny(['/', '\\', ':']) >= 0 ||
            target.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = new InstancePathValidationError("Instance target must be a contained file name.");
            return false;
        }

        return TryResolveContainedPath(workingDirectory, target, out fullPath, out error);
    }

    public static bool TryResolveGeneratedPath(
        string workingDirectory,
        string? generatedPath,
        out string fullPath,
        out InstancePathValidationError? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(generatedPath) ||
            Path.IsPathRooted(generatedPath) ||
            IsDriveQualified(generatedPath) ||
            generatedPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment is "." or ".."))
        {
            error = new InstancePathValidationError("Instance installation metadata contains an invalid generated path.");
            return false;
        }

        if (!TryResolveContainedPath(workingDirectory, generatedPath, out fullPath, out error))
            return false;

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            error = new InstancePathValidationError("Instance installation metadata cannot target the instance root.");
            return false;
        }

        return true;
    }

    private static bool TryResolveContainedPath(
        string workingDirectory,
        string path,
        out string fullPath,
        out InstancePathValidationError? error)
    {
        try
        {
            fullPath = FileSessionCoordinator.ResolveAndValidatePath(path, workingDirectory);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            fullPath = string.Empty;
            error = new InstancePathValidationError("Instance path must remain contained within the instance directory.");
            return false;
        }
    }

    private static bool IsDriveQualified(string path)
    {
        return path.Length >= 2 && path[1] == Path.VolumeSeparatorChar;
    }
}
