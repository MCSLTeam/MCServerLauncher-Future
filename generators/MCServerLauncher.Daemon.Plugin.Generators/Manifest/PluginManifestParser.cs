using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugin.Generators.Manifest;

internal enum PluginManifestIssueKind
{
    UnknownFeature,
    DuplicateFeature,
    ConflictingFeature,
}

internal sealed class PluginManifestIssue
{
    public PluginManifestIssue(PluginManifestIssueKind kind, string value, string? conflictingValue = null)
    {
        Kind = kind;
        Value = value;
        ConflictingValue = conflictingValue;
    }

    public PluginManifestIssueKind Kind { get; }

    public string Value { get; }

    public string? ConflictingValue { get; }
}

internal sealed class ParsedPluginDependency
{
    public ParsedPluginDependency(string id, string versionRange)
    {
        Id = id;
        VersionRange = versionRange;
    }

    public string Id { get; }

    public string VersionRange { get; }
}

internal sealed class ParsedContractDependency
{
    public ParsedContractDependency(string assembly, string assemblyName, string versionRange, string sha256)
    {
        Assembly = assembly;
        AssemblyName = assemblyName;
        VersionRange = versionRange;
        Sha256 = sha256;
    }

    public string Assembly { get; }

    public string AssemblyName { get; }

    public string VersionRange { get; }

    public string Sha256 { get; }
}

internal sealed class ParsedExtensionPoint
{
    public ParsedExtensionPoint(
        string id,
        string kind,
        int priority,
        string? replaces,
        IReadOnlyList<string> conflicts,
        string? defaultFor)
    {
        Id = id;
        Kind = kind;
        Priority = priority;
        Replaces = replaces;
        Conflicts = conflicts;
        DefaultFor = defaultFor;
    }

    public string Id { get; }

    public string Kind { get; }

    public int Priority { get; }

    public string? Replaces { get; }

    public IReadOnlyList<string> Conflicts { get; }

    public string? DefaultFor { get; }
}

internal sealed class ParsedCommand
{
    public ParsedCommand(string id, string? title, string? description, string target)
    {
        Id = id;
        Title = title;
        Description = description;
        Target = target;
    }

    public string Id { get; }

    public string? Title { get; }

    public string? Description { get; }

    public string Target { get; }
}

internal sealed class ParsedPluginManifest
{
    public ParsedPluginManifest(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        IReadOnlyList<string> sourceFeatures,
        IReadOnlyList<string> features,
        IReadOnlyList<ParsedPluginDependency> pluginDependencies,
        IReadOnlyList<ParsedContractDependency> contractDependencies,
        IReadOnlyList<ParsedCommand> commands,
        string digest,
        bool apiRangeSupported,
        IReadOnlyList<PluginManifestIssue> issues,
        string? error)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        EntryAssembly = entryAssembly;
        EntryType = entryType;
        ApiRange = apiRange;
        SourceFeatures = sourceFeatures;
        Features = features;
        PluginDependencies = pluginDependencies;
        ContractDependencies = contractDependencies;
        Commands = commands;
        Digest = digest;
        ApiRangeSupported = apiRangeSupported;
        Issues = issues;
        Error = error;
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public string EntryAssembly { get; }

    public string EntryType { get; }

    public string ApiRange { get; }

    public IReadOnlyList<string> SourceFeatures { get; }

    public IReadOnlyList<string> Features { get; }

    public IReadOnlyList<ParsedPluginDependency> PluginDependencies { get; }

    public IReadOnlyList<ParsedContractDependency> ContractDependencies { get; }

    public IReadOnlyList<ParsedCommand> Commands { get; }

    public string Digest { get; }

    public bool ApiRangeSupported { get; }

    public IReadOnlyList<PluginManifestIssue> Issues { get; }

    public string? Error { get; }

    public bool IsStructurallyValid => Error is null;

    public bool HasFeatureErrors => Issues.Count > 0;
}

internal static class PluginManifestParser
{
    internal const string CanonicalSchemaUri =
        "https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json";

    private const string HostApiVersion = "1.0.0";
    private const string DigestDomain = "mcsl-plugin-manifest-v2";

    private static readonly HashSet<string> KnownFeatures = new(StringComparer.Ordinal)
    {
        "rpc.register",
        "event.publish",
        "event.subscribe",
        "instance.query",
        "instance.manage",
        "file.read",
        "file.write",
        "system.query",
        "event-rule.manage",
        "operation.query",
        "operation.cancel",
        "provisioning.manage",
        "backup.manage",
        "monitoring.query",
        "automation.manage",
        "audit.query",
        "storage.private",
        "network.http.listen",
        "auth.verify",
    };

    // No Preview-1 feature pairs conflict. Keep the table explicit so future vocabulary
    // additions cannot silently skip the required diagnostic.
    private static readonly HashSet<string> FeatureConflicts = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> KnownExtensionPoints = new(StringComparer.Ordinal)
    {
        ["provider.java"] = "provider",
        ["provider.serverCore"] = "provider",
        ["provider.backup"] = "provider",
        ["provider.processLauncher"] = "provider",
        ["registry.template"] = "registry",
        ["registry.serverCore"] = "registry",
        ["registry.icon"] = "registry",
        ["registry.formatter"] = "registry",
        ["registry.command"] = "registry",
        ["pipeline.instance.create"] = "pipeline",
        ["hook.instance.lifecycle"] = "hook",
        ["override.process.launcher"] = "override",
        ["ui.panel"] = "ui",
        ["event.daemon"] = "event",
        ["command.daemon"] = "command",
    };

    private static readonly HashSet<string> KnownExtensionPointKinds = new(StringComparer.Ordinal)
    {
        "provider",
        "registry",
        "pipeline",
        "hook",
        "override",
        "ui",
        "event",
        "command",
    };

    public static ParsedPluginManifest Parse(string json, string pathHint)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return Fail("Manifest is empty.");

            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowDuplicateProperties = false,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });

            var root = RequireObject(document.RootElement, "$");
            ValidateProperties(root, "$", "$schema", "package", "entry", "requires", "dependencies", "extensionPoints", "commands");

            var schema = ReadOptionalString(root, "$schema", "$");
            if (schema is not null && !string.Equals(schema, CanonicalSchemaUri, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Field '$schema' must be '{CanonicalSchemaUri}' when present.");
            }

            var package = RequireObjectProperty(root, "package", "$");
            ValidateProperties(package, "$.package", "id", "version");
            var packageId = RequireString(package, "id", "$.package");
            ValidatePluginId(packageId, "package.id");
            var packageVersionText = RequireString(package, "version", "$.package");
            if (!NuGetVersion.TryParse(packageVersionText, out var packageVersion))
                throw new InvalidOperationException("Field 'package.version' is not a valid NuGet version.");
            var normalizedPackageVersion = packageVersion.ToNormalizedString();

            var entry = RequireObjectProperty(root, "entry", "$");
            ValidateProperties(entry, "$.entry", "assembly", "type");
            var entryAssembly = RequireString(entry, "assembly", "$.entry");
            ValidateEntryAssembly(entryAssembly, "entry.assembly");
            var entryType = RequireString(entry, "type", "$.entry");
            ValidateEntryType(entryType);

            var requires = RequireObjectProperty(root, "requires", "$");
            ValidateProperties(requires, "$.requires", "api", "features");
            var apiRangeText = RequireString(requires, "api", "$.requires");
            if (!VersionRange.TryParse(apiRangeText, out var apiRange))
                throw new InvalidOperationException("Field 'requires.api' is not a valid NuGet version range.");
            var normalizedApiRange = apiRange.ToNormalizedString();
            var apiRangeSupported = apiRange.Satisfies(NuGetVersion.Parse(HostApiVersion));

            if (!requires.TryGetProperty("features", out var featuresElement))
                throw new InvalidOperationException("Field 'requires.features' is required.");
            if (featuresElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Field 'requires.features' must be an array.");

            var sourceFeatures = new List<string>();
            var normalizedFeatures = new List<string>();
            var issues = new List<PluginManifestIssue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in featuresElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("Every 'requires.features' item must be a string.");

                var value = item.GetString() ?? string.Empty;
                ValidateFeatureValue(value);
                sourceFeatures.Add(value);

                if (!KnownFeatures.Contains(value))
                    issues.Add(new PluginManifestIssue(PluginManifestIssueKind.UnknownFeature, value));
                if (!seen.Add(value))
                {
                    issues.Add(new PluginManifestIssue(PluginManifestIssueKind.DuplicateFeature, value));
                    continue;
                }

                normalizedFeatures.Add(value);
            }

            normalizedFeatures.Sort(StringComparer.Ordinal);
            foreach (var feature in normalizedFeatures)
            {
                foreach (var other in normalizedFeatures)
                {
                    if (StringComparer.Ordinal.Compare(feature, other) >= 0)
                        continue;
                    if (FeatureConflicts.Contains(feature + "\n" + other))
                    {
                        issues.Add(new PluginManifestIssue(
                            PluginManifestIssueKind.ConflictingFeature,
                            feature,
                            other));
                    }
                }
            }

            var (pluginDependencies, contractDependencies) = ParseDependencies(root, packageId);
            var extensionPoints = ParseExtensionPoints(root);
            var commands = ParseCommands(root, normalizedFeatures, extensionPoints);
            var digest = ComputeNormalizedDigest(
                packageId,
                normalizedPackageVersion,
                entryAssembly,
                entryType,
                normalizedApiRange,
                normalizedFeatures,
                pluginDependencies,
                contractDependencies,
                extensionPoints,
                commands);

            return new ParsedPluginManifest(
                packageId,
                normalizedPackageVersion,
                entryAssembly,
                entryType,
                normalizedApiRange,
                sourceFeatures,
                normalizedFeatures,
                pluginDependencies,
                contractDependencies,
                commands,
                digest,
                apiRangeSupported,
                issues,
                error: null);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return Fail($"{pathHint}: {exception.Message}");
        }
    }

    public static bool IsFeatureKnown(string value) => KnownFeatures.Contains(value);

    private static (List<ParsedPluginDependency> Plugins, List<ParsedContractDependency> Contracts) ParseDependencies(JsonElement root, string packageId)
    {
        if (!root.TryGetProperty("dependencies", out var dependenciesElement))
            return (new List<ParsedPluginDependency>(), new List<ParsedContractDependency>());

        var dependencies = RequireObject(dependenciesElement, "$.dependencies");
        ValidateProperties(dependencies, "$.dependencies", "version", "plugins", "contracts");
        if (!dependencies.TryGetProperty("version", out var versionElement))
            throw new InvalidOperationException("Field 'dependencies.version' is required.");
        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version) || version != 1)
            throw new InvalidOperationException("Field 'dependencies.version' must be 1.");

        return (
            ParsePluginDependencies(dependencies, packageId),
            ParseContractDependencies(dependencies));
    }

    private static List<ParsedPluginDependency> ParsePluginDependencies(JsonElement dependencies, string packageId)
    {
        if (!dependencies.TryGetProperty("plugins", out var pluginsElement))
            return new List<ParsedPluginDependency>();
        if (pluginsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Field 'dependencies.plugins' must be an array.");

        var parsed = new List<ParsedPluginDependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pluginElement in pluginsElement.EnumerateArray())
        {
            var plugin = RequireObject(pluginElement, "$.dependencies.plugins[]");
            ValidateProperties(plugin, "$.dependencies.plugins[]", "id", "version");
            var id = RequireString(plugin, "id", "$.dependencies.plugins[]");
            ValidatePluginId(id, "dependencies.plugins[].id");
            if (string.Equals(id, packageId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Plugin dependency '{id}' must not reference the current plugin.");
            if (!seen.Add(id))
                throw new InvalidOperationException($"Plugin dependency '{id}' is declared more than once.");

            var versionText = RequireString(plugin, "version", "$.dependencies.plugins[]");
            if (!VersionRange.TryParse(versionText, out var versionRange))
                throw new InvalidOperationException("Field 'dependencies.plugins[].version' is not a valid NuGet version range.");
            parsed.Add(new ParsedPluginDependency(id, versionRange.ToNormalizedString()));
        }

        parsed.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return parsed;
    }

    private static List<ParsedContractDependency> ParseContractDependencies(JsonElement dependencies)
    {
        if (!dependencies.TryGetProperty("contracts", out var contractsElement))
            return new List<ParsedContractDependency>();
        if (contractsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Field 'dependencies.contracts' must be an array.");

        var parsed = new List<ParsedContractDependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contractElement in contractsElement.EnumerateArray())
        {
            var contract = RequireObject(contractElement, "$.dependencies.contracts[]");
            ValidateProperties(contract, "$.dependencies.contracts[]", "assembly", "version", "sha256");
            var assembly = RequireString(contract, "assembly", "$.dependencies.contracts[]");
            ValidateEntryAssembly(assembly, "dependencies.contracts[].assembly");
            var assemblyName = Path.GetFileNameWithoutExtension(assembly);
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new InvalidOperationException($"Contract dependency assembly '{assembly}' is invalid.");
            if (!seen.Add(assemblyName))
                throw new InvalidOperationException($"Contract dependency assembly '{assemblyName}' is declared more than once.");

            var versionText = RequireString(contract, "version", "$.dependencies.contracts[]");
            if (!VersionRange.TryParse(versionText, out var versionRange))
                throw new InvalidOperationException("Field 'dependencies.contracts[].version' is not a valid NuGet version range.");

            var sha256 = RequireString(contract, "sha256", "$.dependencies.contracts[]").ToLowerInvariant();
            if (!IsSha256Hex(sha256))
                throw new InvalidOperationException("Field 'dependencies.contracts[].sha256' is not a SHA-256 hex fingerprint.");

            parsed.Add(new ParsedContractDependency(assembly, assemblyName, versionRange.ToNormalizedString(), sha256));
        }

        parsed.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.AssemblyName, right.AssemblyName));
        return parsed;
    }

    private static List<ParsedExtensionPoint> ParseExtensionPoints(JsonElement root)
    {
        if (!root.TryGetProperty("extensionPoints", out var extensionPointsElement))
            return new List<ParsedExtensionPoint>();
        if (extensionPointsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Field 'extensionPoints' must be an array.");

        var parsed = new List<ParsedExtensionPoint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extensionPointElement in extensionPointsElement.EnumerateArray())
        {
            var extensionPoint = RequireObject(extensionPointElement, "$.extensionPoints[]");
            ValidateProperties(extensionPoint, "$.extensionPoints[]", "id", "kind", "priority", "replaces", "conflicts", "defaultFor");
            var id = RequireString(extensionPoint, "id", "$.extensionPoints[]");
            var kind = RequireString(extensionPoint, "kind", "$.extensionPoints[]");
            if (!KnownExtensionPointKinds.Contains(kind))
                throw new InvalidOperationException($"Extension point kind '{kind}' is not supported.");
            if (!KnownExtensionPoints.TryGetValue(id, out var expectedKind))
                throw new InvalidOperationException($"Extension point id '{id}' is not supported.");
            if (!string.Equals(kind, expectedKind, StringComparison.Ordinal))
                throw new InvalidOperationException($"Extension point '{id}' must use kind '{expectedKind}'.");
            if (!seen.Add(id))
                throw new InvalidOperationException($"Extension point id '{id}' is declared more than once.");

            var priority = ReadOptionalInt(extensionPoint, "priority", "$.extensionPoints[]") ?? 0;
            if (priority is < -1000 or > 1000)
                throw new InvalidOperationException($"Extension point '{id}' priority is outside the supported range.");
            var replaces = ReadOptionalString(extensionPoint, "replaces", "$.extensionPoints[]");
            if (!string.IsNullOrWhiteSpace(replaces) && kind != "override")
                throw new InvalidOperationException($"Extension point '{id}' cannot replace another provider.");
            var conflicts = ReadOptionalStringArray(extensionPoint, "conflicts", "$.extensionPoints[]");
            var defaultFor = ReadOptionalString(extensionPoint, "defaultFor", "$.extensionPoints[]");
            parsed.Add(new ParsedExtensionPoint(id, kind, priority, replaces, conflicts, defaultFor));
        }

        foreach (var extensionPoint in parsed)
        {
            foreach (var conflict in extensionPoint.Conflicts)
            {
                if (seen.Contains(conflict))
                    throw new InvalidOperationException($"Extension point '{extensionPoint.Id}' conflicts with declared extension point '{conflict}'.");
            }
        }

        parsed.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return parsed;
    }

    private static List<ParsedCommand> ParseCommands(
        JsonElement root,
        IReadOnlyList<string> features,
        IReadOnlyList<ParsedExtensionPoint> extensionPoints)
    {
        if (!root.TryGetProperty("commands", out var commandsElement))
            return new List<ParsedCommand>();
        if (commandsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Field 'commands' must be an array.");

        var parsed = new List<ParsedCommand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commandElement in commandsElement.EnumerateArray())
        {
            var command = RequireObject(commandElement, "$.commands[]");
            ValidateProperties(command, "$.commands[]", "id", "title", "description", "target");
            var id = RequireString(command, "id", "$.commands[]");
            ValidateCommandId(id, "commands[].id");
            if (!seen.Add(id))
                throw new InvalidOperationException($"Command id '{id}' is declared more than once.");

            var title = ReadOptionalString(command, "title", "$.commands[]");
            var description = ReadOptionalString(command, "description", "$.commands[]");
            var target = ReadOptionalString(command, "target", "$.commands[]") ?? "daemon";
            if (!string.Equals(target, "daemon", StringComparison.Ordinal))
                throw new InvalidOperationException("Field 'commands[].target' must be 'daemon'.");
            parsed.Add(new ParsedCommand(id, title, description, target));
        }

        if (parsed.Count > 0)
        {
            if (!features.Contains("rpc.register", StringComparer.Ordinal))
                throw new InvalidOperationException("Field 'commands' requires feature 'rpc.register'.");
            if (!extensionPoints.Any(static extensionPoint => extensionPoint.Kind == "command" && extensionPoint.Id == "command.daemon"))
                throw new InvalidOperationException("Field 'commands' requires extension point 'command.daemon'.");
        }

        parsed.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return parsed;
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

    private static JsonElement RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Field '{path}' must be an object.");
        return value;
    }

    private static JsonElement RequireObjectProperty(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' is required.");
        return RequireObject(value, DisplayPath(path, name));
    }

    private static string RequireString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' is required.");
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must be a string.");

        var text = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' is required.");
        if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must not contain surrounding whitespace.");
        return text;
    }

    private static string? ReadOptionalString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must be a string.");

        var text = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must be a non-empty canonical string.");
        return text;
    }

    private static int? ReadOptionalInt(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var integer))
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must be an integer.");
        return integer;
    }

    private static IReadOnlyList<string> ReadOptionalStringArray(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
            return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' must be an array.");

        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' items must be strings.");
            var text = item.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException($"Field '{DisplayPath(path, name)}' items must be non-empty canonical strings.");
            values.Add(text);
        }

        return values.ToArray();
    }

    private static void ValidateProperties(JsonElement value, string path, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!set.Contains(property.Name))
                throw new InvalidOperationException($"Unknown manifest field '{DisplayPath(path, property.Name)}'.");
        }
    }

    private static void ValidatePluginId(string value, string field)
    {
        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';

            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] == '-')
                    throw new InvalidOperationException($"Field '{field}' is not a canonical plugin id.");
                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter && !isDigit)
                throw new InvalidOperationException($"Field '{field}' is not a canonical plugin id.");
            if (!isAsciiLetter && !isDigit && character != '-')
                throw new InvalidOperationException($"Field '{field}' is not a canonical plugin id.");
            if (index == value.Length - 1 && character == '-')
                throw new InvalidOperationException($"Field '{field}' is not a canonical plugin id.");

            segmentStart = false;
        }
    }

    private static void ValidateEntryAssembly(string value, string field)
    {
        if (!value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(value) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
            HasControlCharacter(value))
        {
            throw new InvalidOperationException(
                $"Field '{field}' must be a relative file name ending in .dll.");
        }
    }

    private static void ValidateEntryType(string value)
    {
        if (value.IndexOf(',') >= 0 || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 ||
            HasControlCharacter(value))
            throw new InvalidOperationException("Field 'entry.type' must be a non-assembly-qualified CLR type name.");
    }

    private static bool HasControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
                return true;
        }

        return false;
    }

    private static void ValidateFeatureValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("Plugin feature values must be non-empty canonical strings.");

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] is '-' or '_')
                    throw new InvalidOperationException($"Plugin feature '{value}' is not canonical.");
                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter && !isDigit)
                throw new InvalidOperationException($"Plugin feature '{value}' is not canonical.");
            if (!isAsciiLetter && !isDigit && character is not '-' and not '_')
                throw new InvalidOperationException($"Plugin feature '{value}' is not canonical.");
            if (index == value.Length - 1 && (character is '-' or '_'))
                throw new InvalidOperationException($"Plugin feature '{value}' is not canonical.");
            segmentStart = false;
        }
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
                    throw new InvalidOperationException($"Field '{field}' is not a canonical command id.");
                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter)
                throw new InvalidOperationException($"Field '{field}' is not a canonical command id.");
            if (!isAsciiLetter && !isDigit)
                throw new InvalidOperationException($"Field '{field}' is not a canonical command id.");
            segmentStart = false;
        }
    }

    private static string ComputeNormalizedDigest(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        IReadOnlyList<string> features,
        IReadOnlyList<ParsedPluginDependency> pluginDependencies,
        IReadOnlyList<ParsedContractDependency> contractDependencies,
        IReadOnlyList<ParsedExtensionPoint> extensionPoints,
        IReadOnlyList<ParsedCommand> commands)
    {
        var builder = new StringBuilder();
        AppendDigestValue(builder, DigestDomain);
        AppendDigestValue(builder, packageId);
        AppendDigestValue(builder, packageVersion);
        AppendDigestValue(builder, entryAssembly);
        AppendDigestValue(builder, entryType);
        AppendDigestValue(builder, apiRange);
        AppendDigestValue(builder, features.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var feature in features)
            AppendDigestValue(builder, feature);

        if (pluginDependencies.Count > 0)
        {
            AppendDigestValue(builder, "dependencies.plugins");
            AppendDigestValue(builder, pluginDependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var dependency in pluginDependencies)
            {
                AppendDigestValue(builder, dependency.Id);
                AppendDigestValue(builder, dependency.VersionRange);
            }
        }

        if (contractDependencies.Count > 0)
        {
            AppendDigestValue(builder, "dependencies.contracts");
            AppendDigestValue(builder, contractDependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var dependency in contractDependencies)
            {
                AppendDigestValue(builder, dependency.Assembly);
                AppendDigestValue(builder, dependency.VersionRange);
                AppendDigestValue(builder, dependency.Sha256);
            }
        }

        if (extensionPoints.Count > 0)
        {
            AppendDigestValue(builder, "extensionPoints");
            AppendDigestValue(builder, extensionPoints.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var extensionPoint in extensionPoints)
            {
                AppendDigestValue(builder, extensionPoint.Id);
                AppendDigestValue(builder, extensionPoint.Kind);
                AppendDigestValue(builder, extensionPoint.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendDigestValue(builder, extensionPoint.Replaces ?? string.Empty);
                AppendDigestValue(builder, extensionPoint.DefaultFor ?? string.Empty);
                foreach (var conflict in extensionPoint.Conflicts)
                    AppendDigestValue(builder, conflict);
            }
        }

        if (commands.Count > 0)
        {
            AppendDigestValue(builder, "commands");
            AppendDigestValue(builder, commands.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var command in commands)
            {
                AppendDigestValue(builder, command.Id);
                AppendDigestValue(builder, command.Title ?? string.Empty);
                AppendDigestValue(builder, command.Description ?? string.Empty);
                AppendDigestValue(builder, command.Target);
            }
        }

        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendDigestValue(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string DisplayPath(string parent, string child) =>
        parent == "$" ? child : parent.Substring(2) + "." + child;

    private static ParsedPluginManifest Fail(string error) =>
        new(
            packageId: string.Empty,
            packageVersion: string.Empty,
            entryAssembly: string.Empty,
            entryType: string.Empty,
            apiRange: string.Empty,
            sourceFeatures: Array.Empty<string>(),
            features: Array.Empty<string>(),
            pluginDependencies: Array.Empty<ParsedPluginDependency>(),
            contractDependencies: Array.Empty<ParsedContractDependency>(),
            commands: Array.Empty<ParsedCommand>(),
            digest: string.Empty,
            apiRangeSupported: false,
            issues: Array.Empty<PluginManifestIssue>(),
            error: error);

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        for (var index = 0; index < bytes.Length; index++)
            builder.Append(bytes[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
