using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Features.Plugins;

/// <summary>Registry REST DTO: plugin list item.</summary>
public sealed record PluginRegistryPluginSummary(
    string Id,
    string DisplayName,
    string Summary,
    string? LatestVersion,
    int Downloads,
    bool TargetsClient,
    bool TargetsDaemon,
    IReadOnlyList<string> Categories);

/// <summary>Registry REST DTO: a published version.</summary>
public sealed record PluginRegistryVersionSummary(
    string Version,
    string? Changelog,
    long SizeBytes,
    int DownloadCount,
    bool Signed,
    string DownloadUrl);

/// <summary>Registry REST DTO: plugin detail.</summary>
public sealed record PluginRegistryPluginDetail(
    string Id,
    string DisplayName,
    string Summary,
    string Description,
    string? LatestVersion,
    int Downloads,
    IReadOnlyList<string> Categories,
    bool TargetsClient,
    bool TargetsDaemon,
    IReadOnlyList<PluginRegistryVersionSummary> Versions);

/// <summary>
/// HTTP client for the MCSL Plugin Registry public API
/// (see MCSL-Plugin-Registry/docs/API.md). Remote packages are always
/// re-validated locally before anything is written to the client cache.
/// </summary>
public sealed class PluginRegistryClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public PluginRegistryClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
    }

    public string BaseUrl { get; }

    public async Task<IReadOnlyList<PluginRegistryPluginSummary>> ListPluginsAsync(
        string? query = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var uri = $"{BaseUrl}/api/v1/plugins?pageSize=50";
        if (!string.IsNullOrWhiteSpace(query))
            uri += $"&q={Uri.EscapeDataString(query.Trim())}";
        if (!string.IsNullOrWhiteSpace(category))
            uri += $"&category={Uri.EscapeDataString(category)}";

        using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("plugins", out var plugins))
            return [];

        var result = new List<PluginRegistryPluginSummary>();
        foreach (var plugin in plugins.EnumerateArray())
        {
            result.Add(new PluginRegistryPluginSummary(
                GetString(plugin, "id"),
                GetString(plugin, "displayName"),
                GetString(plugin, "summary"),
                GetNullableString(plugin, "latestVersion"),
                GetInt(plugin, "downloads"),
                GetTarget(plugin, "client"),
                GetTarget(plugin, "daemon"),
                GetStringArray(plugin, "categories")));
        }
        return result;
    }

    public async Task<PluginRegistryPluginDetail?> GetPluginAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http
            .GetAsync($"{BaseUrl}/api/v1/plugins/{Uri.EscapeDataString(id)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var plugin = document.RootElement;

        var versions = new List<PluginRegistryVersionSummary>();
        if (plugin.TryGetProperty("versions", out var versionsElement))
        {
            foreach (var version in versionsElement.EnumerateArray())
            {
                var downloadUrl = GetString(version, "downloadUrl");
                versions.Add(new PluginRegistryVersionSummary(
                    GetString(version, "version"),
                    GetNullableString(version, "changelog"),
                    GetLong(version, "sizeBytes"),
                    GetInt(version, "downloadCount"),
                    GetBool(version, "signed"),
                    downloadUrl));
            }
        }

        return new PluginRegistryPluginDetail(
            GetString(plugin, "id"),
            GetString(plugin, "displayName"),
            GetString(plugin, "summary"),
            GetString(plugin, "description"),
            GetNullableString(plugin, "latestVersion"),
            GetInt(plugin, "downloads"),
            GetStringArray(plugin, "categories"),
            GetTarget(plugin, "client"),
            GetTarget(plugin, "daemon"),
            versions);
    }

    public async Task<byte[]> DownloadVersionAsync(
        string id,
        string version,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http
            .GetAsync(
                $"{BaseUrl}/api/v1/plugins/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(version)}/download",
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        string message = $"Registry request failed with HTTP {(int)response.StatusCode}.";
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("message", out var value))
                message = value.GetString() ?? message;
        }
        catch
        {
            // non-JSON error body
        }
        throw new PluginRegistryException((int)response.StatusCode, message);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? GetNullableString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool GetTarget(JsonElement element, string name)
    {
        if (!element.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            return false;
        return GetBool(targets, name);
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return [];
        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }
}

public sealed class PluginRegistryException : Exception
{
    public PluginRegistryException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
