using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Immutable normalized metadata emitted for a generated daemon plugin adapter.
/// </summary>
public sealed class PluginAdapterMetadata
{
    public PluginAdapterMetadata(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        ImmutableArray<string> features,
        string manifestDigest)
    {
        PackageId = ProtocolIdentifier.ValidatePluginId(packageId, nameof(packageId));
        PackageVersion = RequireCanonicalValue(packageVersion, nameof(packageVersion));
        EntryAssembly = RequireCanonicalValue(entryAssembly, nameof(entryAssembly));
        EntryType = RequireCanonicalValue(entryType, nameof(entryType));
        ApiRange = RequireCanonicalValue(apiRange, nameof(apiRange));
        Features = ValidateFeatures(features);
        ManifestDigest = ValidateDigest(manifestDigest);
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public string EntryAssembly { get; }

    public string EntryType { get; }

    public string ApiRange { get; }

    public ImmutableArray<string> Features { get; }

    public string ManifestDigest { get; }

    private static ImmutableArray<string> ValidateFeatures(ImmutableArray<string> features)
    {
        if (features.IsDefault)
            throw new ArgumentException("Feature metadata must not be default.", nameof(features));

        string? previous = null;
        foreach (var feature in features)
        {
            _ = new PluginFeature(feature);
            if (!FeatureCatalog.IsKnown(feature))
                throw new ArgumentException($"Plugin feature '{feature}' is unknown.", nameof(features));
            if (previous is not null && StringComparer.Ordinal.Compare(previous, feature) >= 0)
            {
                throw new ArgumentException(
                    "Feature metadata must be unique and sorted with ordinal comparison.",
                    nameof(features));
            }

            previous = feature;
        }

        return features;
    }

    private static string RequireCanonicalValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Metadata values must not contain surrounding whitespace.", parameterName);
        return value;
    }

    private static string ValidateDigest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64)
            throw new ArgumentException("Manifest digest must be a lowercase SHA-256 value.", nameof(value));
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("Manifest digest must be a lowercase SHA-256 value.", nameof(value));
        }

        return value;
    }
}
