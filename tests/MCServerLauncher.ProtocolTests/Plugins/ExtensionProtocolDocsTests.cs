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
        Assert.Contains("mcsl.extension.daemon.bundle.install", topic, StringComparison.Ordinal);
        Assert.Contains("mcsl.extension.daemon.bundle.status", topic, StringComparison.Ordinal);
        Assert.Contains("mcsl.extension.daemon.bundle.enable", topic, StringComparison.Ordinal);
        Assert.Contains("mcsl.extension.daemon.bundle.disable", topic, StringComparison.Ordinal);
        Assert.Contains("restart_required", topic, StringComparison.Ordinal);
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

    [Fact]
    public void PluginManifestSchemaDocumentsGeneratorManifestGrammar()
    {
        var root = FindRepositoryRoot();
        var schemaPath = Path.Combine(root, "docs", "spec", "mcsl-plugin-2.0.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var properties = document.RootElement.GetProperty("properties");
        var definitions = document.RootElement.GetProperty("$defs");

        Assert.Equal("https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json", document.RootElement.GetProperty("$id").GetString());
        Assert.True(properties.TryGetProperty("package", out _));
        Assert.True(properties.TryGetProperty("entry", out _));
        Assert.True(properties.TryGetProperty("requires", out _));
        Assert.True(properties.TryGetProperty("dependencies", out _));
        Assert.True(properties.TryGetProperty("extensionPoints", out _));
        Assert.True(properties.TryGetProperty("commands", out _));

        var features = definitions
            .GetProperty("feature")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("rpc.register", features);
        Assert.Contains("event.subscribe", features);
        Assert.Contains("audit.query", features);

        var extensionPointIds = definitions
            .GetProperty("extensionPoint")
            .GetProperty("properties")
            .GetProperty("id")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("provider.java", extensionPointIds);
        Assert.Contains("pipeline.instance.create", extensionPointIds);
        Assert.Contains("override.process.launcher", extensionPointIds);
    }

    [Fact]
    public void PublicSchemaMirrorMatchesAuthoredSpecs()
    {
        var root = FindRepositoryRoot();
        var mappings = new[]
        {
            (Source: "mcsl-extension-package-1.0.schema.json", Public: "mcsl-extension-1.0.schema.json", Id: "https://mcsl-team.github.io/schemas/mcsl-extension-1.0.schema.json"),
            (Source: "mcsl-extension-protocol-1.0.schema.json", Public: "mcsl-extension-protocol-1.0.schema.json", Id: "https://mcsl-team.github.io/schemas/mcsl-extension-protocol-1.0.schema.json"),
            (Source: "mcsl-extension-signature-1.0.schema.json", Public: "mcsl-extension-signature-1.0.schema.json", Id: "https://mcsl-team.github.io/schemas/mcsl-extension-signature-1.0.schema.json"),
            (Source: "mcsl-plugin-2.0.schema.json", Public: "mcsl-plugin-2.0.schema.json", Id: "https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json"),
        };

        foreach (var mapping in mappings)
        {
            var sourcePath = Path.Combine(root, "docs", "spec", mapping.Source);
            var publicPath = Path.Combine(root, "schemas", mapping.Public);
            var source = File.ReadAllText(sourcePath);
            var published = File.ReadAllText(publicPath);
            Assert.Equal(source, published);

            using var document = JsonDocument.Parse(published);
            Assert.Equal(mapping.Id, document.RootElement.GetProperty("$id").GetString());
        }
    }

    [Fact]
    public void PluginManifestTemplateCarriesSchemaAndRequiredFields()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "docs", "templates", "daemon-plugin-basic", "mcsl-plugin.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var rootElement = document.RootElement;

        Assert.Equal("https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json", rootElement.GetProperty("$schema").GetString());
        Assert.Equal("community.example.basic", rootElement.GetProperty("package").GetProperty("id").GetString());
        Assert.Equal("Community.Example.BasicPlugin.dll", rootElement.GetProperty("entry").GetProperty("assembly").GetString());
        Assert.Contains("rpc.register", rootElement
            .GetProperty("requires")
            .GetProperty("features")
            .EnumerateArray()
            .Select(static item => item.GetString()));
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
