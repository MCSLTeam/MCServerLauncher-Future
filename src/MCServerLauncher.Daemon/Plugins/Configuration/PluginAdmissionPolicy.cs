using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

/// <summary>
/// A feature grant decision produced by admission.
/// </summary>
internal sealed record PluginFeatureGrant(
    string PluginId,
    ImmutableArray<PluginFeature> Granted,
    ImmutableArray<PluginFeature> Denied,
    bool Enabled,
    string Decision = "allow");

internal enum PluginGrantLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Custom = 4
}

/// <summary>
/// Computes the effective feature set per plugin from the frozen daemon config.
///
/// effective(pluginId) = features_allowed_by(grant_level) UNION plugin_grants[pluginId]
///                       (or feature_grants when Custom).
/// A plugin loads iff requires.features is a subset of effective AND its entry is enabled.
/// </summary>
internal static class PluginAdmissionPolicy
{
    internal static PluginGrantLevel ParseGrantLevel(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "none" => PluginGrantLevel.None,
            "low" => PluginGrantLevel.Low,
            "medium" => PluginGrantLevel.Medium,
            "high" => PluginGrantLevel.High,
            "custom" => PluginGrantLevel.Custom,
            null or "" => PluginGrantLevel.Medium,
            _ => PluginGrantLevel.Medium,
        };
    }

    internal static ImmutableArray<PluginFeature> FeaturesAllowedByLevel(PluginGrantLevel level, DaemonPluginsConfig config)
    {
        if (level == PluginGrantLevel.Custom)
        {
            var builder = ImmutableArray.CreateBuilder<PluginFeature>();
            foreach (var value in config.FeatureGrants)
            {
                if (FeatureCatalog.TryGet(value, out var descriptor))
                    builder.Add(descriptor.Feature);
            }

            return builder.ToImmutable();
        }

        var risk = level switch
        {
            PluginGrantLevel.None => PluginFeatureRisk.None,
            PluginGrantLevel.Low => PluginFeatureRisk.Low,
            PluginGrantLevel.Medium or PluginGrantLevel.Custom => PluginFeatureRisk.Medium,
            PluginGrantLevel.High => PluginFeatureRisk.High,
            _ => PluginFeatureRisk.Medium,
        };

        return FeatureCatalog.FeaturesAllowedByRisk(risk);
    }

    /// <summary>
    /// Decides whether a discovered plugin may load, based on its required features and the
    /// effective grant set. Returns granted/denied + enabled flag.
    /// </summary>
    internal static PluginFeatureGrant Decide(
        PluginManifest manifest,
        DaemonPluginsConfig config)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(config);

        var level = ParseGrantLevel(config.GrantLevel);
        var allowed = FeaturesAllowedByLevel(level, config);

        // Union in per-plugin permanent grants.
        var effective = new HashSet<PluginFeature>(allowed);
        if (config.PluginGrants.TryGetValue(manifest.Identity.Id, out var extra))
        {
            foreach (var value in extra)
            {
                if (FeatureCatalog.TryGet(value, out var descriptor))
                    effective.Add(descriptor.Feature);
            }
        }

        var granted = ImmutableArray.CreateBuilder<PluginFeature>();
        var denied = ImmutableArray.CreateBuilder<PluginFeature>();
        foreach (var required in manifest.Features)
        {
            if (effective.Contains(required))
                granted.Add(required);
            else
                denied.Add(required);
        }

        var enabled = !config.Entries.TryGetValue(manifest.Identity.Id, out var entry) || entry.Enabled;

        return new PluginFeatureGrant(manifest.Identity.Id, granted.ToImmutable(), denied.ToImmutable(), enabled);
    }

    /// <summary>
    /// SHA-256 digest of the canonical manifest JSON. Stored in admissions and compared on
    /// cold restart to force re-review when the manifest feature set or identity changes.
    /// </summary>
    internal static string ComputeDigest(string canonicalManifestJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalManifestJson);
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonicalManifestJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns true if the on-disk permanent admission still matches the current manifest
    /// digest and required feature set. A mismatch forces re-preflight (TTY) or skip (no TTY).
    /// </summary>
    internal static bool AdmissionMatches(PluginAdmissionConfig admission, string manifestDigest, FrozenSet<PluginFeature> features)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDigest);
        ArgumentNullException.ThrowIfNull(features);

        if (!admission.Decision.Equals("allow", StringComparison.Ordinal))
            return false;
        if (!string.Equals(admission.ManifestDigest, manifestDigest, StringComparison.Ordinal))
            return false;

        var admitted = new HashSet<PluginFeature>();
        foreach (var value in admission.Features)
        {
            if (FeatureCatalog.TryGet(value, out var descriptor))
                admitted.Add(descriptor.Feature);
        }

        if (admitted.Count != features.Count)
            return false;
        foreach (var feature in features)
        {
            if (!admitted.Contains(feature))
                return false;
        }

        return true;
    }
}
