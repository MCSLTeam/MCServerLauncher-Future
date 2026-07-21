using System.Reflection;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Plugins;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginManifestAndDiscoveryTests
{
    [Fact]
    public void ReadsManifestWithExplicitSnakeCaseFieldsAndFeatures()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.2.3",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "event.publish",
            "instance.query",
            "rpc.register");

        var manifest = PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0");

        Assert.Equal("community.instance-health", manifest.Identity.Id);
        Assert.Equal("1.2.3", manifest.Identity.Version);
        Assert.Equal("PluginEntry.dll", manifest.EntryAssembly);
        Assert.Equal("Community.InstanceHealth.InstanceHealthPlugin", manifest.EntryType);
        Assert.True(manifest.HasFeature(PluginFeature.RpcRegister));
        Assert.True(manifest.HasFeature(PluginFeature.EventPublish));
        Assert.True(manifest.HasFeature(PluginFeature.InstanceQuery));
    }

    [Fact]
    public void RejectsUnsupportedApiRangeAndUnknownFeature()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[3.0.0,4.0.0)",
            "rpc.register");

        var rangeException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0"));
        Assert.Equal("api_version_unsupported", rangeException.Code);

        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "unknown.feature");

        var featureException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0"));
        Assert.Equal("feature_unsupported", featureException.Code);
    }

    [Fact]
    public void RejectsUnimplementedFeature()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "network.http.listen");

        var exception = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0"));
        Assert.Equal("feature_unimplemented", exception.Code);
    }

    [Fact]
    public void RejectsPaddedAndMalformedFeatureIdentifiers()
    {
        // Surrounding whitespace must not be silently trimmed into admission; a padded valid
        // name is a distinct feature_invalid failure, not feature_unsupported.
        using var padded = PluginFixture.Create("community.instance-health");
        padded.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            " rpc.register");
        var paddedException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(padded.BundleDirectory, "2.0.0"));
        Assert.Equal("feature_invalid", paddedException.Code);

        // Malformed identifiers must be reported as feature_invalid, not feature_unsupported.
        using var malformed = PluginFixture.Create("community.instance-health");
        malformed.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "rpc/register");
        var malformedException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(malformed.BundleDirectory, "2.0.0"));
        Assert.Equal("feature_invalid", malformedException.Code);
    }

    [Fact]
    public void RejectsDuplicateFeaturesAndDuplicatePluginIds()
    {
        using var duplicateFixture = PluginFixture.Create("community.instance-health");
        duplicateFixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            ["rpc.register", "rpc.register"]);

        var duplicateException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(duplicateFixture.BundleDirectory, "2.0.0"));
        Assert.Equal("feature_duplicate", duplicateException.Code);

        var root = Directory.CreateTempSubdirectory("mcsl-plugin-duplicates-").FullName;
        try
        {
            using var first = PluginFixture.Create("first", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var second = PluginFixture.Create("second", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            first.WriteManifest(
                "community.instance-health",
                "1.0.0",
                "PluginEntry.dll",
                "Community.InstanceHealth.InstanceHealthPlugin",
                "[2.0.0,3.0.0)",
                "rpc.register");
            second.WriteManifest(
                "community.instance-health",
                "1.0.1",
                "PluginEntry.dll",
                "Community.InstanceHealth.InstanceHealthPlugin",
                "[2.0.0,3.0.0)",
                "rpc.register");

            var result = new PluginDiscovery("2.0.0").Discover(root);

            Assert.Empty(result.Plugins);
            Assert.Equal(2, result.Failures.Count(failure => failure.Code == "duplicate_id"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsUnknownManifestMembersAndForbiddenAssemblyReferences()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "rpc.register",
            extraJson: ",\"unexpected\":true");

        var manifestException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0"));
        Assert.Equal("manifest_invalid", manifestException.Code);

        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "rpc.register");
        var assemblyException = Assert.Throws<PluginAssemblyException>(
            () => PluginAssemblyPolicy.ValidateBundle(
                PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "2.0.0")));
        Assert.Equal("forbidden_reference", assemblyException.Code);

        using var nestedFixture = PluginFixture.Create(
            "nested",
            Directory.CreateTempSubdirectory("mcsl-plugin-nested-").FullName,
            typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
        nestedFixture.WriteManifest(
            "nested",
            "1.0.0",
            "PluginEntry.dll",
            "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
            "[2.0.0,3.0.0)",
            "rpc.register");
        var nestedDirectory = Path.Combine(nestedFixture.BundleDirectory, "deps");
        Directory.CreateDirectory(nestedDirectory);
        File.Copy(
            typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location,
            Path.Combine(nestedDirectory, "TouchSocket.Fake.dll"));
        File.Copy(
            typeof(PluginHost).Assembly.Location,
            Path.Combine(nestedDirectory, "renamed.dll"));

        var nestedException = Assert.Throws<PluginAssemblyException>(
            () => PluginAssemblyPolicy.ValidateBundle(
                PluginManifestReader.ReadAndValidate(nestedFixture.BundleDirectory, "2.0.0")));
        Assert.Equal("forbidden_reference", nestedException.Code);
    }

    private sealed class PluginFixture : IDisposable
    {
        private readonly string _root;

        private PluginFixture(string root, string bundleDirectory)
        {
            _root = root;
            BundleDirectory = bundleDirectory;
        }

        public string BundleDirectory { get; }

        public static PluginFixture Create(string bundleId) =>
            Create(bundleId, Directory.CreateTempSubdirectory("mcsl-plugin-test-").FullName, Assembly.GetExecutingAssembly().Location);

        public static PluginFixture Create(string bundleId, string root, string sourceAssemblyPath)
        {
            var bundleDirectory = Path.Combine(root, bundleId);
            Directory.CreateDirectory(bundleDirectory);
            File.Copy(sourceAssemblyPath, Path.Combine(bundleDirectory, "PluginEntry.dll"));
            return new PluginFixture(root, bundleDirectory);
        }

        public void WriteManifest(
            string id,
            string version,
            string entryAssembly,
            string entryType,
            string apiVersion,
            params string[] features)
        {
            WriteManifest(id, version, entryAssembly, entryType, apiVersion, features, string.Empty);
        }

        public void WriteManifest(
            string id,
            string version,
            string entryAssembly,
            string entryType,
            string apiVersion,
            string feature,
            string extraJson)
        {
            WriteManifest(id, version, entryAssembly, entryType, apiVersion, [feature], extraJson);
        }

        private void WriteManifest(
            string id,
            string version,
            string entryAssembly,
            string entryType,
            string apiVersion,
            string[] features,
            string extraJson)
        {
            var featureJson = string.Join(",", features.Select(static value => $"\"{value}\""));
            var json = $$"""
                {
                  "package": {
                    "id": "{{id}}",
                    "version": "{{version}}"
                  },
                  "entry": {
                    "assembly": "{{entryAssembly}}",
                    "type": "{{entryType}}"
                  },
                  "requires": {
                    "api": "{{apiVersion}}",
                    "features": [{{featureJson}}]
                  }{{extraJson}}
                }
                """;
            File.WriteAllText(Path.Combine(BundleDirectory, "mcsl-plugin.json"), json);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
