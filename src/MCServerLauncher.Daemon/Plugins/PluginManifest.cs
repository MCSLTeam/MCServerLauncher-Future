using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginManifestPackageDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal sealed class PluginManifestEntryDocument
{
    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class PluginManifestRequiresDocument
{
    [JsonPropertyName("api")]
    public string? Api { get; set; }

    [JsonPropertyName("features")]
    public string[]? Features { get; set; }
}

internal sealed class PluginManifestPluginDependencyDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal sealed class PluginManifestContractDependencyDocument
{
    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}

internal sealed class PluginManifestDependenciesDocument
{
    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [JsonPropertyName("plugins")]
    public PluginManifestPluginDependencyDocument[]? Plugins { get; set; }

    [JsonPropertyName("contracts")]
    public PluginManifestContractDependencyDocument[]? Contracts { get; set; }
}

internal sealed class PluginManifestDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("package")]
    public PluginManifestPackageDocument? Package { get; set; }

    [JsonPropertyName("entry")]
    public PluginManifestEntryDocument? Entry { get; set; }

    [JsonPropertyName("requires")]
    public PluginManifestRequiresDocument? Requires { get; set; }

    [JsonPropertyName("dependencies")]
    public PluginManifestDependenciesDocument? Dependencies { get; set; }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PluginManifestDocument))]
internal partial class PluginHostJsonContext : JsonSerializerContext;

internal sealed record PluginManifestPluginDependency(
    string Id,
    VersionRange VersionRange)
{
    internal string NormalizedVersionRange => VersionRange.ToNormalizedString();
}

internal sealed record PluginManifestContractDependency(
    string Assembly,
    string AssemblyName,
    VersionRange VersionRange,
    string Sha256)
{
    internal string NormalizedVersionRange => VersionRange.ToNormalizedString();
}

internal sealed record PluginManifest(
    PluginIdentity Identity,
    string EntryAssembly,
    string EntryType,
    NuGetVersion Version,
    VersionRange ApiVersionRange,
    FrozenSet<PluginFeature> Features,
    ImmutableArray<PluginManifestPluginDependency> PluginDependencies,
    ImmutableArray<PluginManifestContractDependency> ContractDependencies,
    string BundleDirectory,
    string EntryAssemblyPath,
    string ManifestDigest)
{
    public bool HasFeature(PluginFeature feature) => Features.Contains(feature);
}

internal sealed record PluginDiscoveryFailure(
    string BundleDirectory,
    string Code,
    string Message,
    Exception? Exception = null);

internal sealed record PluginDiscoveryResult(
    ImmutableArray<PluginManifest> Plugins,
    ImmutableArray<PluginDiscoveryFailure> Failures);

internal static class PluginManifestReader
{
    internal const string ManifestFileName = "mcsl-plugin.json";
    internal const string CanonicalSchemaUri =
        "https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json";

    internal static PluginManifest ReadAndValidate(
        string bundleDirectory,
        string hostApiVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApiVersion);

        var fullBundleDirectory = Path.GetFullPath(bundleDirectory);
        if (!Directory.Exists(fullBundleDirectory))
            throw new PluginManifestException("bundle_missing", "The plugin bundle directory does not exist.");

        var manifestPath = Path.Combine(fullBundleDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new PluginManifestException("manifest_missing", "The plugin bundle does not contain mcsl-plugin.json.");

        PluginManifestDocument? document;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var json = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            document = json.RootElement.Deserialize(PluginHostJsonContext.Default.PluginManifestDocument);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new PluginManifestException("manifest_invalid", "The plugin manifest is not valid JSON.", exception);
        }

        if (document is null)
            throw new PluginManifestException("manifest_invalid", "The plugin manifest is empty.");

        if (document.Package is null)
            throw new PluginManifestException("manifest_field_missing", "The plugin manifest field 'package' is required.");
        if (document.Entry is null)
            throw new PluginManifestException("manifest_field_missing", "The plugin manifest field 'entry' is required.");
        if (document.Requires is null)
            throw new PluginManifestException("manifest_field_missing", "The plugin manifest field 'requires' is required.");
        if (document.Schema is not null && !string.Equals(document.Schema, CanonicalSchemaUri, StringComparison.Ordinal))
        {
            throw new PluginManifestException(
                "manifest_schema_invalid",
                $"The plugin manifest field '$schema' must be '{CanonicalSchemaUri}' when present.");
        }

        var id = Require(document.Package.Id, "package.id");
        var versionText = Require(document.Package.Version, "package.version");
        var entryAssembly = Require(document.Entry.Assembly, "entry.assembly");
        var entryType = Require(document.Entry.Type, "entry.type");
        var apiVersionText = Require(document.Requires.Api, "requires.api");

        PluginIdentity identity;
        NuGetVersion version;
        VersionRange apiVersionRange;
        try
        {
            version = NuGetVersion.Parse(versionText);
            apiVersionRange = VersionRange.Parse(apiVersionText);
            identity = new PluginIdentity(id, version.ToNormalizedString());
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new PluginManifestException("manifest_version_invalid", "The plugin id or version range is invalid.", exception);
        }

        NuGetVersion hostVersion;
        try
        {
            hostVersion = NuGetVersion.Parse(hostApiVersion);
        }
        catch (FormatException exception)
        {
            throw new PluginManifestException("host_api_version_invalid", "The daemon plugin API version is invalid.", exception);
        }

        if (!apiVersionRange.Satisfies(hostVersion))
        {
            throw new PluginManifestException(
                "api_version_unsupported",
                $"Plugin API range '{apiVersionText}' does not include host API version '{hostApiVersion}'.");
        }

        ValidateEntryName(entryAssembly, "entry.assembly");
        ValidateEntryType(entryType);
        var entryAssemblyPath = Path.Combine(fullBundleDirectory, entryAssembly);
        if (!File.Exists(entryAssemblyPath))
            throw new PluginManifestException("entry_missing", $"The plugin entry assembly '{entryAssembly}' does not exist.");

        var features = ParseFeatures(document.Requires.Features);
        var (pluginDependencies, contractDependencies) = ParseDependencies(document.Dependencies, identity.Id);

        var normalizedFeatures = features
            .Select(static feature => feature.Value)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var manifestDigest = PluginManifestDigest.Compute(
            identity.Id,
            identity.Version,
            entryAssembly,
            entryType,
            apiVersionRange.ToNormalizedString(),
            normalizedFeatures,
            pluginDependencies,
            contractDependencies);

        return new PluginManifest(
            identity,
            entryAssembly,
            entryType,
            version,
            apiVersionRange,
            features,
            pluginDependencies,
            contractDependencies,
            fullBundleDirectory,
            entryAssemblyPath,
            manifestDigest);
    }

    private static string Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PluginManifestException("manifest_field_missing", $"The plugin manifest field '{field}' is required.");
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new PluginManifestException("manifest_field_invalid", $"The plugin manifest field '{field}' must not contain surrounding whitespace.");
        return value;
    }

    private static void ValidateEntryName(string entryAssembly, string field)
    {
        if (!entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(entryAssembly), entryAssembly) ||
            Path.IsPathRooted(entryAssembly) ||
            entryAssembly.Any(char.IsControl))
        {
            throw new PluginManifestException("entry_invalid", $"The manifest field '{field}' must be a file name ending in .dll.");
        }
    }

    private static void ValidateEntryType(string entryType)
    {
        if (entryType.Contains(',', StringComparison.Ordinal) ||
            entryType.Contains('/', StringComparison.Ordinal) ||
            entryType.Contains('\\', StringComparison.Ordinal) ||
            entryType.Any(char.IsControl))
        {
            throw new PluginManifestException("entry_type_invalid", "The plugin entry type must be an unqualified CLR type name.");
        }
    }

    private static (ImmutableArray<PluginManifestPluginDependency> Plugins, ImmutableArray<PluginManifestContractDependency> Contracts) ParseDependencies(
        PluginManifestDependenciesDocument? dependencies,
        string packageId)
    {
        if (dependencies is null)
        {
            return (
                ImmutableArray<PluginManifestPluginDependency>.Empty,
                ImmutableArray<PluginManifestContractDependency>.Empty);
        }

        if (dependencies.Version is null)
            throw new PluginManifestException("dependencies_version_missing", "The plugin manifest dependencies.version field is required when dependencies is present.");
        if (dependencies.Version != 1)
            throw new PluginManifestException("dependencies_version_unsupported", "The plugin manifest dependencies.version field must be 1.");

        return (
            ParsePluginDependencies(dependencies.Plugins, packageId),
            ParseContractDependencies(dependencies.Contracts));
    }

    private static ImmutableArray<PluginManifestPluginDependency> ParsePluginDependencies(
        PluginManifestPluginDependencyDocument[]? plugins,
        string packageId)
    {
        plugins ??= [];
        var parsed = ImmutableArray.CreateBuilder<PluginManifestPluginDependency>(plugins.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in plugins)
        {
            if (dependency is null)
                throw new PluginManifestException("dependency_invalid", "Plugin dependency entries must be objects.");

            var id = Require(dependency.Id, "dependencies.plugins[].id");
            var versionText = Require(dependency.Version, "dependencies.plugins[].version");
            try
            {
                _ = ProtocolIdentifier.ValidatePluginId(id, nameof(id));
            }
            catch (ArgumentException exception)
            {
                throw new PluginManifestException("dependency_invalid", $"Plugin dependency id '{id}' is invalid.", exception);
            }

            if (string.Equals(id, packageId, StringComparison.Ordinal))
                throw new PluginManifestException("dependency_self", $"Plugin '{packageId}' must not depend on itself.");
            if (!seen.Add(id))
                throw new PluginManifestException("dependency_duplicate", $"Plugin dependency '{id}' is declared more than once.");

            VersionRange versionRange;
            try
            {
                versionRange = VersionRange.Parse(versionText);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                throw new PluginManifestException("dependency_invalid", $"Plugin dependency '{id}' has an invalid version range.", exception);
            }

            parsed.Add(new PluginManifestPluginDependency(id, versionRange));
        }

        return parsed
            .OrderBy(static dependency => dependency.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<PluginManifestContractDependency> ParseContractDependencies(
        PluginManifestContractDependencyDocument[]? contracts)
    {
        contracts ??= [];
        var parsed = ImmutableArray.CreateBuilder<PluginManifestContractDependency>(contracts.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in contracts)
        {
            if (dependency is null)
                throw new PluginManifestException("contract_dependency_invalid", "Contract dependency entries must be objects.");

            var assembly = Require(dependency.Assembly, "dependencies.contracts[].assembly");
            ValidateEntryName(assembly, "dependencies.contracts[].assembly");
            var assemblyName = Path.GetFileNameWithoutExtension(assembly);
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new PluginManifestException("contract_dependency_invalid", $"Contract dependency assembly '{assembly}' is invalid.");
            if (!seen.Add(assemblyName))
                throw new PluginManifestException("contract_dependency_duplicate", $"Contract dependency assembly '{assemblyName}' is declared more than once.");

            var versionText = Require(dependency.Version, "dependencies.contracts[].version");
            VersionRange versionRange;
            try
            {
                versionRange = VersionRange.Parse(versionText);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                throw new PluginManifestException("contract_dependency_invalid", $"Contract dependency '{assemblyName}' has an invalid version range.", exception);
            }

            var sha256 = Require(dependency.Sha256, "dependencies.contracts[].sha256").ToLowerInvariant();
            if (!IsSha256Hex(sha256))
                throw new PluginManifestException("contract_dependency_invalid", $"Contract dependency '{assemblyName}' has an invalid SHA-256 fingerprint.");

            parsed.Add(new PluginManifestContractDependency(assembly, assemblyName, versionRange, sha256));
        }

        return parsed
            .OrderBy(static dependency => dependency.AssemblyName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    private static FrozenSet<PluginFeature> ParseFeatures(string[]? values)
    {
        if (values is null)
            throw new PluginManifestException("features_missing", "The plugin manifest requires.features field is required.");

        var features = new HashSet<PluginFeature>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new PluginManifestException("feature_invalid", "Plugin feature values must be non-empty.");

            // Reject surrounding/internal whitespace by validating the raw value before any
            // normalization. A padded value like " rpc.register " must not be silently admitted
            // while "rpc/register" is misreported as feature_unsupported.
            if (value != value.Trim())
                throw new PluginManifestException("feature_invalid", $"Plugin feature '{value}' must not contain surrounding whitespace.");

            PluginFeature feature;
            try
            {
                feature = new PluginFeature(value);
            }
            catch (ArgumentException exception)
            {
                throw new PluginManifestException("feature_invalid", $"Plugin feature '{value}' is invalid.", exception);
            }

            if (!FeatureCatalog.TryGet(value, out var descriptor))
                throw new PluginManifestException("feature_unsupported", $"Plugin feature '{value}' is not supported.");

            if (!descriptor.IsImplemented)
            {
                throw new PluginManifestException(
                    "feature_unimplemented",
                    $"Plugin feature '{value}' is declared but not implemented by this host.");
            }

            if (!features.Add(feature))
                throw new PluginManifestException("feature_duplicate", $"Plugin feature '{value}' is declared more than once.");
        }

        // FrozenSet is unordered. The generator reports source ordering while both generator
        // metadata and runtime digest normalize the semantic feature set independently.
        return features.ToFrozenSet();
    }
}

internal sealed class PluginManifestException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
