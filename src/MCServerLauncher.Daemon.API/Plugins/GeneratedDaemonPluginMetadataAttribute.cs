namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Assembly metadata emitted by the plugin SDK generator and verified by the daemon before
/// loading the plugin assembly. All values are strings so the daemon can decode the custom
/// attribute directly from PE metadata without executing plugin code. An assembly may describe
/// multiple entry types, but each entry type must have exactly one metadata attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class GeneratedDaemonPluginMetadataAttribute : Attribute
{
    public GeneratedDaemonPluginMetadataAttribute(
        string packageId,
        string packageVersion,
        string entryAssembly,
        string entryType,
        string apiRange,
        string features,
        string manifestDigest)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(packageVersion);
        ArgumentNullException.ThrowIfNull(entryAssembly);
        ArgumentNullException.ThrowIfNull(entryType);
        ArgumentNullException.ThrowIfNull(apiRange);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(manifestDigest);

        PackageId = packageId;
        PackageVersion = packageVersion;
        EntryAssembly = entryAssembly;
        EntryType = entryType;
        ApiRange = apiRange;
        Features = features;
        ManifestDigest = manifestDigest;
    }

    public string PackageId { get; }

    public string PackageVersion { get; }

    public string EntryAssembly { get; }

    public string EntryType { get; }

    public string ApiRange { get; }

    /// <summary>
    /// Exact ordinal feature order encoded as line-feed-separated canonical identifiers.
    /// An empty string represents an empty feature set.
    /// </summary>
    public string Features { get; }

    public string ManifestDigest { get; }
}
