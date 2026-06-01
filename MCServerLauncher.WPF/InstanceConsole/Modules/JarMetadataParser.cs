using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.Modules;

public record JarMetadata(string DisplayName, string Version);

public static class JarMetadataParser
{
    public static JarMetadata? Parse(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            // 1. Fabric mod
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null) return ParseFabric(fabric);

            // 2. Quilt mod
            var quilt = zip.GetEntry("quilt.mod.json");
            if (quilt != null) return ParseQuilt(quilt);

            // 3. Forge / NeoForge mods.toml
            var modsToml = zip.GetEntry("META-INF/neoforge.mods.toml")
                           ?? zip.GetEntry("META-INF/mods.toml");
            if (modsToml != null) return ParseModsToml(modsToml);

            // 4. Bukkit / Spigot / Paper plugin
            var pluginYml = zip.GetEntry("plugin.yml") ?? zip.GetEntry("paper-plugin.yml");
            if (pluginYml != null) return ParsePluginYml(pluginYml);

            // 5. BungeeCord plugin
            var bungee = zip.GetEntry("bungee.yml");
            if (bungee != null) return ParsePluginYml(bungee);

            // 6. Velocity plugin (annotation-based, may have velocity-plugin.json since 3.x)
            var velocity = zip.GetEntry("velocity-plugin.json");
            if (velocity != null) return ParseFabric(velocity); // similar JSON structure

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[JarMetadataParser] Failed to parse {0}", jarPath);
            return null;
        }
    }

    private static JarMetadata? ParseFabric(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            string name = TryGetString(root, "name")
                          ?? TryGetString(root, "id")
                          ?? string.Empty;
            string version = TryGetString(root, "version") ?? string.Empty;
            return new JarMetadata(name, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[JarMetadataParser] Failed to parse fabric/json metadata");
            return null;
        }
    }

    private static JarMetadata? ParseQuilt(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // Quilt: quilt_loader.metadata.name, quilt_loader.version
            string name = string.Empty;
            string version = string.Empty;

            if (root.TryGetProperty("quilt_loader", out var loader))
            {
                version = TryGetString(loader, "version") ?? string.Empty;
                if (loader.TryGetProperty("metadata", out var meta))
                {
                    name = TryGetString(meta, "name") ?? string.Empty;
                }
                if (string.IsNullOrEmpty(name))
                {
                    name = TryGetString(loader, "id") ?? string.Empty;
                }
            }
            return new JarMetadata(name, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[JarMetadataParser] Failed to parse quilt metadata");
            return null;
        }
    }

    private static JarMetadata? ParseModsToml(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();

            // mods.toml uses [[mods]] table arrays. Find first one.
            // Look for displayName, version fields in the first [[mods]] block.
            var modsBlockMatch = Regex.Match(content,
                @"\[\[mods\]\](?<body>[\s\S]*?)(?=\[\[mods\]\]|\[\[|\z)",
                RegexOptions.Multiline);

            string blockBody = modsBlockMatch.Success ? modsBlockMatch.Groups["body"].Value : content;

            string name = ExtractTomlString(blockBody, "displayName")
                          ?? ExtractTomlString(blockBody, "modId")
                          ?? string.Empty;
            string version = ExtractTomlString(blockBody, "version") ?? string.Empty;

            // version may contain ${file.jarVersion} placeholder; leave as-is.
            return new JarMetadata(name, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[JarMetadataParser] Failed to parse mods.toml");
            return null;
        }
    }

    private static JarMetadata? ParsePluginYml(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();

            string name = ExtractYamlString(content, "name") ?? string.Empty;
            string version = ExtractYamlString(content, "version") ?? string.Empty;
            return new JarMetadata(name, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[JarMetadataParser] Failed to parse plugin.yml");
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? ExtractTomlString(string body, string key)
    {
        var match = Regex.Match(body,
            @"^\s*" + Regex.Escape(key) + @"\s*=\s*""(?<val>[^""]*)""",
            RegexOptions.Multiline);
        return match.Success ? match.Groups["val"].Value : null;
    }

    private static string? ExtractYamlString(string content, string key)
    {
        // Match top-level (no leading whitespace) yaml `key: value` pairs.
        var match = Regex.Match(content,
            @"^" + Regex.Escape(key) + @"\s*:\s*(?<val>.+?)\s*$",
            RegexOptions.Multiline);
        if (!match.Success) return null;
        var value = match.Groups["val"].Value.Trim();
        if ((value.StartsWith('"') && value.EndsWith('"'))
            || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
        }
        return value;
    }
}
