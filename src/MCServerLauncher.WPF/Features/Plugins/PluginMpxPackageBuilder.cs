using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginMpxPackageResourceInput(string Path, byte[] Bytes, string Mime);

public sealed record PluginMpxPackageBuildInput(
    string PackageId,
    string Version,
    string? DisplayName = null,
    string? UiAuthoringJson5 = null,
    string? ThemeJson = null,
    byte[]? DaemonPlugin = null,
    IReadOnlyList<PluginMpxPackageResourceInput>? Resources = null,
    IReadOnlyList<PluginMpxExtensionPoint>? ExtensionPoints = null,
    IReadOnlyList<PluginMpxCommand>? Commands = null,
    IReadOnlyList<string>? HostCapabilities = null);

public sealed record PluginMpxPackageBuildResult(
    bool Ok,
    byte[]? PackageBytes,
    IReadOnlyList<PluginUiDiagnostic> Diagnostics)
{
    public static PluginMpxPackageBuildResult Success(byte[] packageBytes) =>
        new(true, packageBytes, []);

    public static PluginMpxPackageBuildResult Failure(IReadOnlyList<PluginUiDiagnostic> diagnostics) =>
        new(false, null, diagnostics);
}

public static class PluginMpxPackageBuilder
{
    public static PluginMpxPackageBuildResult Build(PluginMpxPackageBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        var targets = new JsonObject();
        var runtime = new JsonObject { ["daemonApi"] = "[1.0.0,2.0.0)" };

        if (input.UiAuthoringJson5 is not null)
        {
            var compiled = PluginUiSchemaCompiler.CompileToRuntimeJson(input.UiAuthoringJson5);
            if (!compiled.Ok || compiled.RuntimeJson is null)
                return PluginMpxPackageBuildResult.Failure(compiled.Diagnostics);

            var uiBytes = Encoding.UTF8.GetBytes(compiled.RuntimeJson);
            entries["client/ui.json"] = uiBytes;
            EnsureClientTarget(targets)["ui"] = FileRef("client/ui.json", uiBytes);
            runtime["ui"] = "[1.0.0,2.0.0)";
        }

        if (input.ThemeJson is not null)
        {
            var compiled = PluginThemeRuntimeParser.Parse(input.ThemeJson);
            if (!compiled.Ok || compiled.RuntimeJson is null)
                return PluginMpxPackageBuildResult.Failure(compiled.Diagnostics);

            var themeBytes = Encoding.UTF8.GetBytes(compiled.RuntimeJson);
            entries["client/theme.json"] = themeBytes;
            EnsureClientTarget(targets)["theme"] = FileRef("client/theme.json", themeBytes);
        }

        if (input.DaemonPlugin is not null)
        {
            entries["daemon/plugin-bundle.zip"] = input.DaemonPlugin;
            targets["daemon"] = new JsonObject
            {
                ["plugin"] = FileRef("daemon/plugin-bundle.zip", input.DaemonPlugin)
            };
        }

        var resources = new JsonArray();
        if (input.Resources is { Count: > 0 })
        {
            _ = EnsureClientTarget(targets);
            foreach (var resource in input.Resources)
            {
                entries[resource.Path] = resource.Bytes;
                var resourceRef = FileRef(resource.Path, resource.Bytes);
                resourceRef["mime"] = resource.Mime;
                resourceRef["bytes"] = resource.Bytes.Length;
                resources.Add(resourceRef);
            }
        }

        var manifest = new JsonObject
        {
            ["schema"] = "https://mcsl-team.github.io/schemas/mcsl-extension-1.0.schema.json",
            ["package"] = new JsonObject
            {
                ["id"] = input.PackageId,
                ["version"] = input.Version,
                ["displayName"] = input.DisplayName ?? input.PackageId
            },
            ["runtime"] = runtime,
            ["targets"] = targets,
            ["permissions"] = new JsonObject
            {
                ["host"] = ToJsonArray(input.HostCapabilities ?? []),
                ["events"] = new JsonArray(),
                ["network"] = new JsonArray()
            },
            ["resources"] = resources,
            ["integrity"] = new JsonObject
            {
                ["algorithm"] = "sha256",
                ["signed"] = false
            }
        };

        if (input.ExtensionPoints is { Count: > 0 })
        {
            var extensionPoints = new JsonArray();
            foreach (var extensionPoint in input.ExtensionPoints)
            {
                extensionPoints.Add(new JsonObject
                {
                    ["kind"] = extensionPoint.Kind,
                    ["id"] = extensionPoint.Id,
                    ["target"] = extensionPoint.Target
                });
            }

            manifest["extensionPoints"] = extensionPoints;
        }

        if (input.Commands is { Count: > 0 })
        {
            var commands = new JsonArray();
            foreach (var command in input.Commands)
            {
                commands.Add(new JsonObject
                {
                    ["id"] = command.Id,
                    ["title"] = command.Title,
                    ["description"] = command.Description,
                    ["target"] = command.Target
                });
            }

            manifest["commands"] = commands;
        }

        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return PluginMpxPackageBuildResult.Success(BuildZip(entries));
    }

    private static JsonObject EnsureClientTarget(JsonObject targets)
    {
        if (targets["client"] is JsonObject client)
            return client;

        client = new JsonObject();
        targets["client"] = client;
        return client;
    }

    private static JsonObject FileRef(string path, byte[] bytes) => new()
    {
        ["path"] = path,
        ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    };

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static byte[] BuildZip(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var archiveEntry = archive.CreateEntry(entry.Key, CompressionLevel.NoCompression);
                using var entryStream = archiveEntry.Open();
                entryStream.Write(entry.Value);
            }
        }

        return stream.ToArray();
    }
}
