using System.Collections.Frozen;
using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
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

    private static PluginManifest CreateManifest(ImmutableArray<PluginManifestExtensionPoint> extensionPoints) =>
        new(
            new PluginIdentity("community.example.runtime", "1.0.0"),
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
