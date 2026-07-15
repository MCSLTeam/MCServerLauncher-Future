using System.Reflection;
using MCServerLauncher.Daemon.Plugins;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginManifestAndDiscoveryTests
{
    [Fact]
    public void ReadsManifestWithExplicitSnakeCaseFieldsAndCapabilities()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.2.3",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            "event.publish",
            "instance.query");

        var manifest = PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0");

        Assert.Equal("community.instance-health", manifest.Identity.Id);
        Assert.Equal("1.2.3", manifest.Identity.Version);
        Assert.Equal("PluginEntry.dll", manifest.EntryAssembly);
        Assert.Equal("Community.InstanceHealth.InstanceHealthPlugin", manifest.EntryType);
        Assert.True(manifest.HasCapability(MCServerLauncher.Daemon.API.Protocol.PluginCapability.RpcRegister));
        Assert.True(manifest.HasCapability(MCServerLauncher.Daemon.API.Protocol.PluginCapability.EventPublish));
        Assert.True(manifest.HasCapability(MCServerLauncher.Daemon.API.Protocol.PluginCapability.InstanceQuery));
    }

    [Fact]
    public void RejectsUnsupportedApiRangeAndUnknownCapability()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[2.0.0,3.0.0)",
            "rpc.register");

        var rangeException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("api_version_unsupported", rangeException.Code);

        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "unknown.capability");

        var capabilityException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("capability_unsupported", capabilityException.Code);
    }

    [Fact]
    public void RejectsDuplicateCapabilitiesAndDuplicatePluginIds()
    {
        using var duplicateFixture = PluginFixture.Create("community.instance-health");
        duplicateFixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            ["rpc.register", "rpc.register"]);

        var duplicateException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(duplicateFixture.BundleDirectory, "1.0.0"));
        Assert.Equal("capability_duplicate", duplicateException.Code);

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
                "[1.0.0,2.0.0)",
                "rpc.register");
            second.WriteManifest(
                "community.instance-health",
                "1.0.1",
                "PluginEntry.dll",
                "Community.InstanceHealth.InstanceHealthPlugin",
                "[1.0.0,2.0.0)",
                "rpc.register");

            var result = new PluginDiscovery("1.0.0").Discover(root);

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
            "[1.0.0,2.0.0)",
            "rpc.register",
            extraJson: ",\"unexpected\":true");

        var manifestException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("manifest_invalid", manifestException.Code);

        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "rpc.register");
        var assemblyException = Assert.Throws<PluginAssemblyException>(
            () => PluginAssemblyPolicy.ValidateBundle(
                PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0")));
        Assert.Equal("forbidden_reference", assemblyException.Code);
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
            params string[] capabilities)
        {
            WriteManifest(id, version, entryAssembly, entryType, apiVersion, capabilities, string.Empty);
        }

        public void WriteManifest(
            string id,
            string version,
            string entryAssembly,
            string entryType,
            string apiVersion,
            string capability,
            string extraJson)
        {
            WriteManifest(id, version, entryAssembly, entryType, apiVersion, [capability], extraJson);
        }

        private void WriteManifest(
            string id,
            string version,
            string entryAssembly,
            string entryType,
            string apiVersion,
            string[] capabilities,
            string extraJson)
        {
            var capabilityJson = string.Join(",", capabilities.Select(static value => $"\"{value}\""));
            var json = $$"""
                {
                  "id": "{{id}}",
                  "version": "{{version}}",
                  "entry_assembly": "{{entryAssembly}}",
                  "entry_type": "{{entryType}}",
                  "api_version": "{{apiVersion}}",
                  "capabilities": [{{capabilityJson}}]{{extraJson}}
                }
                """;
            File.WriteAllText(Path.Combine(BundleDirectory, "plugin.json"), json);
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
