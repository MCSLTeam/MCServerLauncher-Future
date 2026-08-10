using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using MCServerLauncher.WPF.Features.Plugins;

namespace MCServerLauncher.WPF.Tests.Plugins;

public sealed class PluginMpxPackageValidatorTests
{
    private const string ValidUi = """
    {
      "schema": "mcsl.ui.v1",
      "root": {
        "type": "Card",
        "props": { "Title": "{state.title}" },
        "children": [
          { "type": "Text", "props": { "Text": "Ready" } },
          { "type": "Button", "props": { "Text": "Refresh", "OnClick": "refresh" } }
        ]
      }
    }
    """;

    private const string ValidScript = "export function onLoad() {}\n";

    [Fact]
    public void PackageBuilderCompilesAuthoringUiIntoValidatedRuntimePackage()
    {
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.builder",
            "1.0.0",
            UiAuthoringJson5: """
            {
              schema: "mcsl.ui.v1",
              root: {
                type: "Card",
                props: { Title: "{state.title}" },
                children: [{ type: "Text", props: { Text: "Ready" } }],
              },
            }
            """,
            HostCapabilities: ["ui.state"]));

        Assert.True(built.Ok, string.Join(Environment.NewLine, built.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var validated = PluginMpxPackageValidator.Validate(built.PackageBytes!);

        Assert.True(validated.Ok, FormatDiagnostics(validated.Diagnostics));
        Assert.Equal(PluginUiComponentType.Card, validated.Package!.UiSchema!.Root.Type);
    }

    [Fact]
    public void PackageBuilderBuildsDaemonPluginTargetWithExtensionPointDeclarations()
    {
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.provider",
            "1.0.0",
            DaemonPlugin: Encoding.UTF8.GetBytes("not-a-real-assembly-yet"),
            ExtensionPoints:
            [
                new PluginMpxExtensionPoint("provider", "provider.java", "daemon")
            ]));

        Assert.True(built.Ok, string.Join(Environment.NewLine, built.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var validated = PluginMpxPackageValidator.Validate(built.PackageBytes!);

        Assert.True(validated.Ok, FormatDiagnostics(validated.Diagnostics));
        Assert.Equal(["daemon"], validated.Package!.DeploymentPlan.Scopes);
        Assert.Equal("provider.java", validated.Package.DeploymentPlan.Daemon?.ExtensionPoints[0].Id);
    }

    [Fact]
    public void PackageBuilderBuildsCommandAwareUnifiedPackage()
    {
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.command-panel",
            "1.0.0",
            UiAuthoringJson5: """
            {
              schema: "mcsl.ui.v1",
              root: {
                type: "Button",
                props: {
                  Text: "Refresh",
                  OnClick: { Command: "refresh", Params: { mode: "{state.mode}" } },
                },
              },
            }
            """,
            DaemonPlugin: Encoding.UTF8.GetBytes("not-a-real-assembly-yet"),
            ExtensionPoints:
            [
                new PluginMpxExtensionPoint("command", "command.daemon", "daemon")
            ],
            Commands:
            [
                new PluginMpxCommand("refresh", "Refresh", "Refreshes panel state.", "daemon")
            ]));

        Assert.True(built.Ok, string.Join(Environment.NewLine, built.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var validated = PluginMpxPackageValidator.Validate(built.PackageBytes!);

        Assert.True(validated.Ok, FormatDiagnostics(validated.Diagnostics));
        Assert.Equal(["client", "daemon"], validated.Package!.DeploymentPlan.Scopes);
        Assert.Equal("refresh", validated.Package.Commands[0].Id);
    }

    [Fact]
    public void PackageBuilderBuildsThemeAuthoringIntoValidatedRuntimePackage()
    {
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.theme-builder",
            "1.0.0",
            ThemeJson: """
            {
              "schema": "mcsl.theme.v1",
              "colors": { "accent": "#0070C0" },
              "spacing": { "md": 12 }
            }
            """));

        Assert.True(built.Ok, string.Join(Environment.NewLine, built.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var validated = PluginMpxPackageValidator.Validate(built.PackageBytes!);

        Assert.True(validated.Ok, FormatDiagnostics(validated.Diagnostics));
        Assert.Null(validated.Package!.UiSchema);
        Assert.Equal("#0070c0", validated.Package.Theme!.Colors["accent"]);
        Assert.Equal(12d, validated.Package.Theme.Spacing["md"]);
    }

    [Fact]
    public void PackageBuilderBuildsClientOnlyResourceExtensionPackage()
    {
        var icon = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.icon-pack",
            "1.0.0",
            Resources:
            [
                new PluginMpxPackageResourceInput("client/resources/icon.png", icon, "image/png")
            ]));

        Assert.True(built.Ok, string.Join(Environment.NewLine, built.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var validated = PluginMpxPackageValidator.Validate(built.PackageBytes!);

        Assert.True(validated.Ok, FormatDiagnostics(validated.Diagnostics));
        Assert.Equal(["client"], validated.Package!.DeploymentPlan.Scopes);
        Assert.Equal("client/resources/icon.png", validated.Package.DeploymentPlan.Client?.Resources[0].Path);
    }

    [Fact]
    public void PackageBuilderRejectsInvalidAuthoringUiBeforePackaging()
    {
        var built = PluginMpxPackageBuilder.Build(new PluginMpxPackageBuildInput(
            "community.example.invalid",
            "1.0.0",
            UiAuthoringJson5: """
            { schema: "mcsl.ui.v1", root: { type: "WebView" } }
            """));

        Assert.False(built.Ok);
        Assert.Contains(built.Diagnostics, diagnostic => diagnostic.Code == "component_unknown");
    }

    [Fact]
    public void ValidatorAcceptsHashedPackageAndReturnsUiSchema()
    {
        var packageBytes = BuildMpxPackage();

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Package);
        Assert.Equal("community.example.status-panel", result.Package.PluginId);
        Assert.Equal(PluginUiComponentType.Card, result.Package.UiSchema!.Root.Type);
        Assert.Matches("^[a-f0-9]{64}$", result.Package.FileDigests["client/ui.json"]);
    }

    [Fact]
    public void ClientExtensionManagerCachesRenderableUiPackages()
    {
        var result = PluginMpxPackageValidator.Validate(BuildMpxPackage());
        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));

        var manager = new PluginClientExtensionManager();
        var install = manager.Install(result.Package!);

        Assert.True(install.Ok);
        Assert.Same(install.Entry, manager.Get("community.example.status-panel"));
        Assert.NotNull(install.Entry!.UiSchema);
    }

    [Fact]
    public void ClientExtensionManagerPersistsAndRestoresClientCacheEntries()
    {
        var validation = PluginMpxPackageValidator.Validate(BuildMpxPackage());
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));
        var root = Directory.CreateTempSubdirectory("mcsl-client-extension-cache-").FullName;
        try
        {
            var store = new PluginClientExtensionCacheStore(root);
            var writer = new PluginClientExtensionManager(store);
            var install = writer.InstallPersisted(validation.Package!);
            Assert.True(install.Ok);

            var reader = new PluginClientExtensionManager(store);
            reader.Restore();

            var restored = reader.Get("community.example.status-panel");
            Assert.NotNull(restored);
            Assert.Equal(PluginUiComponentType.Card, restored.UiSchema!.Root.Type);
            Assert.Empty(restored.CachedFiles);
            Assert.True(reader.UninstallPersisted("community.example.status-panel"));

            var afterDelete = new PluginClientExtensionManager(store);
            afterDelete.Restore();
            Assert.Empty(afterDelete.List());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtensionCenterServiceInstallsRestoresSummarizesAndUninstallsPackages()
    {
        var packageBytes = BuildMpxPackage();
        var root = Directory.CreateTempSubdirectory("mcsl-extension-center-").FullName;
        var packagePath = Path.Combine(root, "status-panel.mpx");
        File.WriteAllBytes(packagePath, packageBytes);
        try
        {
            var service = new PluginExtensionCenterService(Path.Combine(root, "cache"));
            var review = service.ReviewPackage(packagePath);
            Assert.True(review.Ok, review.Message);
            Assert.Equal("community.example.status-panel", review.Review!.Package.PluginId);
            Assert.Empty(service.Restore());

            var install = service.InstallReviewedPackage(review.Review);
            Assert.True(install.Ok, install.Message);
            Assert.Equal("community.example.status-panel", install.Entry!.Id);

            var restored = service.Restore();
            Assert.Single(restored);
            var summary = PluginExtensionCenterService.Summarize(restored[0]);
            Assert.True(summary.HasUi);
            Assert.Contains("client/ui.json", summary.Resources);
            Assert.Contains("bundle.js", summary.Resources);

            Assert.True(service.Uninstall("community.example.status-panel"));
            Assert.Empty(service.Restore());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClientExtensionManagerPersistsValidatedClientPayloadFiles()
    {
        var iconBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var daemonBundleBytes = Encoding.UTF8.GetBytes("daemon-bundle");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]>
            {
                ["client/resources/icon.png"] = iconBytes,
                ["daemon/plugin-bundle.zip"] = daemonBundleBytes
            },
            mutateManifest: manifest =>
            {
                var resources = (JsonArray)manifest["resources"]!;
                resources.Add(new JsonObject
                {
                    ["path"] = "client/resources/icon.png",
                    ["sha256"] = Sha256(iconBytes),
                    ["mime"] = "image/png",
                    ["bytes"] = iconBytes.Length
                });
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(daemonBundleBytes)
                    }
                };
            });
        var validation = PluginMpxPackageValidator.Validate(packageBytes);
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));
        var root = Directory.CreateTempSubdirectory("mcsl-client-extension-cache-").FullName;
        try
        {
            var store = new PluginClientExtensionCacheStore(root);
            var writer = new PluginClientExtensionManager(store);
            var install = writer.InstallPersisted(validation.Package!, packageBytes);
            Assert.True(install.Ok, install.Message);

            Assert.True(install.Entry!.CachedFiles.TryGetValue("client/ui.json", out var cachedUi));
            Assert.True(install.Entry.CachedFiles.TryGetValue("bundle.js", out var cachedScript));
            Assert.True(install.Entry.CachedFiles.TryGetValue("client/resources/icon.png", out var cachedIcon));
            Assert.True(install.Entry.CachedFiles.TryGetValue("daemon/plugin-bundle.zip", out var cachedDaemonBundle));
            Assert.True(File.Exists(cachedUi));
            Assert.True(File.Exists(cachedScript));
            Assert.Equal(iconBytes, File.ReadAllBytes(cachedIcon!));
            Assert.Equal(daemonBundleBytes, File.ReadAllBytes(cachedDaemonBundle!));

            var reader = new PluginClientExtensionManager(store);
            reader.Restore();
            var restored = reader.Get("community.example.status-panel");
            Assert.NotNull(restored);
            Assert.True(restored.CachedFiles.TryGetValue("client/resources/icon.png", out cachedIcon));
            Assert.True(restored.CachedFiles.TryGetValue("daemon/plugin-bundle.zip", out cachedDaemonBundle));
            Assert.True(File.Exists(cachedIcon));
            Assert.True(File.Exists(cachedDaemonBundle));

            File.WriteAllBytes(cachedIcon!, [0, 1, 2, 3]);
            var tampered = new PluginClientExtensionManager(store);
            tampered.Restore();
            Assert.Empty(tampered.List());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClientExtensionRuntimeHostRendersRestoredCacheEntryAndResolvesPayloadPaths()
    {
        var iconBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["client/resources/icon.png"] = iconBytes },
            mutateManifest: manifest =>
            {
                var resources = (JsonArray)manifest["resources"]!;
                resources.Add(new JsonObject
                {
                    ["path"] = "client/resources/icon.png",
                    ["sha256"] = Sha256(iconBytes),
                    ["mime"] = "image/png",
                    ["bytes"] = iconBytes.Length
                });
            });
        var validation = PluginMpxPackageValidator.Validate(packageBytes);
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));
        var root = Directory.CreateTempSubdirectory("mcsl-client-extension-cache-").FullName;
        try
        {
            var store = new PluginClientExtensionCacheStore(root);
            var writer = new PluginClientExtensionManager(store);
            var install = writer.InstallPersisted(validation.Package!, packageBytes);
            Assert.True(install.Ok, install.Message);

            var reader = new PluginClientExtensionManager(store);
            reader.Restore();
            var restored = reader.Get("community.example.status-panel");
            Assert.NotNull(restored);
            Assert.Contains("ui.state", restored.HostCapabilities);
            Assert.Contains("daemon.instance.query", restored.HostCapabilities);

            var icon = PluginClientExtensionRuntimeHost.ResolveCachedPath(restored, "client/resources/icon.png");
            Assert.True(icon.Ok, icon.Message);
            Assert.Equal(iconBytes, File.ReadAllBytes(icon.Path!));
            var hidden = PluginClientExtensionRuntimeHost.ResolveCachedPath(restored, "client/resources/hidden.png");
            Assert.False(hidden.Ok);
            Assert.Equal("resource_undeclared", hidden.Code);

            RunOnSta(async () =>
            {
                var runtime = new PluginClientExtensionRuntimeHost(runtimeFactory: () => new StatePatchingRuntime());
                var result = await runtime.RenderAsync(
                    new PluginClientExtensionRenderRequest(restored, new Dictionary<string, object?> { ["title"] = "Original" }),
                    Dispatcher.CurrentDispatcher);

                Assert.True(result.Ok, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
                Assert.True(result.ScriptResult?.Success);
                Assert.Contains(result.Logs, log => log.Message == "preview loaded");
                var border = Assert.IsType<Border>(result.Root);
                var content = Assert.IsType<StackPanel>(border.Child);
                Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Patched");
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClientExtensionRuntimeHostDispatchesDeclaredCommandsOnly()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(daemonPlugin)
                    }
                };
                manifest["extensionPoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "command",
                        ["id"] = "command.daemon",
                        ["target"] = "daemon"
                    }
                };
                manifest["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "refresh",
                        ["target"] = "daemon"
                    }
                };
            });
        var validation = PluginMpxPackageValidator.Validate(packageBytes);
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));
        var manager = new PluginClientExtensionManager();
        var install = manager.Install(validation.Package!);
        Assert.True(install.Ok, install.Message);

        var dispatcher = new RecordingCommandDispatcher();
        var runtime = new PluginClientExtensionRuntimeHost(commandDispatcher: dispatcher);
        var accepted = await runtime.DispatchCommandAsync(
            install.Entry!,
            new PluginUiCommandEvent("refresh", new Dictionary<string, object?> { ["mode"] = "safe" }),
            "request-1");

        Assert.True(accepted.Ok, string.Join(Environment.NewLine, accepted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var call = Assert.Single(dispatcher.Calls);
        Assert.Equal("community.example.status-panel", call.PluginId);
        Assert.Equal("refresh", call.Command.Command);

        var rejected = await runtime.DispatchCommandAsync(install.Entry!, new PluginUiCommandEvent("missing"));
        Assert.False(rejected.Ok);
        Assert.Equal("command_undeclared", Assert.Single(rejected.Diagnostics).Code);
        Assert.Single(dispatcher.Calls);
    }

    [Fact]
    public void ClientExtensionManagerDoesNotPersistDaemonOnlyPackagesIntoClientCache()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var validation = PluginMpxPackageValidator.Validate(BuildMpxPackage(
            ui: null,
            script: null,
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                manifest["runtime"] = new JsonObject { ["daemonApi"] = "[1.0.0,2.0.0)" };
                manifest["targets"] = new JsonObject
                {
                    ["daemon"] = new JsonObject
                    {
                        ["plugin"] = new JsonObject
                        {
                            ["path"] = "daemon/plugin-bundle.zip",
                            ["sha256"] = Sha256(daemonPlugin)
                        }
                    }
                };
            }));
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));
        var root = Directory.CreateTempSubdirectory("mcsl-client-extension-cache-").FullName;
        try
        {
            var manager = new PluginClientExtensionManager(new PluginClientExtensionCacheStore(root));
            var install = manager.InstallPersisted(validation.Package!);

            Assert.False(install.Ok);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClientExtensionManagerCachesThemePackagesWithoutUiSchema()
    {
        var themeBytes = Encoding.UTF8.GetBytes("""
        {"schema":"mcsl.theme.v1","colors":{"accent":"#0070c0"},"spacing":{"md":8}}
        """);
        var validation = PluginMpxPackageValidator.Validate(BuildMpxPackage(
            ui: null,
            script: null,
            extraEntries: new Dictionary<string, byte[]> { ["client/theme.json"] = themeBytes },
            mutateManifest: manifest =>
            {
                manifest["runtime"] = new JsonObject();
                manifest["targets"] = new JsonObject
                {
                    ["client"] = new JsonObject
                    {
                        ["theme"] = new JsonObject
                        {
                            ["path"] = "client/theme.json",
                            ["sha256"] = Sha256(themeBytes)
                        }
                    }
                };
                var permissions = (JsonObject)manifest["permissions"]!;
                permissions["host"] = new JsonArray();
            }));
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));

        var manager = new PluginClientExtensionManager();
        var install = manager.Install(validation.Package!);

        Assert.True(install.Ok);
        Assert.Null(install.Entry!.UiSchema);
        Assert.Equal("#0070c0", install.Entry.Theme!.Colors["accent"]);
        Assert.Equal("client/theme.json", install.Entry.DeploymentPlan.Client?.Theme?.Path);
    }

    [Fact]
    public void ClientExtensionManagerRejectsDaemonOnlyPackages()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var validation = PluginMpxPackageValidator.Validate(BuildMpxPackage(
            ui: null,
            script: null,
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                manifest["runtime"] = new JsonObject { ["daemonApi"] = "[1.0.0,2.0.0)" };
                manifest["targets"] = new JsonObject
                {
                    ["daemon"] = new JsonObject
                    {
                        ["plugin"] = new JsonObject
                        {
                            ["path"] = "daemon/plugin-bundle.zip",
                            ["sha256"] = Sha256(daemonPlugin)
                        }
                    }
                };
            }));
        Assert.True(validation.Ok, FormatDiagnostics(validation.Diagnostics));

        var manager = new PluginClientExtensionManager();
        var install = manager.Install(validation.Package!);

        Assert.False(install.Ok);
        Assert.Equal("client_target_missing", install.Code);
    }

    [Fact]
    public void ValidatorOutputCanBeRenderedByWpfRenderer()
    {
        var packageBytes = BuildMpxPackage();
        var result = PluginMpxPackageValidator.Validate(packageBytes);
        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));

        RunOnSta(() =>
        {
            List<PluginUiEvent> events = [];
            var host = new RecordingPluginUiHost(events);
            var state = new Dictionary<string, object?> { ["title"] = "Status Panel" };
            var element = new PluginUiRenderer().RenderOnCurrentThread(result.Package!.UiSchema!, state, host);

            var border = Assert.IsType<Border>(element);
            var content = Assert.IsType<StackPanel>(border.Child);
            Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Status Panel");
        });
    }

    [Fact]
    public void PreviewHostValidatesRunsRuntimeAndRendersPackage()
    {
        var packageBytes = BuildMpxPackage();

        RunOnSta(async () =>
        {
            var preview = new PluginMpxPreviewHost(runtimeFactory: () => new StatePatchingRuntime());
            var result = await preview.RenderAsync(
                new PluginMpxPreviewRequest(packageBytes, new Dictionary<string, object?> { ["title"] = "Original" }),
                Dispatcher.CurrentDispatcher);

            Assert.True(result.Ok, FormatDiagnostics(result.PackageDiagnostics));
            Assert.True(result.ScriptResult?.Success);
            Assert.Contains(result.Logs, log => log.Message == "preview loaded");
            var border = Assert.IsType<Border>(result.Root);
            var content = Assert.IsType<StackPanel>(border.Child);
            Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Patched");
        });
    }

    [Fact]
    public void PreviewHostRendersDeclarativePackageWithoutScriptRuntime()
    {
        var packageBytes = BuildMpxPackage(script: null);

        RunOnSta(async () =>
        {
            var preview = new PluginMpxPreviewHost(runtimeFactory: () => new ThrowingRuntime());
            var result = await preview.RenderAsync(
                new PluginMpxPreviewRequest(packageBytes, new Dictionary<string, object?> { ["title"] = "Declarative" }),
                Dispatcher.CurrentDispatcher);

            Assert.True(result.Ok, FormatDiagnostics(result.PackageDiagnostics));
            Assert.True(result.ScriptResult?.Success);
            var border = Assert.IsType<Border>(result.Root);
            var content = Assert.IsType<StackPanel>(border.Child);
            Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Declarative");
        });
    }

    [Fact]
    public void PreviewHostBlocksStateMutationWithoutManifestCapability()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var permissions = (JsonObject)manifest["permissions"]!;
            permissions["host"] = new JsonArray("daemon.instance.query");
        });

        RunOnSta(async () =>
        {
            var preview = new PluginMpxPreviewHost(runtimeFactory: () => new StatePatchingRuntime());
            var result = await preview.RenderAsync(
                new PluginMpxPreviewRequest(packageBytes, new Dictionary<string, object?> { ["title"] = "Original" }),
                Dispatcher.CurrentDispatcher);

            Assert.True(result.Ok, FormatDiagnostics(result.PackageDiagnostics));
            Assert.Equal("script_exception", result.ScriptResult?.Code);
            var border = Assert.IsType<Border>(result.Root);
            var content = Assert.IsType<StackPanel>(border.Child);
            Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Original");
            Assert.DoesNotContain(content.Children.OfType<TextBlock>(), text => text.Text == "Patched");
        });
    }

    [Fact]
    public void PreviewHostTimesOutHangingRuntimeAndStillRendersPackageUi()
    {
        var packageBytes = BuildMpxPackage();

        RunOnSta(async () =>
        {
            var preview = new PluginMpxPreviewHost(
                runtimeFactory: () => new HangingRuntime(),
                scriptOptions: new PluginScriptRuntimeOptions(ExecutionTimeout: TimeSpan.FromMilliseconds(5)));
            var result = await preview.RenderAsync(
                new PluginMpxPreviewRequest(packageBytes, new Dictionary<string, object?> { ["title"] = "Still Rendered" }),
                Dispatcher.CurrentDispatcher);

            Assert.True(result.Ok, FormatDiagnostics(result.PackageDiagnostics));
            Assert.Equal("script_timeout", result.ScriptResult?.Code);
            var border = Assert.IsType<Border>(result.Root);
            var content = Assert.IsType<StackPanel>(border.Child);
            Assert.Contains(content.Children.OfType<TextBlock>(), text => text.Text == "Still Rendered");
        });
    }

    [Fact]
    public void ValidatorRejectsZipSlipPathsBeforePayloadAdmission()
    {
        var packageBytes = BuildZip(new Dictionary<string, byte[]>
        {
            ["../evil.txt"] = Encoding.UTF8.GetBytes("owned"),
            ["manifest.json"] = Encoding.UTF8.GetBytes("{}")
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Equal("path_traversal", result.Diagnostics[0].Code);
    }

    [Fact]
    public void ValidatorRejectsManifestPayloadHashMismatch()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var targets = (JsonObject)manifest["targets"]!;
            var client = (JsonObject)targets["client"]!;
            var ui = (JsonObject)client["ui"]!;
            ui["sha256"] = new string('0', 64);
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "sha256_mismatch");
    }

    [Fact]
    public void ValidatorAcceptsDeclaredDaemonPluginTargetPayload()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(daemonPlugin)
                    }
                };
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));
        Assert.Matches("^[a-f0-9]{64}$", result.Package!.FileDigests["daemon/plugin-bundle.zip"]);
        Assert.Equal(["client", "daemon"], result.Package.DeploymentPlan.Scopes);
        Assert.Equal("daemon/plugin-bundle.zip", result.Package.DeploymentPlan.Daemon?.Plugin?.Path);
    }

    [Fact]
    public void ValidatorRejectsSingleDllDaemonPayloadBecauseInstallRequiresBundle()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin.dll"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin.dll",
                        ["sha256"] = Sha256(daemonPlugin)
                    }
                };
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "daemon_bundle_path_invalid");
    }

    [Fact]
    public void ValidatorAcceptsClientOnlyThemeExtensionPackage()
    {
        var themeBytes = Encoding.UTF8.GetBytes("""
        {"schema":"mcsl.theme.v1","colors":{"accent":"#0070c0"},"spacing":{"md":8}}
        """);
        var packageBytes = BuildMpxPackage(
            ui: null,
            script: null,
            extraEntries: new Dictionary<string, byte[]> { ["client/theme.json"] = themeBytes },
            mutateManifest: manifest =>
            {
                manifest["runtime"] = new JsonObject();
                manifest["targets"] = new JsonObject
                {
                    ["client"] = new JsonObject
                    {
                        ["theme"] = new JsonObject
                        {
                            ["path"] = "client/theme.json",
                            ["sha256"] = Sha256(themeBytes)
                        }
                    }
                };
                var permissions = (JsonObject)manifest["permissions"]!;
                permissions["host"] = new JsonArray();
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));
        Assert.Null(result.Package!.UiSchema);
        Assert.Equal(["client"], result.Package.DeploymentPlan.Scopes);
        Assert.Equal("#0070c0", result.Package.Theme!.Colors["accent"]);
        Assert.Equal(8d, result.Package.Theme.Spacing["md"]);
        Assert.Equal("client/theme.json", result.Package.DeploymentPlan.Client?.Theme?.Path);
    }

    [Fact]
    public void ValidatorAcceptsDaemonOnlyProviderExtensionPackage()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            ui: null,
            script: null,
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                manifest["runtime"] = new JsonObject { ["daemonApi"] = "[1.0.0,2.0.0)" };
                manifest["targets"] = new JsonObject
                {
                    ["daemon"] = new JsonObject
                    {
                        ["plugin"] = new JsonObject
                        {
                            ["path"] = "daemon/plugin-bundle.zip",
                            ["sha256"] = Sha256(daemonPlugin)
                        }
                    }
                };
                manifest["extensionPoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "provider",
                        ["id"] = "provider.java",
                        ["target"] = "daemon"
                    }
                };
                var permissions = (JsonObject)manifest["permissions"]!;
                permissions["host"] = new JsonArray();
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));
        Assert.Null(result.Package!.UiSchema);
        Assert.Equal(["daemon"], result.Package.DeploymentPlan.Scopes);
        Assert.Equal("provider.java", result.Package.DeploymentPlan.Daemon?.ExtensionPoints[0].Id);
    }

    [Fact]
    public void ValidatorAcceptsDeclaredDaemonCommandBindings()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var commandUi = """
        {
          "schema": "mcsl.ui.v1",
          "root": {
            "type": "Button",
            "props": {
              "Text": "Refresh",
              "OnClick": {
                "Command": "refresh",
                "Params": { "mode": "{state.mode}" }
              }
            }
          }
        }
        """;
        var packageBytes = BuildMpxPackage(
            ui: commandUi,
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(daemonPlugin)
                    }
                };
                manifest["extensionPoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "command",
                        ["id"] = "command.daemon",
                        ["target"] = "daemon"
                    }
                };
                manifest["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "refresh",
                        ["title"] = "Refresh",
                        ["description"] = "Refreshes panel state.",
                        ["target"] = "daemon"
                    }
                };
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, FormatDiagnostics(result.Diagnostics));
        Assert.Equal("refresh", result.Package!.Commands[0].Id);
        Assert.Equal("refresh", result.Package.DeploymentPlan.Daemon?.Commands[0].Id);
    }

    [Fact]
    public void ValidatorRejectsUndeclaredUiCommandBindings()
    {
        var packageBytes = BuildMpxPackage(ui: """
        {
          "schema": "mcsl.ui.v1",
          "root": {
            "type": "Button",
            "props": { "Text": "Refresh", "OnClick": { "Command": "refresh" } }
          }
        }
        """);

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "command_undeclared");
    }

    [Fact]
    public void ValidatorRejectsCommandDeclarationsWithoutDaemonExtensionPoint()
    {
        var daemonPlugin = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = daemonPlugin },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(daemonPlugin)
                    }
                };
                manifest["commands"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "refresh",
                        ["target"] = "daemon"
                    }
                };
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "command_extension_point_missing");
    }

    [Fact]
    public void ValidatorRejectsIllegalExtensionPointDeclarations()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            manifest["extensionPoints"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "override",
                    ["id"] = "private.instanceBase.patch",
                    ["target"] = "daemon"
                }
            };
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "extension_point_unknown");
    }

    [Fact]
    public void ValidatorRejectsUnknownExtensionTargets()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var targets = (JsonObject)manifest["targets"]!;
            var client = (JsonObject)targets["client"]!;
            targets["browser"] = new JsonObject
            {
                ["ui"] = client["ui"]?.DeepClone()
            };
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "target_unknown");
    }

    [Fact]
    public void ValidatorRejectsUnknownHostCapabilities()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var permissions = (JsonObject)manifest["permissions"]!;
            permissions["host"] = new JsonArray("ui.state", "daemon.file.write");
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "capability_unknown_or_unsupported");
    }

    [Fact]
    public void ValidatorRejectsUnknownEventPermissions()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var permissions = (JsonObject)manifest["permissions"]!;
            permissions["host"] = new JsonArray("daemon.event.subscribe");
            permissions["events"] = new JsonArray("daemon.file.changed");
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "event_permission_unknown");
    }

    [Fact]
    public void ValidatorAcceptsPackageOwnedExtensionEventPermission()
    {
        var pluginBytes = Encoding.UTF8.GetBytes("not-a-real-assembly-yet");
        var packageBytes = BuildMpxPackage(
            extraEntries: new Dictionary<string, byte[]> { ["daemon/plugin-bundle.zip"] = pluginBytes },
            mutateManifest: manifest =>
            {
                var targets = (JsonObject)manifest["targets"]!;
                targets["daemon"] = new JsonObject
                {
                    ["plugin"] = new JsonObject
                    {
                        ["path"] = "daemon/plugin-bundle.zip",
                        ["sha256"] = Sha256(pluginBytes)
                    }
                };
                manifest["extensionPoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "event",
                        ["id"] = "event.daemon",
                        ["target"] = "daemon"
                    }
                };
                var permissions = (JsonObject)manifest["permissions"]!;
                permissions["host"] = new JsonArray("daemon.event.subscribe");
                permissions["events"] = new JsonArray("plugin.community.example.status-panel.event.extension");
            });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void ValidatorRejectsPackageOwnedExtensionEventWithoutDaemonExtensionPoint()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var permissions = (JsonObject)manifest["permissions"]!;
            permissions["host"] = new JsonArray("daemon.event.subscribe");
            permissions["events"] = new JsonArray("plugin.community.example.status-panel.event.extension");
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "event_daemon_target_missing" or "event_extension_point_missing");
    }

    [Fact]
    public void ValidatorRejectsCrossPluginExtensionEventPermission()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var permissions = (JsonObject)manifest["permissions"]!;
            permissions["host"] = new JsonArray("daemon.event.subscribe");
            permissions["events"] = new JsonArray("plugin.community.example.other.event.extension");
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "event_permission_unknown");
    }

    [Fact]
    public void ValidatorRejectsSignedPackagesUntilVerifierExists()
    {
        var packageBytes = BuildMpxPackage(mutateManifest: manifest =>
        {
            var integrity = (JsonObject)manifest["integrity"]!;
            integrity["signed"] = true;
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "signature_verification_unavailable");
    }

    [Fact]
    public void ValidatorRejectsAuthoringJson5RuntimeSchema()
    {
        var packageBytes = BuildMpxPackage(ui: """
        {
          schema: "mcsl.ui.v1",
          root: { type: "Card" }
        }
        """);

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ui_schema_parse_failed");
    }

    [Fact]
    public void ValidatorRejectsInvalidUiSchemaInsideHashedPackage()
    {
        var packageBytes = BuildMpxPackage(ui: """
        {
          "schema": "mcsl.ui.v1",
          "root": { "type": "WebView" }
        }
        """);

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ui_component_unknown");
    }

    [Fact]
    public void ValidatorRejectsUnsignedPackagesWithSignatureSidecars()
    {
        var packageBytes = BuildMpxPackage(extraEntries: new Dictionary<string, byte[]>
        {
            ["signatures/publisher.sig"] = Encoding.UTF8.GetBytes("signature")
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "signature_unexpected");
    }

    [Fact]
    public void ValidatorRejectsUndeclaredPayloadFiles()
    {
        var packageBytes = BuildMpxPackage(extraEntries: new Dictionary<string, byte[]>
        {
            ["resources/hidden.bin"] = [1, 2, 3]
        });

        var result = PluginMpxPackageValidator.Validate(packageBytes);

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "file_undeclared");
    }

    private static byte[] BuildMpxPackage(
        string? ui = ValidUi,
        string? script = ValidScript,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null,
        Action<JsonObject>? mutateManifest = null)
    {
        byte[]? uiBytes = ui is null ? null : Encoding.UTF8.GetBytes(ui);
        byte[]? scriptBytes = script is null ? null : Encoding.UTF8.GetBytes(script);
        var manifest = BuildManifest(uiBytes is null ? null : Sha256(uiBytes), scriptBytes is null ? null : Sha256(scriptBytes));
        mutateManifest?.Invoke(manifest);

        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal)
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = false }))
        };

        if (uiBytes is not null)
            entries["client/ui.json"] = uiBytes;

        if (scriptBytes is not null)
            entries["bundle.js"] = scriptBytes;

        if (extraEntries is not null)
        {
            foreach (var entry in extraEntries)
                entries[entry.Key] = entry.Value;
        }

        return BuildZip(entries);
    }

    private static JsonObject BuildManifest(string? uiSha256, string? scriptSha256)
    {
        var targets = new JsonObject();
        if (uiSha256 is not null)
        {
            targets["client"] = new JsonObject
            {
                ["ui"] = new JsonObject
                {
                    ["path"] = "client/ui.json",
                    ["sha256"] = uiSha256
                }
            };
        }
        var runtime = new JsonObject
        {
            ["daemonApi"] = "[1.0.0,2.0.0)"
        };
        if (uiSha256 is not null)
            runtime["ui"] = "[1.0.0,2.0.0)";
        var entry = new JsonObject();

        if (scriptSha256 is not null)
        {
            runtime["javascript"] = "es2020";
            entry["script"] = new JsonObject
            {
                ["path"] = "bundle.js",
                ["sha256"] = scriptSha256,
                ["module"] = "esm"
            };
        }

        return new JsonObject
        {
            ["schema"] = "https://mcsl-team.github.io/schemas/mcsl-extension-1.0.schema.json",
            ["package"] = new JsonObject
            {
                ["id"] = "community.example.status-panel",
                ["version"] = "1.0.0",
                ["publisher"] = "community.example",
                ["displayName"] = "Status Panel"
            },
            ["runtime"] = runtime,
            ["targets"] = targets,
            ["entry"] = entry,
            ["permissions"] = new JsonObject
            {
                ["host"] = new JsonArray("ui.state", "daemon.instance.query"),
                ["events"] = new JsonArray(),
                ["network"] = new JsonArray(),
                ["storage"] = new JsonObject { ["privateBytes"] = 1024 }
            },
            ["resources"] = new JsonArray(),
            ["integrity"] = new JsonObject
            {
                ["algorithm"] = "sha256",
                ["signed"] = false
            }
        };
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

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }

    private static void RunOnSta(Func<Task> action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                var task = action();
                task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }

    private sealed class RecordingCommandDispatcher : IPluginExtensionCommandDispatcher
    {
        public List<(string PluginId, PluginUiCommandEvent Command, string? RequestId)> Calls { get; } = [];

        public Task<PluginExtensionProtocolParseResult> DispatchAsync(
            string pluginId,
            PluginUiCommandEvent command,
            string? requestId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((pluginId, command, requestId));
            return Task.FromResult(PluginExtensionProtocol.Parse($$"""
            {
              "protocol": "mcsl.extension.v1",
              "type": "response",
              "id": "{{requestId ?? "request"}}",
              "plugin": "{{pluginId}}",
              "result": { "accepted": true }
            }
            """));
        }
    }

    private sealed class RecordingPluginUiHost(List<PluginUiEvent> events) : IPluginUiHost
    {
        public void Emit(PluginUiEvent uiEvent) => events.Add(uiEvent);
    }

    private sealed class StatePatchingRuntime : IPluginScriptRuntime
    {
        public Task<PluginScriptExecutionResult> ExecuteAsync(
            PluginScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Host.Log(PluginUiHostLogLevel.Info, "preview loaded");
            request.Host.SetState(new Dictionary<string, object?> { ["title"] = "Patched" });
            return Task.FromResult(PluginScriptExecutionResult.Ok());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HangingRuntime : IPluginScriptRuntime
    {
        public Task<PluginScriptExecutionResult> ExecuteAsync(
            PluginScriptExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None).ContinueWith(
                _ => PluginScriptExecutionResult.Ok(),
                TaskScheduler.Default);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingRuntime : IPluginScriptRuntime
    {
        public Task<PluginScriptExecutionResult> ExecuteAsync(
            PluginScriptExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Declarative UI packages must not invoke the script runtime.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(IReadOnlyList<PluginMpxDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}"));
}
