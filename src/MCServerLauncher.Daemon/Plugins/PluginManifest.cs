using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginManifestDocument
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("entry_assembly")]
    public string? EntryAssembly { get; set; }

    [JsonPropertyName("entry_type")]
    public string? EntryType { get; set; }

    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("capabilities")]
    public string[]? Capabilities { get; set; }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PluginManifestDocument))]
internal partial class PluginHostJsonContext : JsonSerializerContext;

internal sealed record PluginManifest(
    PluginIdentity Identity,
    string EntryAssembly,
    string EntryType,
    NuGetVersion Version,
    VersionRange ApiVersionRange,
    FrozenSet<PluginCapability> Capabilities,
    string BundleDirectory,
    string EntryAssemblyPath)
{
    public bool HasCapability(PluginCapability capability) => Capabilities.Contains(capability);
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
    internal static PluginManifest ReadAndValidate(
        string bundleDirectory,
        string hostApiVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostApiVersion);

        var fullBundleDirectory = Path.GetFullPath(bundleDirectory);
        if (!Directory.Exists(fullBundleDirectory))
            throw new PluginManifestException("bundle_missing", "The plugin bundle directory does not exist.");

        var manifestPath = Path.Combine(fullBundleDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
            throw new PluginManifestException("manifest_missing", "The plugin bundle does not contain plugin.json.");

        PluginManifestDocument? document;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            document = JsonSerializer.Deserialize(stream, PluginHostJsonContext.Default.PluginManifestDocument);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new PluginManifestException("manifest_invalid", "The plugin manifest is not valid JSON.", exception);
        }

        if (document is null)
            throw new PluginManifestException("manifest_invalid", "The plugin manifest is empty.");

        var id = Require(document.Id, "id");
        var versionText = Require(document.Version, "version");
        var entryAssembly = Require(document.EntryAssembly, "entry_assembly");
        var entryType = Require(document.EntryType, "entry_type");
        var apiVersionText = Require(document.ApiVersion, "api_version");

        PluginIdentity identity;
        NuGetVersion version;
        VersionRange apiVersionRange;
        try
        {
            identity = new PluginIdentity(id, versionText);
            version = NuGetVersion.Parse(versionText);
            apiVersionRange = VersionRange.Parse(apiVersionText);
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

        ValidateEntryName(entryAssembly, "entry_assembly");
        ValidateEntryType(entryType);
        var entryAssemblyPath = Path.Combine(fullBundleDirectory, entryAssembly);
        if (!File.Exists(entryAssemblyPath))
            throw new PluginManifestException("entry_missing", $"The plugin entry assembly '{entryAssembly}' does not exist.");

        var capabilities = ParseCapabilities(document.Capabilities);
        return new PluginManifest(
            identity,
            entryAssembly,
            entryType,
            version,
            apiVersionRange,
            capabilities,
            fullBundleDirectory,
            entryAssemblyPath);
    }

    private static string Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PluginManifestException("manifest_field_missing", $"The plugin manifest field '{field}' is required.");

        return value.Trim();
    }

    private static void ValidateEntryName(string entryAssembly, string field)
    {
        if (!entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(entryAssembly), entryAssembly) ||
            Path.IsPathRooted(entryAssembly))
        {
            throw new PluginManifestException("entry_invalid", $"The manifest field '{field}' must be a file name ending in .dll.");
        }
    }

    private static void ValidateEntryType(string entryType)
    {
        if (entryType.Contains(',', StringComparison.Ordinal) ||
            entryType.Contains('/', StringComparison.Ordinal) ||
            entryType.Contains('\\', StringComparison.Ordinal))
        {
            throw new PluginManifestException("entry_type_invalid", "The plugin entry type must be an unqualified CLR type name.");
        }
    }

    private static FrozenSet<PluginCapability> ParseCapabilities(string[]? values)
    {
        if (values is null)
            throw new PluginManifestException("capabilities_missing", "The plugin manifest capabilities field is required.");

        var capabilities = new HashSet<PluginCapability>();
        foreach (var value in values)
        {
            PluginCapability capability;
            try
            {
                capability = value switch
                {
                    "rpc.register" => PluginCapability.RpcRegister,
                    "event.publish" => PluginCapability.EventPublish,
                    "instance.query" => PluginCapability.InstanceQuery,
                    _ => throw new PluginManifestException("capability_unsupported", $"Plugin capability '{value}' is not supported.")
                };
            }
            catch (PluginManifestException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                throw new PluginManifestException("capability_invalid", $"Plugin capability '{value}' is invalid.", exception);
            }

            if (!capabilities.Add(capability))
                throw new PluginManifestException("capability_duplicate", $"Plugin capability '{value}' is declared more than once.");
        }

        return capabilities.ToFrozenSet();
    }
}

internal sealed class PluginManifestException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
