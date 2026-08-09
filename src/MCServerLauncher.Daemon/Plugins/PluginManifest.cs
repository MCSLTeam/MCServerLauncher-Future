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

internal sealed class PluginManifestExtensionPointDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("replaces")]
    public string? Replaces { get; set; }

    [JsonPropertyName("conflicts")]
    public string[]? Conflicts { get; set; }

    [JsonPropertyName("defaultFor")]
    public string? DefaultFor { get; set; }
}

internal sealed class PluginManifestCommandDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }
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

    [JsonPropertyName("extensionPoints")]
    public PluginManifestExtensionPointDocument[]? ExtensionPoints { get; set; }

    [JsonPropertyName("commands")]
    public PluginManifestCommandDocument[]? Commands { get; set; }
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

internal sealed record PluginManifestExtensionPoint(
    string Id,
    PluginExtensionPointKind Kind,
    int Priority,
    string? Replaces,
    ImmutableArray<string> Conflicts,
    string? DefaultFor);

internal sealed record PluginManifestCommand(
    string Id,
    string? Title,
    string? Description,
    string Target);

internal sealed record PluginManifest(
    PluginIdentity Identity,
    string EntryAssembly,
    string EntryType,
    NuGetVersion Version,
    VersionRange ApiVersionRange,
    FrozenSet<PluginFeature> Features,
    ImmutableArray<PluginManifestPluginDependency> PluginDependencies,
    ImmutableArray<PluginManifestContractDependency> ContractDependencies,
    ImmutableArray<PluginManifestExtensionPoint> ExtensionPoints,
    ImmutableArray<PluginManifestCommand> Commands,
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
        var extensionPoints = ParseExtensionPoints(document.ExtensionPoints);
        var commands = ParseCommands(document.Commands, features, extensionPoints);

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
            contractDependencies,
            extensionPoints,
            commands);

        return new PluginManifest(
            identity,
            entryAssembly,
            entryType,
            version,
            apiVersionRange,
            features,
            pluginDependencies,
            contractDependencies,
            extensionPoints,
            commands,
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

    private static ImmutableArray<PluginManifestExtensionPoint> ParseExtensionPoints(PluginManifestExtensionPointDocument[]? extensionPoints)
    {
        if (extensionPoints is null)
            return ImmutableArray<PluginManifestExtensionPoint>.Empty;

        var parsed = ImmutableArray.CreateBuilder<PluginManifestExtensionPoint>(extensionPoints.Length);
        foreach (var document in extensionPoints)
        {
            if (document is null)
                throw new PluginManifestException("extension_point_invalid", "Extension point entries must be objects.");

            var id = Require(document.Id, "extensionPoints[].id");
            var kindText = Require(document.Kind, "extensionPoints[].kind");
            var kind = ParseExtensionPointKind(kindText);
            var priority = document.Priority ?? 0;
            var conflicts = ParseExtensionPointConflicts(document.Conflicts);
            parsed.Add(new PluginManifestExtensionPoint(
                id,
                kind,
                priority,
                NormalizeOptionalCanonicalString(document.Replaces, "extensionPoints[].replaces"),
                conflicts,
                NormalizeOptionalCanonicalString(document.DefaultFor, "extensionPoints[].defaultFor")));
        }

        var declarations = parsed.Select(static extensionPoint => new PluginExtensionPointDeclaration(
            extensionPoint.Id,
            extensionPoint.Kind,
            extensionPoint.Priority,
            extensionPoint.Replaces,
            extensionPoint.Conflicts,
            extensionPoint.DefaultFor));
        var admission = ExtensionPointCatalog.ValidateDeclarations(declarations);
        if (!admission.IsAdmitted)
            throw ToExtensionPointException(admission.Diagnostics[0]);

        return parsed
            .OrderBy(static extensionPoint => extensionPoint.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<string> ParseExtensionPointConflicts(string[]? conflicts)
    {
        if (conflicts is null)
            return ImmutableArray<string>.Empty;

        var parsed = ImmutableArray.CreateBuilder<string>(conflicts.Length);
        foreach (var conflict in conflicts)
            parsed.Add(Require(conflict, "extensionPoints[].conflicts[]"));
        return parsed
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string? NormalizeOptionalCanonicalString(string? value, string field)
    {
        if (value is null)
            return null;
        return Require(value, field);
    }

    private static ImmutableArray<PluginManifestCommand> ParseCommands(
        PluginManifestCommandDocument[]? commands,
        FrozenSet<PluginFeature> features,
        ImmutableArray<PluginManifestExtensionPoint> extensionPoints)
    {
        if (commands is null)
            return ImmutableArray<PluginManifestCommand>.Empty;

        var parsed = ImmutableArray.CreateBuilder<PluginManifestCommand>(commands.Length);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var document in commands)
        {
            if (document is null)
                throw new PluginManifestException("command_invalid", "Command entries must be objects.");

            var id = Require(document.Id, "commands[].id");
            ValidateCommandId(id, "commands[].id");
            if (!seen.Add(id))
                throw new PluginManifestException("command_duplicate", $"Command '{id}' is declared more than once.");

            var target = NormalizeOptionalCanonicalString(document.Target, "commands[].target") ?? "daemon";
            if (!StringComparer.Ordinal.Equals(target, "daemon"))
                throw new PluginManifestException("command_target_invalid", "Daemon plugin commands must use target 'daemon'.");

            parsed.Add(new PluginManifestCommand(
                id,
                NormalizeOptionalCanonicalString(document.Title, "commands[].title"),
                NormalizeOptionalCanonicalString(document.Description, "commands[].description"),
                target));
        }

        if (parsed.Count > 0)
        {
            if (!features.Contains(new PluginFeature("rpc.register")))
                throw new PluginManifestException("command_feature_missing", "Daemon plugin commands require feature 'rpc.register'.");
            if (!extensionPoints.Any(static extensionPoint =>
                    extensionPoint.Kind == PluginExtensionPointKind.Command &&
                    StringComparer.Ordinal.Equals(extensionPoint.Id, "command.daemon")))
            {
                throw new PluginManifestException("command_extension_point_missing", "Daemon plugin commands require extension point 'command.daemon'.");
            }
        }

        return parsed
            .OrderBy(static command => command.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void ValidateCommandId(string value, string field)
    {
        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
            var isDigit = character is >= '0' and <= '9';
            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1)
                    throw new PluginManifestException("command_id_invalid", $"The plugin manifest field '{field}' is not a canonical command id.");
                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter)
                throw new PluginManifestException("command_id_invalid", $"The plugin manifest field '{field}' is not a canonical command id.");
            if (!isAsciiLetter && !isDigit)
                throw new PluginManifestException("command_id_invalid", $"The plugin manifest field '{field}' is not a canonical command id.");
            segmentStart = false;
        }
    }

    private static PluginExtensionPointKind ParseExtensionPointKind(string value) => value switch
    {
        "provider" => PluginExtensionPointKind.Provider,
        "registry" => PluginExtensionPointKind.Registry,
        "pipeline" => PluginExtensionPointKind.Pipeline,
        "hook" => PluginExtensionPointKind.Hook,
        "override" => PluginExtensionPointKind.Override,
        "ui" => PluginExtensionPointKind.Ui,
        "event" => PluginExtensionPointKind.Event,
        "command" => PluginExtensionPointKind.Command,
        _ => throw new PluginManifestException("extension_point_kind_unknown", $"Extension point kind '{value}' is not supported.")
    };

    private static PluginManifestException ToExtensionPointException(string diagnostic)
    {
        var code = diagnostic.Split(':', 2)[0];
        return new PluginManifestException(code, $"Plugin extension point declaration is invalid: {diagnostic}.");
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
