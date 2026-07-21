using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Plugins.Configuration;

/// <summary>
/// Daemon security surface for token issuance and audience binding.
/// </summary>
public sealed class DaemonSecurityConfig
{
    [JsonPropertyName("allow_main_token_issue")]
    public bool AllowMainTokenIssue { get; set; } = true;

    [JsonPropertyName("max_token_ttl_seconds")]
    public int MaxTokenTtlSeconds { get; set; } = 2_592_000;

    [JsonPropertyName("api_canonical_uri")]
    public string ApiCanonicalUri { get; set; } = "mcsl://daemon/api/v2";
}

/// <summary>
/// Per-plugin entry overrides stored under config.json plugins.entries.
/// </summary>
public sealed class PluginEntryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("storage_quota_bytes")]
    public long? StorageQuotaBytes { get; set; }
}

/// <summary>
/// A permanent per-plugin feature grant written by Approve Permanent.
/// </summary>
public sealed class PluginAdmissionConfig
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "allow";

    [JsonPropertyName("manifest_digest")]
    public string ManifestDigest { get; set; } = string.Empty;

    [JsonPropertyName("features")]
    public string[] Features { get; set; } = [];

    [JsonPropertyName("decided_at")]
    public string DecidedAt { get; set; } = string.Empty;
}

/// <summary>
/// Storage quota defaults for plugin-private storage.
/// </summary>
public sealed class PluginStorageConfig
{
    [JsonPropertyName("default_quota_bytes")]
    public long DefaultQuotaBytes { get; set; } = 268_435_456;

    [JsonPropertyName("default_max_files")]
    public int DefaultMaxFiles { get; set; } = 4096;
}

/// <summary>
/// Frozen decision: grant_level ceiling + optional Custom feature_grants + per-plugin grants/admissions/entries.
/// Loaded once at cold start; never mutated at runtime.
/// </summary>
public sealed class DaemonPluginsConfig
{
    [JsonPropertyName("start_timeout_seconds")]
    public int StartTimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("grant_level")]
    public string GrantLevel { get; set; } = "Medium";

    [JsonPropertyName("feature_grants")]
    public string[] FeatureGrants { get; set; } = [];

    [JsonPropertyName("storage")]
    public PluginStorageConfig Storage { get; set; } = new();

    [JsonPropertyName("plugin_grants")]
    public Dictionary<string, string[]> PluginGrants { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("admissions")]
    public Dictionary<string, PluginAdmissionConfig> Admissions { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("entries")]
    public Dictionary<string, PluginEntryConfig> Entries { get; set; } = new(StringComparer.Ordinal);

    internal static DaemonPluginsConfig Default => new();
}
