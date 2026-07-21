using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Plugin.Generators.Manifest;

internal sealed class ParsedPluginManifest
{
    public ParsedPluginManifest(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        IReadOnlyList<string> features,
        string digest,
        string? error)
    {
        PackageId = packageId;
        PackageVersion = packageVersion;
        EntryAssembly = entryAssembly;
        EntryType = entryType;
        ApiRange = apiRange;
        Features = features;
        Digest = digest;
        Error = error;
    }

    public string PackageId { get; }
    public string PackageVersion { get; }
    public string EntryAssembly { get; }
    public string EntryType { get; }
    public string ApiRange { get; }
    public IReadOnlyList<string> Features { get; }
    public string Digest { get; }
    public string? Error { get; }
    public bool IsValid => Error is null;
}

/// <summary>
/// Minimal schema-aware parser for mcsl-plugin.json that avoids System.Text.Json so the
/// analyzer package remains self-contained (no STJ dependency DLL to ship under analyzers/).
/// </summary>
internal static class PluginManifestParser
{
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

    private static readonly Regex StringProp = new(
        "\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FeaturesArray = new(
        "\"features\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex FeatureItem = new(
        "\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ParsedPluginManifest Parse(string json, string pathHint)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                return Fail("Manifest is empty.");

            // Extract nested object regions for package/entry/requires by simple brace matching
            // after locating the property key. Sufficient for the frozen 2.0 schema shape.
            var packageId = RequireNestedString(json, "package", "id");
            var packageVersion = RequireNestedString(json, "package", "version");
            var entryAssembly = RequireNestedString(json, "entry", "assembly");
            var entryType = RequireNestedString(json, "entry", "type");
            var apiRange = RequireNestedString(json, "requires", "api");

            var featuresMatch = FeaturesArray.Match(json);
            if (!featuresMatch.Success)
                return Fail("Field 'requires.features' is required and must be an array.");

            var features = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match item in FeatureItem.Matches(featuresMatch.Groups["body"].Value))
            {
                var value = Unescape(item.Groups["value"].Value);
                if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                    return Fail($"Feature '{value}' is invalid.");
                if (!KnownFeatures.Contains(value))
                    return Fail($"Feature '{value}' is not supported.");
                if (!seen.Add(value))
                    return Fail($"Feature '{value}' is declared more than once.");
                features.Add(value);
            }

            byte[] digestBytes;
            using (var sha = SHA256.Create())
                digestBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var digest = ToHex(digestBytes);

            return new ParsedPluginManifest(
                packageId,
                packageVersion,
                entryAssembly,
                entryType,
                apiRange,
                features,
                digest,
                error: null);
        }
        catch (Exception exception)
        {
            return Fail($"{pathHint}: {exception.Message}");
        }
    }

    public static bool IsFeatureKnown(string value) => KnownFeatures.Contains(value);

    private static string RequireNestedString(string json, string parent, string child)
    {
        var parentIndex = json.IndexOf("\"" + parent + "\"", StringComparison.Ordinal);
        if (parentIndex < 0)
            throw new InvalidOperationException($"Field '{parent}' is required.");

        var brace = json.IndexOf('{', parentIndex);
        if (brace < 0)
            throw new InvalidOperationException($"Field '{parent}' is required.");

        var depth = 0;
        var end = -1;
        for (var i = brace; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end < 0)
            throw new InvalidOperationException($"Field '{parent}' is malformed.");

        var region = json.Substring(brace, end - brace + 1);
        foreach (Match match in StringProp.Matches(region))
        {
            if (match.Groups["key"].Value == child)
            {
                var value = Unescape(match.Groups["value"].Value).Trim();
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"Field '{parent}.{child}' is required.");
                return value;
            }
        }

        throw new InvalidOperationException($"Field '{parent}.{child}' is required.");
    }

    private static string Unescape(string value) =>
        value.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static ParsedPluginManifest Fail(string error) =>
        new(
            packageId: string.Empty,
            packageVersion: string.Empty,
            entryAssembly: string.Empty,
            entryType: string.Empty,
            apiRange: string.Empty,
            features: Array.Empty<string>(),
            digest: string.Empty,
            error: error);

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        for (var i = 0; i < bytes.Length; i++)
            builder.Append(bytes[i].ToString("x2"));
        return builder.ToString();
    }
}
