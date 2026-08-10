using System.Collections.Frozen;
using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Plugins;
using NuGet.Versioning;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginExtensionPointRuntimeRegistryTests
{
    [Fact]
    public void RegisterRequiresDeclaredExtensionPoint()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest([]);
        var registrar = registry.CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity));

        var result = registrar.Register<IPluginJavaRuntimeProvider>("provider.java", new JavaProvider());

        Assert.True(result.IsErr(out var error));
        Assert.Equal("extension_point_declaration_required", error!.Code);
    }

    [Fact]
    public void RegisterRequiresMatchingRuntimeContract()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "provider.java",
                PluginExtensionPointKind.Provider,
                0,
                null,
                ImmutableArray<string>.Empty,
                null)
        ]);
        var registrar = registry.CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity));

        var result = registrar.Register<IPluginServerCoreProvider>("provider.java", new ServerCoreProvider());

        Assert.True(result.IsErr(out var error));
        Assert.Equal("extension_point_contract_mismatch", error!.Code);
    }

    [Fact]
    public void RegisterStoresDeclaredRuntimeContribution()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "provider.java",
                PluginExtensionPointKind.Provider,
                10,
                null,
                ImmutableArray<string>.Empty,
                "minecraft")
        ]);
        var registrar = registry.CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity));

        var result = registrar.Register<IPluginJavaRuntimeProvider>("provider.java", new JavaProvider());
        var registered = registry.List("provider.java");

        Assert.True(result.IsOk(out _));
        var contribution = Assert.Single(registered);
        Assert.Equal("community.example.runtime", contribution.Owner.Id);
        Assert.Equal(10, contribution.Priority);
        Assert.Equal("minecraft", contribution.DefaultFor);
        Assert.Equal(typeof(IPluginJavaRuntimeProvider), contribution.ContractType);
    }

    [Fact]
    public async Task ListRegistryContributionsSortsAndDeduplicatesByPriority()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var highManifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "registry.command",
                PluginExtensionPointKind.Registry,
                20,
                null,
                ImmutableArray<string>.Empty,
                null)
        ], "community.example.high");
        var lowManifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "registry.command",
                PluginExtensionPointKind.Registry,
                5,
                null,
                ImmutableArray<string>.Empty,
                null)
        ], "community.example.low");
        Assert.True(registry
            .CreateRegistrar(lowManifest, new PluginErrorFactory(lowManifest.Identity))
            .Register<IPluginCommandRegistry>("registry.command", new CommandRegistry([
                new PluginCommandContribution("launch", "Low launch", null, "low.launch"),
                new PluginCommandContribution("diagnose", "Diagnose", null, "low.diagnose")
            ]))
            .IsOk(out _));
        Assert.True(registry
            .CreateRegistrar(highManifest, new PluginErrorFactory(highManifest.Identity))
            .Register<IPluginCommandRegistry>("registry.command", new CommandRegistry([
                new PluginCommandContribution("launch", "High launch", null, "high.launch")
            ]))
            .IsOk(out _));

        var result = await registry.ListCommandsAsync(CancellationToken.None);

        Assert.True(result.IsOk(out var commands));
        Assert.Equal(["launch", "diagnose"], commands.Select(static item => item.Value.Id).ToArray());
        Assert.Equal("community.example.high", commands[0].Owner.Id);
        Assert.Equal("high.launch", commands[0].Value.Target);
        Assert.Equal("community.example.low", commands[1].Owner.Id);
    }

    [Fact]
    public async Task ListRegistryContributionsRejectsInvalidContributions()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "registry.template",
                PluginExtensionPointKind.Registry,
                0,
                null,
                ImmutableArray<string>.Empty,
                null)
        ]);
        Assert.True(registry
            .CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity))
            .Register<IPluginInstanceTemplateRegistry>("registry.template", new TemplateRegistry([
                new PluginInstanceTemplateContribution("template", "", "Universal")
            ]))
            .IsOk(out _));

        var result = await registry.ListInstanceTemplatesAsync(CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("extension_point_registry_invalid", error!.Code);
    }

    [Fact]
    public async Task CreateProcessStartInfoUsesHandledLauncherOverride()
    {
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest([
            new PluginManifestExtensionPoint(
                "override.process.launcher",
                PluginExtensionPointKind.Override,
                0,
                null,
                ImmutableArray<string>.Empty,
                null)
        ]);
        var launcherOverride = new ProcessLauncherOverride();
        Assert.True(registry
            .CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity))
            .Register<IPluginProcessLauncherOverride>("override.process.launcher", launcherOverride)
            .IsOk(out _));
        var config = CreateInstanceConfig();

        var result = await registry.CreateProcessStartInfoAsync(config, CancellationToken.None);

        Assert.True(result.IsOk(out var startInfo));
        Assert.Equal("custom-launcher", startInfo.FileName);
        Assert.Equal(["--instance", config.Uuid.ToString("D")], startInfo.ArgumentList.ToArray());
        Assert.Equal(config.GetWorkingDirectory(), startInfo.WorkingDirectory);
        Assert.Equal("1", startInfo.Environment["MCSL_OVERRIDE"]);
        Assert.NotNull(launcherOverride.LastRequest);
        Assert.Equal(config.Uuid.ToString("D"), launcherOverride.LastRequest.InstanceId);
        Assert.Contains("-jar", launcherOverride.LastRequest.Command[^1], StringComparison.Ordinal);
    }

    private static PluginManifest CreateManifest(
        ImmutableArray<PluginManifestExtensionPoint> extensionPoints,
        string pluginId = "community.example.runtime") =>
        new(
            new PluginIdentity(pluginId, "1.0.0"),
            "Runtime.dll",
            "Community.Example.Runtime.Plugin",
            NuGetVersion.Parse("1.0.0"),
            VersionRange.Parse("[1.0.0,2.0.0)"),
            Array.Empty<PluginFeature>().ToFrozenSet(),
            ImmutableArray<PluginManifestPluginDependency>.Empty,
            ImmutableArray<PluginManifestContractDependency>.Empty,
            extensionPoints,
            ImmutableArray<PluginManifestCommand>.Empty,
            "/tmp/community.example.runtime",
            "/tmp/community.example.runtime/Runtime.dll",
            "digest");

    private static InstanceConfig CreateInstanceConfig() =>
        new()
        {
            Uuid = Guid.NewGuid(),
            Name = "launcher-test",
            Target = "server.jar",
            TargetType = TargetType.Jar,
            InstanceType = InstanceType.MCJava,
            Version = "1.20.1",
            JavaPath = "java",
            Arguments = ["nogui"]
        };

    private sealed class ProcessLauncherOverride : IPluginProcessLauncherOverride
    {
        public PluginProcessLaunchRequest? LastRequest { get; private set; }

        public ValueTask<Result<PluginProcessLauncherOverrideResult, DaemonError>> TryCreatePlanAsync(
            PluginProcessLaunchRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(PluginResult.Ok(new PluginProcessLauncherOverrideResult(
                true,
                new PluginProcessLaunchPlan(
                    "custom-launcher",
                    ["--instance", request.InstanceId],
                    request.WorkingDirectory,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["MCSL_OVERRIDE"] = "1"
                    }))));
        }
    }

    private sealed class CommandRegistry(IReadOnlyList<PluginCommandContribution> commands) : IPluginCommandRegistry
    {
        public ValueTask<Result<IReadOnlyList<PluginCommandContribution>, DaemonError>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PluginResult.Ok(commands));
    }

    private sealed class TemplateRegistry(IReadOnlyList<PluginInstanceTemplateContribution> templates) : IPluginInstanceTemplateRegistry
    {
        public ValueTask<Result<IReadOnlyList<PluginInstanceTemplateContribution>, DaemonError>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PluginResult.Ok(templates));
    }

    private sealed class JavaProvider : IPluginJavaRuntimeProvider
    {
        public ValueTask<Result<IReadOnlyList<PluginJavaRuntimeResolution>, DaemonError>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PluginResult.Ok<IReadOnlyList<PluginJavaRuntimeResolution>>([
                new PluginJavaRuntimeResolution("Test Java", "/java", "/java/bin/java", "21.0.7", "x64")
            ]));
    }

    private sealed class ServerCoreProvider : IPluginServerCoreProvider
    {
        public ValueTask<Result<PluginServerCoreResolution, DaemonError>> ResolveAsync(
            PluginServerCoreRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PluginResult.Ok(new PluginServerCoreResolution("core", "Core", null, null)));
    }
}
