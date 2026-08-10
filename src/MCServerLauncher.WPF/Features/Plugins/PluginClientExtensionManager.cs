using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginClientExtensionCacheEntry(
    string Id,
    string Version,
    PluginMpxDeploymentPlan DeploymentPlan,
    PluginUiSchema? UiSchema,
    PluginThemeRuntime? Theme,
    IReadOnlyList<PluginMpxCommand> Commands,
    IReadOnlyList<PluginMpxExtensionDependency> Dependencies,
    PluginMpxUpdatePolicy? UpdatePolicy,
    IReadOnlyList<string> HostCapabilities,
    IReadOnlyList<string> EventPermissions,
    PluginMpxSignatureTrust? Signature,
    IReadOnlyDictionary<string, string> FileDigests,
    IReadOnlyDictionary<string, string> CachedFiles);

public sealed record PluginClientExtensionManifestDrift(
    string Level,
    string Title,
    string Details);

public sealed record PluginClientExtensionInstallResult(
    bool Ok,
    PluginClientExtensionCacheEntry? Entry,
    string? Code,
    string? Message)
{
    public static PluginClientExtensionInstallResult Success(PluginClientExtensionCacheEntry entry) =>
        new(true, entry, null, null);

    public static PluginClientExtensionInstallResult Failure(string code, string message) =>
        new(false, null, code, message);
}

public sealed class PluginClientExtensionCacheStore(string rootDirectory)
{
    private const string MetadataFileName = "metadata.json";
    private const string PayloadDirectoryName = "payload";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));

    public void Save(PluginClientExtensionCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(_rootDirectory);
        var directory = EntryDirectory(entry.Id);
        Directory.CreateDirectory(directory);
        WriteDocument(directory, CacheDocument.From(entry, ToRelativeCachedFiles(directory, entry.CachedFiles)));
    }

    public PluginClientExtensionCacheEntry SavePackage(ValidatedPluginMpxPackage package, byte[] packageBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(packageBytes);
        if (!HasInstallableTarget(package.DeploymentPlan))
            throw new InvalidDataException("Package has no installable client or daemon deployment target.");

        var validation = PluginMpxPackageValidator.Validate(packageBytes);
        if (!validation.Ok || validation.Package is null)
            throw new InvalidDataException("Package bytes are not a valid .mpx package.");
        if (!StringComparer.Ordinal.Equals(validation.Package.PluginId, package.PluginId) ||
            !StringComparer.Ordinal.Equals(validation.Package.Version, package.Version) ||
            !SameDigests(validation.Package.FileDigests, package.FileDigests))
        {
            throw new InvalidDataException("Package bytes do not match the validated package metadata.");
        }

        Directory.CreateDirectory(_rootDirectory);
        var directory = EntryDirectory(package.PluginId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);

        var payloadRoot = Path.Combine(directory, PayloadDirectoryName);
        Directory.CreateDirectory(payloadRoot);
        Dictionary<string, string> cachedRelativeFiles = new(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (var packagePath in PackagePayloadPaths(package.DeploymentPlan))
        {
            var entry = archive.GetEntry(packagePath) ?? throw new InvalidDataException($"Package payload '{packagePath}' is missing.");
            var cachedPath = SafePayloadPath(payloadRoot, packagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(cachedPath)!);
            entry.ExtractToFile(cachedPath, overwrite: true);
            if (!package.FileDigests.TryGetValue(packagePath, out var expectedHash) ||
                !StringComparer.Ordinal.Equals(expectedHash, Sha256File(cachedPath)))
            {
                throw new InvalidDataException($"Cached payload '{packagePath}' hash did not match the manifest digest.");
            }

            cachedRelativeFiles[packagePath] = ToPortableRelativePath(directory, cachedPath);
        }

        var entryModel = CreateEntry(package, ResolveCachedFiles(directory, cachedRelativeFiles));
        WriteDocument(directory, CacheDocument.From(entryModel, cachedRelativeFiles));
        return entryModel;
    }

    public IReadOnlyList<PluginClientExtensionCacheEntry> LoadAll()
    {
        if (!Directory.Exists(_rootDirectory))
            return [];

        List<PluginClientExtensionCacheEntry> entries = [];
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory).Order(StringComparer.Ordinal))
        {
            var metadataPath = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(metadataPath))
                continue;

            try
            {
                var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(metadataPath), JsonOptions);
                if (document?.ToEntry(directory) is { } entry && HasInstallableTarget(entry.DeploymentPlan))
                    entries.Add(entry);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }
        }

        return new ReadOnlyCollection<PluginClientExtensionCacheEntry>(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
    }

    public bool Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var directory = EntryDirectory(id);
        if (!Directory.Exists(directory))
            return false;
        Directory.Delete(directory, recursive: true);
        return true;
    }

    private string EntryDirectory(string id)
    {
        if (id.Any(character => character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-'))
            throw new ArgumentException("Extension id is not a canonical cache key.", nameof(id));
        return Path.Combine(_rootDirectory, id);
    }

    public static bool HasInstallableTarget(PluginMpxDeploymentPlan deploymentPlan) =>
        deploymentPlan.Scopes.Contains("client", StringComparer.Ordinal) || deploymentPlan.Daemon?.Plugin is not null;

    private static PluginClientExtensionCacheEntry CreateEntry(
        ValidatedPluginMpxPackage package,
        IReadOnlyDictionary<string, string> cachedFiles) =>
        new(
            package.PluginId,
            package.Version,
            package.DeploymentPlan,
            package.UiSchema,
            package.Theme,
            new ReadOnlyCollection<PluginMpxCommand>(package.Commands.ToArray()),
            new ReadOnlyCollection<PluginMpxExtensionDependency>(package.Dependencies.ToArray()),
            package.UpdatePolicy,
            new ReadOnlyCollection<string>(package.HostCapabilities.ToArray()),
            new ReadOnlyCollection<string>(package.EventPermissions.ToArray()),
            package.Signature,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(package.FileDigests, StringComparer.Ordinal)),
            cachedFiles);

    private static IEnumerable<string> PackagePayloadPaths(PluginMpxDeploymentPlan deploymentPlan)
    {
        var client = deploymentPlan.Client;
        if (client?.Ui is not null)
            yield return client.Ui.Path;
        if (client?.Theme is not null)
            yield return client.Theme.Path;
        if (client?.Script is not null)
            yield return client.Script.Path;
        if (client is not null)
        {
            foreach (var resource in client.Resources)
                yield return resource.Path;
        }
        if (deploymentPlan.Daemon?.Plugin is not null)
            yield return deploymentPlan.Daemon.Plugin.Path;
    }

    private static void WriteDocument(string directory, CacheDocument document) =>
        File.WriteAllText(Path.Combine(directory, MetadataFileName), JsonSerializer.Serialize(document, JsonOptions));

    private static Dictionary<string, string> ToRelativeCachedFiles(
        string entryDirectory,
        IReadOnlyDictionary<string, string> cachedFiles)
    {
        Dictionary<string, string> relative = new(StringComparer.Ordinal);
        foreach (var (packagePath, cachedPath) in cachedFiles)
            relative[packagePath] = ToPortableRelativePath(entryDirectory, cachedPath);
        return relative;
    }

    private static IReadOnlyDictionary<string, string> ResolveCachedFiles(
        string entryDirectory,
        IReadOnlyDictionary<string, string> cachedFiles)
    {
        Dictionary<string, string> resolved = new(StringComparer.Ordinal);
        foreach (var (packagePath, relativePath) in cachedFiles)
            resolved[packagePath] = SafeCachedRelativePath(entryDirectory, relativePath);
        return new ReadOnlyDictionary<string, string>(resolved);
    }

    private static string SafePayloadPath(string payloadRoot, string packagePath)
    {
        var root = Path.GetFullPath(payloadRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Payload path '{packagePath}' escapes the cache directory.");
        return candidate;
    }

    private static string SafeCachedRelativePath(string entryDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Cached file paths must be relative to the extension cache entry.", nameof(relativePath));
        var root = Path.GetFullPath(entryDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Cached file paths must stay inside the extension cache entry.", nameof(relativePath));
        return candidate;
    }

    private static string ToPortableRelativePath(string entryDirectory, string cachedPath) =>
        Path.GetRelativePath(entryDirectory, cachedPath).Replace(Path.DirectorySeparatorChar, '/');

    private static bool SameDigests(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record CacheDocument(
        string Id,
        string Version,
        PluginMpxDeploymentPlan DeploymentPlan,
        string? UiRuntimeJson,
        string? ThemeRuntimeJson,
        IReadOnlyList<PluginMpxCommand>? Commands,
        IReadOnlyList<PluginMpxExtensionDependency>? Dependencies,
        PluginMpxUpdatePolicy? UpdatePolicy,
        IReadOnlyList<string>? HostCapabilities,
        IReadOnlyList<string>? EventPermissions,
        PluginMpxSignatureTrust? Signature,
        Dictionary<string, string> FileDigests,
        Dictionary<string, string> CachedFiles)
    {
        internal static CacheDocument From(
            PluginClientExtensionCacheEntry entry,
            Dictionary<string, string> cachedFiles) => new(
            entry.Id,
            entry.Version,
            entry.DeploymentPlan,
            entry.UiSchema is null ? null : PluginUiSchemaCompiler.SerializeRuntimeJson(entry.UiSchema),
            entry.Theme is null ? null : PluginThemeRuntimeParser.SerializeRuntimeJson(entry.Theme),
            new ReadOnlyCollection<PluginMpxCommand>(entry.Commands.ToArray()),
            new ReadOnlyCollection<PluginMpxExtensionDependency>(entry.Dependencies.ToArray()),
            entry.UpdatePolicy,
            new ReadOnlyCollection<string>(entry.HostCapabilities.ToArray()),
            new ReadOnlyCollection<string>(entry.EventPermissions.ToArray()),
            entry.Signature,
            new Dictionary<string, string>(entry.FileDigests, StringComparer.Ordinal),
            new Dictionary<string, string>(cachedFiles, StringComparer.Ordinal));

        internal PluginClientExtensionCacheEntry? ToEntry(string entryDirectory)
        {
            PluginUiSchema? uiSchema = null;
            if (UiRuntimeJson is not null)
            {
                var parsed = PluginUiSchemaParser.ParseRuntime(UiRuntimeJson);
                if (!parsed.Ok || parsed.Schema is null)
                    return null;
                uiSchema = parsed.Schema;
            }

            PluginThemeRuntime? theme = null;
            if (ThemeRuntimeJson is not null)
            {
                var parsed = PluginThemeRuntimeParser.ParseRuntime(ThemeRuntimeJson);
                if (!parsed.Ok || parsed.Theme is null)
                    return null;
                theme = parsed.Theme;
            }

            var cachedFiles = ResolveCachedFiles(entryDirectory, CachedFiles ?? []);
            if (DeploymentPlan.Daemon?.Plugin is { } daemonBundle && !cachedFiles.ContainsKey(daemonBundle.Path))
                return null;
            foreach (var (packagePath, cachedPath) in cachedFiles)
            {
                if (!File.Exists(cachedPath))
                    return null;
                if (FileDigests.TryGetValue(packagePath, out var expectedHash) &&
                    !StringComparer.Ordinal.Equals(expectedHash, Sha256File(cachedPath)))
                    return null;
            }

            return new PluginClientExtensionCacheEntry(
                Id,
                Version,
                DeploymentPlan,
                uiSchema,
                theme,
                new ReadOnlyCollection<PluginMpxCommand>((Commands ?? []).ToArray()),
                new ReadOnlyCollection<PluginMpxExtensionDependency>((Dependencies ?? []).ToArray()),
                UpdatePolicy,
                new ReadOnlyCollection<string>((HostCapabilities ?? []).ToArray()),
                new ReadOnlyCollection<string>((EventPermissions ?? []).ToArray()),
                Signature,
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(FileDigests, StringComparer.Ordinal)),
                cachedFiles);
        }
    }
}

public sealed class PluginClientExtensionManager(PluginClientExtensionCacheStore? store = null)
{
    private readonly PluginClientExtensionCacheStore? _store = store;
    private readonly Dictionary<string, PluginClientExtensionCacheEntry> _entries = new(StringComparer.Ordinal);

    public PluginClientExtensionInstallResult Install(ValidatedPluginMpxPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!PluginClientExtensionCacheStore.HasInstallableTarget(package.DeploymentPlan))
            return PluginClientExtensionInstallResult.Failure("deployment_target_missing", "Package has no installable client or daemon deployment target.");
        if (ValidatePackageDependencies(package) is { } dependencyFailure)
            return dependencyFailure;

        var entry = new PluginClientExtensionCacheEntry(
            package.PluginId,
            package.Version,
            package.DeploymentPlan,
            package.UiSchema,
            package.Theme,
            new ReadOnlyCollection<PluginMpxCommand>(package.Commands.ToArray()),
            new ReadOnlyCollection<PluginMpxExtensionDependency>(package.Dependencies.ToArray()),
            package.UpdatePolicy,
            new ReadOnlyCollection<string>(package.HostCapabilities.ToArray()),
            new ReadOnlyCollection<string>(package.EventPermissions.ToArray()),
            package.Signature,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(package.FileDigests, StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)));
        _entries[entry.Id] = entry;
        return PluginClientExtensionInstallResult.Success(entry);
    }

    public PluginClientExtensionInstallResult InstallPersisted(ValidatedPluginMpxPackage package)
    {
        var result = Install(package);
        if (result.Ok && result.Entry is not null)
            _store?.Save(result.Entry);
        return result;
    }

    public PluginClientExtensionInstallResult InstallPersisted(ValidatedPluginMpxPackage package, byte[] packageBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(packageBytes);
        if (!PluginClientExtensionCacheStore.HasInstallableTarget(package.DeploymentPlan))
            return PluginClientExtensionInstallResult.Failure("deployment_target_missing", "Package has no installable client or daemon deployment target.");
        if (ValidatePackageDependencies(package) is { } dependencyFailure)
            return dependencyFailure;

        if (_store is null)
            return Install(package);

        try
        {
            var entry = _store.SavePackage(package, packageBytes);
            _entries[entry.Id] = entry;
            return PluginClientExtensionInstallResult.Success(entry);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PluginClientExtensionInstallResult.Failure("cache_write_failed", exception.Message);
        }
    }

    private PluginClientExtensionInstallResult? ValidatePackageDependencies(ValidatedPluginMpxPackage package)
    {
        foreach (var dependency in package.Dependencies)
        {
            if (!_entries.TryGetValue(dependency.Id, out var installed))
            {
                return PluginClientExtensionInstallResult.Failure(
                    "dependency_missing",
                    $"Extension '{package.PluginId}' requires '{dependency.Id}' {dependency.Version}.");
            }

            if (!PluginMpxPackageValidator.IsDependencyVersionSatisfied(dependency.Version, installed.Version))
            {
                return PluginClientExtensionInstallResult.Failure(
                    "dependency_version_unsupported",
                    $"Extension '{package.PluginId}' requires '{dependency.Id}' {dependency.Version}, but installed version is {installed.Version}.");
            }
        }

        return null;
    }

    public void Restore()
    {
        if (_store is null)
            return;
        _entries.Clear();
        foreach (var entry in _store.LoadAll())
            _entries[entry.Id] = entry;
    }

    public PluginClientExtensionCacheEntry? Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _entries.GetValueOrDefault(id);
    }

    public bool Uninstall(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _entries.Remove(id);
    }

    public bool UninstallPersisted(string id)
    {
        var removed = Uninstall(id);
        if (removed)
            _store?.Delete(id);
        return removed;
    }

    public IReadOnlyList<PluginClientExtensionCacheEntry> List() =>
        new ReadOnlyCollection<PluginClientExtensionCacheEntry>(_entries.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());

    public IReadOnlyList<PluginClientExtensionManifestDrift> BuildManifestDrift(ValidatedPluginMpxPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return BuildManifestDrift(_entries.GetValueOrDefault(package.PluginId), package);
    }

    public static IReadOnlyList<PluginClientExtensionManifestDrift> BuildManifestDrift(
        PluginClientExtensionCacheEntry? installed,
        ValidatedPluginMpxPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (installed is null)
            return [];

        List<PluginClientExtensionManifestDrift> findings = [];
        if (!StringComparer.Ordinal.Equals(installed.Version, package.Version))
            findings.Add(new PluginClientExtensionManifestDrift("low", "Version change", $"{installed.Version} -> {package.Version}"));

        AddStringSetDrift(
            findings,
            "Host permissions",
            installed.HostCapabilities,
            package.HostCapabilities,
            value => value.StartsWith("daemon.", StringComparison.Ordinal) ? "medium" : "low");
        AddStringSetDrift(
            findings,
            "Event permissions",
            installed.EventPermissions,
            package.EventPermissions,
            _ => "medium");
        AddStringSetDrift(
            findings,
            "Extension points",
            ExtensionPointKeys(installed.DeploymentPlan.Daemon?.ExtensionPoints ?? []),
            ExtensionPointKeys(package.DeploymentPlan.Daemon?.ExtensionPoints ?? []),
            value => value.StartsWith("override:override.process.launcher", StringComparison.Ordinal) ? "high" : "medium");
        AddStringSetDrift(
            findings,
            "Daemon commands",
            installed.Commands.Select(command => command.Id),
            package.Commands.Select(command => command.Id),
            _ => "medium");
        AddStringSetDrift(
            findings,
            "Extension dependencies",
            installed.Dependencies.Select(DependencyKey),
            package.Dependencies.Select(DependencyKey),
            _ => "medium");

        var previousDaemon = FileRefKey(installed.DeploymentPlan.Daemon?.Plugin);
        var nextDaemon = FileRefKey(package.DeploymentPlan.Daemon?.Plugin);
        if (!StringComparer.Ordinal.Equals(previousDaemon, nextDaemon))
        {
            var level = previousDaemon is null || nextDaemon is null ? "high" : "medium";
            findings.Add(new PluginClientExtensionManifestDrift(level, "Daemon payload", $"{previousDaemon ?? "none"} -> {nextDaemon ?? "none"}"));
        }

        var previousSignature = SignatureKey(installed.Signature);
        var nextSignature = SignatureKey(package.Signature);
        if (!StringComparer.Ordinal.Equals(previousSignature, nextSignature))
        {
            var level = installed.Signature is not null && package.Signature is null ? "high" : "medium";
            findings.Add(new PluginClientExtensionManifestDrift(level, "Signature trust", $"{previousSignature ?? "unsigned"} -> {nextSignature ?? "unsigned"}"));
        }

        return new ReadOnlyCollection<PluginClientExtensionManifestDrift>(findings);
    }

    private static void AddStringSetDrift(
        List<PluginClientExtensionManifestDrift> findings,
        string title,
        IEnumerable<string> previousValues,
        IEnumerable<string> nextValues,
        Func<string, string> addedLevel)
    {
        var previous = previousValues.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        var next = nextValues.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        var added = next.Except(previous, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var removed = previous.Except(next, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (added.Length == 0 && removed.Length == 0)
            return;

        var details = string.Join(" ", new[]
        {
            added.Length == 0 ? string.Empty : $"Added: {string.Join(", ", added)}.",
            removed.Length == 0 ? string.Empty : $"Removed: {string.Join(", ", removed)}."
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        findings.Add(new PluginClientExtensionManifestDrift(
            added.Length == 0 ? "low" : HighestLevel(added.Select(addedLevel)),
            title,
            details));
    }

    private static string HighestLevel(IEnumerable<string> levels)
    {
        var materialized = levels.ToArray();
        if (materialized.Contains("high", StringComparer.Ordinal))
            return "high";
        return materialized.Contains("medium", StringComparer.Ordinal) ? "medium" : "low";
    }

    private static IEnumerable<string> ExtensionPointKeys(IEnumerable<PluginMpxExtensionPoint> points) =>
        points.Select(point => $"{point.Kind}:{point.Id}{(string.IsNullOrWhiteSpace(point.Target) ? string.Empty : $":{point.Target}")}");

    private static string DependencyKey(PluginMpxExtensionDependency dependency) =>
        $"{dependency.Id} {dependency.Version}";

    private static string? FileRefKey(PluginMpxDeploymentFile? file) =>
        file is null ? null : $"{file.Path}@{file.Sha256}";

    private static string? SignatureKey(PluginMpxSignatureTrust? signature) =>
        signature is null ? null : $"{signature.Publisher}/{signature.KeyId}/{signature.PublicKeySha256}";
}
