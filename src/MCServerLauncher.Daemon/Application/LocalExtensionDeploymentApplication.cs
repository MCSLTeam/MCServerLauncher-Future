using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed partial class LocalExtensionDeploymentApplication : IExtensionDeploymentApplication
{
    private const int MaxBundleFiles = 256;
    private const long MaxBundleUncompressedBytes = 64L * 1024 * 1024;

    private readonly object _configGate = new();
    private readonly string _pluginsRoot;
    private readonly AppConfig _config;
    private readonly string? _configPath;

    internal LocalExtensionDeploymentApplication(AppConfig config, string pluginsRoot, string? configPath = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        _pluginsRoot = Path.GetFullPath(pluginsRoot);
        _configPath = configPath;
    }

    public Task<Result<ExtensionDaemonBundleDeploymentResult, DaemonError>> InstallDaemonBundleAsync(
        ExtensionDaemonBundleInstallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryValidatePluginId(request.PluginId, out var pluginId, out var pluginIdError))
            return Task.FromResult(Err(pluginIdError!));
        if (string.IsNullOrWhiteSpace(request.SourcePath))
            return Task.FromResult(Err(new ValidationDaemonError("extension.bundle.source_missing", "SourcePath is required.")));
        if (!Sha256Regex().IsMatch(request.Sha256))
            return Task.FromResult(Err(new ValidationDaemonError("extension.bundle.sha256_invalid", "Sha256 must be a lowercase 64-character hex digest.")));

        string sourcePath;
        try
        {
            sourcePath = FileManager.ResolveAndValidatePath(request.SourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(Err(new ValidationDaemonError("extension.bundle.source_invalid", exception.Message)));
        }

        if (!IsWithinDirectory(FileManager.UploadRoot, sourcePath))
        {
            return Task.FromResult(Err(new ValidationDaemonError(
                "extension.bundle.source_outside_uploads",
                "Daemon extension bundles must be installed from the daemon upload staging area.")));
        }

        if (!File.Exists(sourcePath))
            return Task.FromResult(Err(new NotFoundDaemonError("extension.bundle.source_not_found", "The staged daemon extension bundle was not found.")));

        return InstallDaemonBundleCoreAsync(pluginId!, sourcePath, request.Sha256, cancellationToken);
    }

    public Task<Result<ExtensionDaemonBundleDeploymentResult, DaemonError>> GetDaemonBundleStatusAsync(
        ExtensionDaemonBundleStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryValidatePluginId(request.PluginId, out var pluginId, out var pluginIdError))
            return Task.FromResult(Err(pluginIdError!));

        var target = GetPluginDirectory(pluginId!);
        var installed = Directory.Exists(target);
        bool configured;
        bool enabled;
        lock (_configGate)
        {
            configured = _config.Plugins.Entries.TryGetValue(pluginId!, out var entry);
            enabled = entry?.Enabled ?? false;
        }

        var status = (installed, configured, enabled) switch
        {
            (true, true, true) => "installed_enabled",
            (true, true, false) => "installed_disabled",
            (true, false, _) => "installed_unconfigured",
            (false, true, true) => "configured_missing",
            (false, true, false) => "disabled_missing",
            _ => "not_installed"
        };

        var message = status switch
        {
            "installed_enabled" => "Daemon extension bundle is installed and enabled in configuration.",
            "installed_disabled" => "Daemon extension bundle is installed but disabled in configuration.",
            "installed_unconfigured" => "Daemon extension bundle is installed but has no configuration entry.",
            "configured_missing" => "Daemon extension bundle is enabled in configuration but missing from plugins directory.",
            "disabled_missing" => "Daemon extension bundle is disabled and missing from plugins directory.",
            _ => "Daemon extension bundle is not installed."
        };

        return Task.FromResult(Ok(new ExtensionDaemonBundleDeploymentResult(
            pluginId!,
            status,
            RestartRequired: false,
            $"plugins/{pluginId}",
            message)));
    }

    public Task<Result<ExtensionDaemonBundleDeploymentResult, DaemonError>> RemoveDaemonBundleAsync(
        ExtensionDaemonBundleRemoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryValidatePluginId(request.PluginId, out var pluginId, out var pluginIdError))
            return Task.FromResult(Err(pluginIdError!));

        var target = GetPluginDirectory(pluginId!);
        if (!Directory.Exists(target))
            return Task.FromResult(Err(new NotFoundDaemonError("extension.bundle.not_installed", $"Daemon extension '{pluginId}' is not installed.")));

        var disabled = Path.Combine(_pluginsRoot, $".{pluginId}.removed-{Guid.NewGuid():N}");
        var rollback = PersistEntry(pluginId!, enabled: false);
        if (rollback.IsErr(out var rollbackError))
            return Task.FromResult(Err(rollbackError!));

        try
        {
            Directory.Move(target, disabled);
            TryDeleteDirectory(disabled);
            return Task.FromResult(Ok(new ExtensionDaemonBundleDeploymentResult(
                pluginId!,
                "removed_pending_restart",
                RestartRequired: true,
                $"plugins/{pluginId}",
                "Daemon extension bundle removed. Restart the daemon to unload an active plugin.")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            rollback.Unwrap().Invoke();
            if (Directory.Exists(disabled) && !Directory.Exists(target))
                Directory.Move(disabled, target);
            return Task.FromResult(Err(new StorageDaemonError("extension.bundle.remove_failed", exception.Message)));
        }
    }

    private async Task<Result<ExtensionDaemonBundleDeploymentResult, DaemonError>> InstallDaemonBundleCoreAsync(
        string pluginId,
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var actualSha256 = await FileManager.FileSha256(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(actualSha256, expectedSha256))
        {
            return Err(new ValidationDaemonError(
                "extension.bundle.sha256_mismatch",
                "The staged daemon extension bundle SHA-256 does not match the package manifest."));
        }

        Directory.CreateDirectory(_pluginsRoot);
        var installRoot = Path.Combine(_pluginsRoot, $".{pluginId}.install-{Guid.NewGuid():N}");
        var target = GetPluginDirectory(pluginId);
        var backup = Path.Combine(_pluginsRoot, $".{pluginId}.backup-{Guid.NewGuid():N}");
        try
        {
            ExtractBundle(sourcePath, installRoot, cancellationToken);
            var manifest = PluginManifestReader.ReadAndValidate(installRoot, PluginHost.HostApiVersion);
            if (!StringComparer.Ordinal.Equals(manifest.Identity.Id, pluginId))
            {
                return Err(new ValidationDaemonError(
                    "extension.bundle.plugin_mismatch",
                    $"Bundle manifest id '{manifest.Identity.Id}' does not match requested plugin id '{pluginId}'."));
            }

            PluginAssemblyPolicy.ValidateBundle(manifest);
            var rollbackConfig = PersistEntry(pluginId, enabled: true);
            if (rollbackConfig.IsErr(out var configError))
                return Err(configError!);

            try
            {
                if (Directory.Exists(target))
                    Directory.Move(target, backup);
                Directory.Move(installRoot, target);
                TryDeleteDirectory(backup);
            }
            catch
            {
                rollbackConfig.Unwrap().Invoke();
                RestorePluginDirectory(target, backup);
                throw;
            }

            return Ok(new ExtensionDaemonBundleDeploymentResult(
                pluginId,
                "installed_pending_restart",
                RestartRequired: true,
                $"plugins/{pluginId}",
                "Daemon extension bundle installed. Restart the daemon to load it."));
        }
        catch (PluginManifestException exception)
        {
            return Err(new ValidationDaemonError(exception.Code, exception.Message));
        }
        catch (PluginAssemblyException exception)
        {
            return Err(new ValidationDaemonError(exception.Code, exception.Message));
        }
        catch (InvalidDataException exception)
        {
            return Err(new ValidationDaemonError("extension.bundle.zip_invalid", exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Err(new StorageDaemonError("extension.bundle.install_failed", exception.Message));
        }
        finally
        {
            TryDeleteDirectory(installRoot);
            TryDeleteDirectory(backup);
        }
    }

    private Result<Action, DaemonError> PersistEntry(string pluginId, bool enabled)
    {
        lock (_configGate)
        {
            var hadEntry = _config.Plugins.Entries.TryGetValue(pluginId, out var previousEntry);
            var replacement = new PluginEntryConfig
            {
                Enabled = enabled,
                StorageQuotaBytes = previousEntry?.StorageQuotaBytes
            };
            _config.Plugins.Entries[pluginId] = replacement;
            if (_config.TrySave(_configPath))
            {
                return Result.Ok<Action, DaemonError>(() =>
                {
                    lock (_configGate)
                    {
                        if (hadEntry)
                            _config.Plugins.Entries[pluginId] = previousEntry!;
                        else
                            _config.Plugins.Entries.Remove(pluginId);
                        _ = _config.TrySave(_configPath);
                    }
                });
            }

            if (hadEntry)
                _config.Plugins.Entries[pluginId] = previousEntry!;
            else
                _config.Plugins.Entries.Remove(pluginId);
            return Err<Action>(new StorageDaemonError("extension.bundle.config_save_failed", "Daemon plugin configuration could not be saved."));
        }
    }

    private string GetPluginDirectory(string pluginId) => Path.Combine(_pluginsRoot, pluginId);

    private static void ExtractBundle(string sourcePath, string destinationRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        using var archive = ZipFile.OpenRead(sourcePath);
        var fileCount = 0;
        long totalBytes = 0;
        var hasManifest = false;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            fileCount++;
            if (fileCount > MaxBundleFiles)
                throw new InvalidDataException($"Daemon extension bundle exceeds {MaxBundleFiles} files.");
            totalBytes += entry.Length;
            if (totalBytes > MaxBundleUncompressedBytes)
                throw new InvalidDataException($"Daemon extension bundle exceeds {MaxBundleUncompressedBytes} bytes.");

            var normalized = NormalizeArchivePath(entry.FullName);
            if (StringComparer.Ordinal.Equals(normalized, "mcsl-plugin.json"))
                hasManifest = true;
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, normalized));
            if (!IsWithinDirectory(destinationRoot, destinationPath))
                throw new InvalidDataException($"Daemon extension bundle entry '{entry.FullName}' escapes the bundle root.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }

        if (!hasManifest)
            throw new InvalidDataException("Daemon extension bundle must contain mcsl-plugin.json at the archive root.");
    }

    private static string NormalizeArchivePath(string path)
    {
        if (path.Contains("\\", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw new InvalidDataException($"Daemon extension bundle entry '{path}' has an invalid path.");
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
            throw new InvalidDataException($"Daemon extension bundle entry '{path}' has an invalid path.");
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool TryValidatePluginId(string value, out string? pluginId, out DaemonError? error)
    {
        try
        {
            pluginId = ProtocolIdentifier.ValidatePluginId(value, nameof(value));
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            pluginId = null;
            error = new ValidationDaemonError("extension.bundle.plugin_id_invalid", exception.Message);
            return false;
        }
    }

    private static bool IsWithinDirectory(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullCandidate.StartsWith(fullRoot, comparison) ||
               string.Equals(fullCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), comparison);
    }

    private static void RestorePluginDirectory(string target, string backup)
    {
        TryDeleteDirectory(target);
        if (Directory.Exists(backup))
            Directory.Move(backup, target);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static Result<ExtensionDaemonBundleDeploymentResult, DaemonError> Ok(ExtensionDaemonBundleDeploymentResult result) =>
        Result.Ok<ExtensionDaemonBundleDeploymentResult, DaemonError>(result);

    private static Result<ExtensionDaemonBundleDeploymentResult, DaemonError> Err(DaemonError error) =>
        Result.Err<ExtensionDaemonBundleDeploymentResult, DaemonError>(error);

    private static Result<T, DaemonError> Err<T>(DaemonError error)
        where T : notnull =>
        Result.Err<T, DaemonError>(error);

    [GeneratedRegex("^[a-f0-9]{64}$")]
    private static partial Regex Sha256Regex();
}
