using System.Text.Json;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class ExtensionProtocolDocsTests
{
    [Fact]
    public void ExtensionProtocolSchemaDocumentsImplementedEnvelopeSet()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "docs", "spec", "mcsl-extension-protocol-1.0.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var definitions = document.RootElement.GetProperty("$defs");

        foreach (var name in new[] { "statePatch", "stateSnapshot", "event", "request", "response", "error" })
            Assert.True(definitions.TryGetProperty(name, out _), $"Missing schema definition '{name}'.");

        var forbidden = definitions
            .GetProperty("jsonObject")
            .GetProperty("propertyNames")
            .GetProperty("not")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("__proto__", forbidden);
        Assert.Contains("prototype", forbidden);
        Assert.Contains("constructor", forbidden);
    }

    [Fact]
    public void ExtensionProtocolTopicDocumentsCacheAndPatchRules()
    {
        var root = FindRepositoryRoot();
        var topicPath = Path.Combine(
            root,
            "src",
            "MCServerLauncher.Daemon",
            ".Resources",
            "Docs",
            "protocol",
            "topics",
            "extension-protocol.md");
        var topic = File.ReadAllText(topicPath);

        Assert.Contains("mcsl.extension.v1", topic, StringComparison.Ordinal);
        Assert.Contains("state.patch", topic, StringComparison.Ordinal);
        Assert.Contains("state.snapshot", topic, StringComparison.Ordinal);
        Assert.Contains("Runtime packages load UI Schema and resources from local package cache", topic, StringComparison.Ordinal);
        Assert.Contains("mcsl.extension.dispatch", topic, StringComparison.Ordinal);
        Assert.Contains("plugin.<plugin>.event.extension", topic, StringComparison.Ordinal);
        Assert.Contains("__proto__", topic, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtensionPackageSchemaDocumentsManifestTargetsAndExtensionPoints()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "docs", "spec", "mcsl-extension-package-1.0.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var properties = document.RootElement.GetProperty("properties");
        var definitions = document.RootElement.GetProperty("$defs");

        Assert.Equal("https://mcsl-team.github.io/schemas/mcsl-extension-1.0.schema.json", document.RootElement.GetProperty("$id").GetString());
        Assert.True(properties.TryGetProperty("targets", out _));
        Assert.True(properties.TryGetProperty("permissions", out _));
        Assert.True(properties.TryGetProperty("integrity", out _));
        Assert.True(definitions.TryGetProperty("extensionPoint", out var extensionPoint));
        Assert.True(definitions.TryGetProperty("daemonBundleRef", out var daemonBundleRef));
        Assert.Contains(".zip", daemonBundleRef.ToString(), StringComparison.Ordinal);
        var extensionPointIds = extensionPoint
            .GetProperty("properties")
            .GetProperty("id")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("command.daemon", extensionPointIds);
        Assert.Contains("ui.panel", extensionPointIds);

        var eventItemSchemas = properties
            .GetProperty("permissions")
            .GetProperty("properties")
            .GetProperty("events")
            .GetProperty("items")
            .GetProperty("oneOf")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(eventItemSchemas, item => item.TryGetProperty("pattern", out var pattern) && pattern.GetString()!.Contains("event\\.extension", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
    }
}
