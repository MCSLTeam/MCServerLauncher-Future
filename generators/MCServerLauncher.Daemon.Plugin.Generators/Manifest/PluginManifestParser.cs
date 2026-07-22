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

    private const string HostApiVersion = "2.0.0";
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
            ValidateProperties(root, "$", "$schema", "package", "entry", "requires");

            var schema = ReadOptionalString(root, "$schema", "$");
            if (schema is not null && !string.Equals(schema, CanonicalSchemaUri, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Field '$schema' must be '{CanonicalSchemaUri}' when present.");
            }

            var package = RequireObjectProperty(root, "package", "$");
            ValidateProperties(package, "$.package", "id", "version");
            var packageId = RequireString(package, "id", "$.package");
            ValidatePluginId(packageId);
            var packageVersionText = RequireString(package, "version", "$.package");
            if (!NuGetVersion.TryParse(packageVersionText, out var packageVersion))
                throw new InvalidOperationException("Field 'package.version' is not a valid NuGet version.");
            var normalizedPackageVersion = packageVersion.ToNormalizedString();

            var entry = RequireObjectProperty(root, "entry", "$");
            ValidateProperties(entry, "$.entry", "assembly", "type");
            var entryAssembly = RequireString(entry, "assembly", "$.entry");
            ValidateEntryAssembly(entryAssembly);
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

            var digest = ComputeNormalizedDigest(
                packageId,
                normalizedPackageVersion,
                entryAssembly,
                entryType,
                normalizedApiRange,
                normalizedFeatures);

            return new ParsedPluginManifest(
                packageId,
                normalizedPackageVersion,
                entryAssembly,
                entryType,
                normalizedApiRange,
                sourceFeatures,
                normalizedFeatures,
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

    private static void ValidateProperties(JsonElement value, string path, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!set.Contains(property.Name))
                throw new InvalidOperationException($"Unknown manifest field '{DisplayPath(path, property.Name)}'.");
        }
    }

    private static void ValidatePluginId(string value)
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
                    throw new InvalidOperationException("Field 'package.id' is not a canonical plugin id.");
                segmentStart = true;
                continue;
            }

            if (segmentStart && !isAsciiLetter && !isDigit)
                throw new InvalidOperationException("Field 'package.id' is not a canonical plugin id.");
            if (!isAsciiLetter && !isDigit && character != '-')
                throw new InvalidOperationException("Field 'package.id' is not a canonical plugin id.");
            if (index == value.Length - 1 && character == '-')
                throw new InvalidOperationException("Field 'package.id' is not a canonical plugin id.");

            segmentStart = false;
        }
    }

    private static void ValidateEntryAssembly(string value)
    {
        if (!value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(value) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
            HasControlCharacter(value))
        {
            throw new InvalidOperationException(
                "Field 'entry.assembly' must be a relative file name ending in .dll.");
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

    private static string ComputeNormalizedDigest(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        IReadOnlyList<string> features)
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
