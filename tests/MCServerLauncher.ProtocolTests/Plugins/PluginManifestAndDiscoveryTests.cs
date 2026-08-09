using System.Reflection;
using MCServerLauncher.Daemon.API.Plugins;
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
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "event.publish",
            "instance.query",
            "rpc.register");

        var manifest = PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0");

        Assert.Equal("community.instance-health", manifest.Identity.Id);
        Assert.Equal("1.0.0", manifest.Identity.Version);
        Assert.Equal("PluginEntry.dll", manifest.EntryAssembly);
        Assert.Equal("Community.InstanceHealth.InstanceHealthPlugin", manifest.EntryType);
        Assert.True(manifest.HasFeature(PluginFeature.RpcRegister));
        Assert.True(manifest.HasFeature(PluginFeature.EventPublish));
        Assert.True(manifest.HasFeature(PluginFeature.InstanceQuery));
    }

    [Fact]
    public void ReadsManifestExtensionPointsAndIncludesThemInSemanticDigest()
    {
        using var first = PluginFixture.Create("extension-points-a");
        using var second = PluginFixture.Create("extension-points-b");
        first.WriteManifest(
            "community.java-provider",
            "1.0.0",
            "PluginEntry.dll",
            "Community.JavaProvider.Plugin",
            "[1.0.0,2.0.0)",
            "system.query",
            """
            ,"extensionPoints": [
              {
                "kind": "provider",
                "id": "provider.java",
                "priority": 10,
                "conflicts": ["provider.serverCore"]
              }
            ]
            """);
        second.WriteManifest(
            "community.java-provider",
            "01.00",
            "PluginEntry.dll",
            "Community.JavaProvider.Plugin",
            "[1.0, 2.0)",
            "system.query",
            """
            ,"extensionPoints": [
              {
                "conflicts": ["provider.serverCore"],
                "priority": 10,
                "id": "provider.java",
                "kind": "provider"
              }
            ]
            """);

        var firstManifest = PluginManifestReader.ReadAndValidate(first.BundleDirectory, "1.0.0");
        var secondManifest = PluginManifestReader.ReadAndValidate(second.BundleDirectory, "1.0.0");

        var extensionPoint = Assert.Single(firstManifest.ExtensionPoints);
        Assert.Equal("provider.java", extensionPoint.Id);
        Assert.Equal(PluginExtensionPointKind.Provider, extensionPoint.Kind);
        Assert.Equal(10, extensionPoint.Priority);
        Assert.Equal(firstManifest.ManifestDigest, secondManifest.ManifestDigest);
    }

    [Fact]
    public void ReadsManifestCommandsAndIncludesThemInSemanticDigest()
    {
        using var first = PluginFixture.Create("commands-a");
        using var second = PluginFixture.Create("commands-b");
        first.WriteManifest(
            "community.command-panel",
            "1.0.0",
            "PluginEntry.dll",
            "Community.CommandPanel.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"extensionPoints": [
              { "kind": "command", "id": "command.daemon" }
            ],
            "commands": [
              {
                "id": "refresh",
                "title": "Refresh",
                "description": "Refreshes panel state.",
                "target": "daemon"
              }
            ]
            """);
        second.WriteManifest(
            "community.command-panel",
            "01.00",
            "PluginEntry.dll",
            "Community.CommandPanel.Plugin",
            "[1.0, 2.0)",
            "rpc.register",
            """
            ,"commands": [
              {
                "target": "daemon",
                "description": "Refreshes panel state.",
                "title": "Refresh",
                "id": "refresh"
              }
            ],
            "extensionPoints": [
              { "id": "command.daemon", "kind": "command" }
            ]
            """);

        var firstManifest = PluginManifestReader.ReadAndValidate(first.BundleDirectory, "1.0.0");
        var secondManifest = PluginManifestReader.ReadAndValidate(second.BundleDirectory, "1.0.0");

        var command = Assert.Single(firstManifest.Commands);
        Assert.Equal("refresh", command.Id);
        Assert.Equal("daemon", command.Target);
        Assert.Equal(firstManifest.ManifestDigest, secondManifest.ManifestDigest);
    }

    [Fact]
    public void RejectsManifestCommandsWithoutDaemonCommandExtensionPoint()
    {
        using var fixture = PluginFixture.Create("commands-missing-extension-point");
        fixture.WriteManifest(
            "community.command-panel",
            "1.0.0",
            "PluginEntry.dll",
            "Community.CommandPanel.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"commands": [
              { "id": "refresh", "target": "daemon" }
            ]
            """);

        var exception = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));

        Assert.Equal("command_extension_point_missing", exception.Code);
    }

    [Fact]
    public void RejectsManifestCommandsWithoutRpcRegisterFeature()
    {
        using var fixture = PluginFixture.Create("commands-missing-feature");
        fixture.WriteManifest(
            "community.command-panel",
            "1.0.0",
            "PluginEntry.dll",
            "Community.CommandPanel.Plugin",
            "[1.0.0,2.0.0)",
            "system.query",
            """
            ,"extensionPoints": [
              { "kind": "command", "id": "command.daemon" }
            ],
            "commands": [
              { "id": "refresh", "target": "daemon" }
            ]
            """);

        var exception = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));

        Assert.Equal("command_feature_missing", exception.Code);
    }

    [Fact]
    public void RejectsUnknownManifestExtensionPointDeclarations()
    {
        using var fixture = PluginFixture.Create("private-patch");
        fixture.WriteManifest(
            "community.private-patch",
            "1.0.0",
            "PluginEntry.dll",
            "Community.PrivatePatch.Plugin",
            "[1.0.0,2.0.0)",
            "system.query",
            """
            ,"extensionPoints": [
              { "kind": "override", "id": "private.instanceBase.patch" }
            ]
            """);

        var exception = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));

        Assert.Equal("extension_point_unknown", exception.Code);
    }

    [Fact]
    public void NormalizesSemanticManifestDigestAcrossEquivalentJson()
    {
        using var first = PluginFixture.Create("first");
        using var second = PluginFixture.Create("second");
                first.WriteRawManifest(
                        """
                        {
                            "package": { "id": "community.instance-health", "version": "01.00" },
                            "entry": {
                                "assembly": "PluginEntry.dll",
                                "type": "Community.InstanceHealth.InstanceHealthPlugin"
                            },
                            "requires": {
                                "api": "[1.0,2.0)",
                                "features": ["rpc.register", "event.publish", "instance.query"]
                            }
                        }
                        """);
                second.WriteRawManifest(
            """
            {
              "$schema": "https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json",
              "requires": {
                                "features": ["event.publish", "instance.query", "rpc.register"],
                                "api": "[1.0.0, 2.0.0)"
              },
              "entry": {
                "type": "Community.InstanceHealth.InstanceHealthPlugin",
                "assembly": "PluginEntry.dll"
              },
            "package": { "version": "1.0.0", "id": "community.instance-health" }
            }
            """);

        var firstManifest = PluginManifestReader.ReadAndValidate(first.BundleDirectory, "1.0.0");
        var secondManifest = PluginManifestReader.ReadAndValidate(second.BundleDirectory, "1.0.0");

        Assert.Equal("1.0.0", firstManifest.Identity.Version);
        Assert.Equal("[1.0.0, 2.0.0)", firstManifest.ApiVersionRange.ToNormalizedString());
        Assert.Equal(firstManifest.ManifestDigest, secondManifest.ManifestDigest);
    }

    [Fact]
    public void ReadsVersionedPluginDependenciesAndNormalizesDigest()
    {
        using var first = PluginFixture.Create("consumer-a");
        using var second = PluginFixture.Create("consumer-b");
        first.WriteManifest(
            "community.consumer",
            "1.0.0",
            "PluginEntry.dll",
            "Community.Consumer.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"dependencies": {
              "version": 1,
              "plugins": [
                { "id": "community.provider-b", "version": "[2.0, 3.0)" },
                { "id": "community.provider-a", "version": "[1.0.0,2.0.0)" }
              ]
            }
            """);
        second.WriteManifest(
            "community.consumer",
            "01.00",
            "PluginEntry.dll",
            "Community.Consumer.Plugin",
            "[1.0, 2.0)",
            "rpc.register",
            """
            ,"dependencies": {
              "plugins": [
                { "version": "[1.0.0, 2.0.0)", "id": "community.provider-a" },
                { "version": "[2.0.0, 3.0.0)", "id": "community.provider-b" }
              ],
              "version": 1
            }
            """);

        var firstManifest = PluginManifestReader.ReadAndValidate(first.BundleDirectory, "1.0.0");
        var secondManifest = PluginManifestReader.ReadAndValidate(second.BundleDirectory, "1.0.0");

        Assert.Equal(
            ["community.provider-a", "community.provider-b"],
            firstManifest.PluginDependencies.Select(static dependency => dependency.Id).ToArray());
        Assert.Equal("[1.0.0, 2.0.0)", firstManifest.PluginDependencies[0].NormalizedVersionRange);
        Assert.Equal(firstManifest.ManifestDigest, secondManifest.ManifestDigest);
    }

    [Fact]
    public void RejectsInvalidPluginDependencies()
    {
        using var fixture = PluginFixture.Create("community.consumer");
        fixture.WriteManifest(
            "community.consumer",
            "1.0.0",
            "PluginEntry.dll",
            "Community.Consumer.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"dependencies": {
              "plugins": [{ "id": "community.provider", "version": "[1.0.0,2.0.0)" }]
            }
            """);
        var missingVersion = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("dependencies_version_missing", missingVersion.Code);

        fixture.WriteManifest(
            "community.consumer",
            "1.0.0",
            "PluginEntry.dll",
            "Community.Consumer.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"dependencies": {
              "version": 1,
              "plugins": [{ "id": "community.consumer", "version": "[1.0.0,2.0.0)" }]
            }
            """);
        var self = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("dependency_self", self.Code);

        fixture.WriteManifest(
            "community.consumer",
            "1.0.0",
            "PluginEntry.dll",
            "Community.Consumer.Plugin",
            "[1.0.0,2.0.0)",
            "rpc.register",
            """
            ,"dependencies": {
              "version": 1,
              "plugins": [
                { "id": "community.provider", "version": "[1.0.0,2.0.0)" },
                { "id": "community.provider", "version": "[1.0.0,2.0.0)" }
              ]
            }
            """);
        var duplicate = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("dependency_duplicate", duplicate.Code);
    }

    [Fact]
    public void ExtensionPointPlannerRejectsCrossPluginConflictsAndDuplicateOverrides()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-extension-points-").FullName;
        try
        {
            using var javaProvider = PluginFixture.Create("java-provider", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var serverCoreProvider = PluginFixture.Create("server-core-provider", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var processOverrideA = PluginFixture.Create("override-a", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var processOverrideB = PluginFixture.Create("override-b", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);

            javaProvider.WriteManifest(
                "community.java-provider",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "provider.java", "kind": "provider", "conflicts": ["provider.serverCore"] }
                ]
                """);
            serverCoreProvider.WriteManifest(
                "community.server-core-provider",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "provider.serverCore", "kind": "provider" }
                ]
                """);
            processOverrideA.WriteManifest(
                "community.override-a",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "override.process.launcher", "kind": "override", "replaces": "builtin.processLauncher" }
                ]
                """);
            processOverrideB.WriteManifest(
                "community.override-b",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "override.process.launcher", "kind": "override", "replaces": "builtin.processLauncher" }
                ]
                """);

            var discovered = new PluginDiscovery("1.0.0").Discover(root);
            var planned = PluginExtensionPointPlanner.Plan(discovered.Plugins);

            Assert.Empty(planned.Plugins);
            Assert.Equal(4, planned.Failures.Length);
            Assert.Contains(planned.Failures, failure => failure.PluginId == "community.java-provider" && failure.Code == "extension_point_conflict");
            Assert.Contains(planned.Failures, failure => failure.PluginId == "community.server-core-provider" && failure.Code == "extension_point_conflict");
            Assert.Contains(planned.Failures, failure => failure.PluginId == "community.override-a" && failure.Code == "extension_point_duplicate");
            Assert.Contains(planned.Failures, failure => failure.PluginId == "community.override-b" && failure.Code == "extension_point_duplicate");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DependencyPlannerBlocksConsumersAfterExtensionPointAdmissionSkipsProvider()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-extension-dependencies-").FullName;
        try
        {
            using var provider = PluginFixture.Create("provider", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var conflict = PluginFixture.Create("conflict", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var consumer = PluginFixture.Create("consumer", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            provider.WriteManifest(
                "community.provider",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "provider.java", "kind": "provider", "conflicts": ["provider.serverCore"] }
                ]
                """);
            conflict.WriteManifest(
                "community.conflict",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"extensionPoints": [
                  { "id": "provider.serverCore", "kind": "provider" }
                ]
                """);
            consumer.WriteManifest(
                "community.consumer",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "system.query",
                """
                ,"dependencies": {
                  "version": 1,
                  "plugins": [{ "id": "community.provider", "version": "[1.0.0,2.0.0)" }]
                }
                """);

            var discovered = new PluginDiscovery("1.0.0").Discover(root);
            var extensionAdmission = PluginExtensionPointPlanner.Plan(discovered.Plugins);
            var dependencyAdmission = PluginDependencyPlanner.Plan(extensionAdmission.Plugins);

            Assert.Equal(["community.consumer"], extensionAdmission.Plugins.Select(static plugin => plugin.Identity.Id).ToArray());
            Assert.Contains(dependencyAdmission.Failures, failure => failure.PluginId == "community.consumer" && failure.Code == "dependency_missing");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DependencyPlannerOrdersProvidersAndSkipsBrokenGraphs()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-dependencies-").FullName;
        try
        {
            using var provider = PluginFixture.Create("z-provider", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var consumer = PluginFixture.Create("a-consumer", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            provider.WriteManifest(
                "community.provider",
                "1.2.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "rpc.register");
            consumer.WriteManifest(
                "community.consumer",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "rpc.register",
                """
                ,"dependencies": {
                  "version": 1,
                  "plugins": [{ "id": "community.provider", "version": "[1.0.0,2.0.0)" }]
                }
                """);

            var discovered = new PluginDiscovery("1.0.0").Discover(root);
            var planned = PluginDependencyPlanner.Plan(discovered.Plugins);

            Assert.Empty(planned.Failures);
            Assert.Equal(["community.provider", "community.consumer"], planned.OrderedPlugins.Select(static plugin => plugin.Identity.Id).ToArray());

            consumer.WriteManifest(
                "community.consumer",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "rpc.register",
                """
                ,"dependencies": {
                  "version": 1,
                  "plugins": [{ "id": "community.missing", "version": "[1.0.0,2.0.0)" }]
                }
                """);
            var missing = PluginDependencyPlanner.Plan(new PluginDiscovery("1.0.0").Discover(root).Plugins);
            Assert.Equal(["community.provider"], missing.OrderedPlugins.Select(static plugin => plugin.Identity.Id).ToArray());
            Assert.Contains(missing.Failures, failure => failure.PluginId == "community.consumer" && failure.Code == "dependency_missing");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DependencyPlannerRejectsCycles()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-plugin-cycle-").FullName;
        try
        {
            using var first = PluginFixture.Create("first", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            using var second = PluginFixture.Create("second", root, typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
            first.WriteManifest(
                "community.first",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "rpc.register",
                """
                ,"dependencies": {
                  "version": 1,
                  "plugins": [{ "id": "community.second", "version": "[1.0.0,2.0.0)" }]
                }
                """);
            second.WriteManifest(
                "community.second",
                "1.0.0",
                "PluginEntry.dll",
                "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
                "[1.0.0,2.0.0)",
                "rpc.register",
                """
                ,"dependencies": {
                  "version": 1,
                  "plugins": [{ "id": "community.first", "version": "[1.0.0,2.0.0)" }]
                }
                """);

            var planned = PluginDependencyPlanner.Plan(new PluginDiscovery("1.0.0").Discover(root).Plugins);

            Assert.Empty(planned.OrderedPlugins);
            Assert.Equal(2, planned.Failures.Count(static failure => failure.Code == "dependency_cycle"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsDuplicateJsonProperties()
    {
        using var fixture = PluginFixture.Create("duplicate-json");
        fixture.WriteRawManifest(
            """
            {
                            "package": {
                                "id": "community.instance-health",
                                "version": "1.0.0",
                                "version": "1.0.0"
                            },
              "entry": {
                "assembly": "PluginEntry.dll",
                "type": "Community.InstanceHealth.InstanceHealthPlugin"
              },
              "requires": {
                "api": "[1.0.0,2.0.0)",
                "features": ["rpc.register"]
              }
            }
            """);

        var exception = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));

        Assert.Equal("manifest_invalid", exception.Code);
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
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("api_version_unsupported", rangeException.Code);

        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "unknown.feature");

        var featureException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0"));
        Assert.Equal("feature_unsupported", featureException.Code);
    }

    [Fact]
    public void AcceptsEventSubscribeFeature()
    {
        using var fixture = PluginFixture.Create("community.instance-health");
        fixture.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "event.subscribe");

        var manifest = PluginManifestReader.ReadAndValidate(fixture.BundleDirectory, "1.0.0");
        Assert.True(manifest.HasFeature(PluginFeature.EventSubscribe));
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
            "[1.0.0,2.0.0)",
            " rpc.register");
        var paddedException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(padded.BundleDirectory, "1.0.0"));
        Assert.Equal("feature_invalid", paddedException.Code);

        // Malformed identifiers must be reported as feature_invalid, not feature_unsupported.
        using var malformed = PluginFixture.Create("community.instance-health");
        malformed.WriteManifest(
            "community.instance-health",
            "1.0.0",
            "PluginEntry.dll",
            "Community.InstanceHealth.InstanceHealthPlugin",
            "[1.0.0,2.0.0)",
            "rpc/register");
        var malformedException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(malformed.BundleDirectory, "1.0.0"));
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
            "[1.0.0,2.0.0)",
            ["rpc.register", "rpc.register"]);

        var duplicateException = Assert.Throws<PluginManifestException>(
            () => PluginManifestReader.ReadAndValidate(duplicateFixture.BundleDirectory, "1.0.0"));
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

        using var nestedFixture = PluginFixture.Create(
            "nested",
            Directory.CreateTempSubdirectory("mcsl-plugin-nested-").FullName,
            typeof(MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin).Assembly.Location);
        nestedFixture.WriteManifest(
            "nested",
            "1.0.0",
            "PluginEntry.dll",
            "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
            "[1.0.0,2.0.0)",
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
                PluginManifestReader.ReadAndValidate(nestedFixture.BundleDirectory, "1.0.0")));
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

        public void WriteRawManifest(string json) =>
            File.WriteAllText(Path.Combine(BundleDirectory, "mcsl-plugin.json"), json);

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
