using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Plugins;
using NuGet.Versioning;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginPhase7RuntimeTests
{
    [Fact]
    public async Task EventSubscribeDeliversTypedInstanceLogAndFiltersMetadata()
    {
        using var host = DomainEventPortTestHost.Create();
        var manifest = Manifest(
            "community.event-subscriber",
            [PluginFeature.EventSubscribe]);
        var errors = new PluginErrorFactory(manifest.Identity);
        var owner = host.Port.CreateOwner("plugin-event-subscription-test");
        var subscriber = new PluginEventSubscriber(manifest, host.Port, owner, errors);
        var target = Guid.NewGuid();
        var received = new List<string>();

        var result = subscriber.Subscribe(
            BuiltInProtocolDefinitions.InstanceLog,
            (meta, data, _) =>
            {
                received.Add($"{meta.Value.InstanceId}:{data.Value.Log}");
                return ValueTask.CompletedTask;
            },
            DaemonEventField<InstanceLogEventMeta>.FromValue(new InstanceLogEventMeta(target)));

        Assert.True(result.IsOk(out _));
        await host.Port.PublishAsync(new InstanceLogDomainEvent(Guid.NewGuid(), "ignored"));
        await host.Port.PublishAsync(new InstanceLogDomainEvent(target, "ready"));

        Assert.Equal([$"{target}:ready"], received);

        host.Port.DisposeOwner(owner);
        await host.Port.PublishAsync(new InstanceLogDomainEvent(target, "after-dispose"));
        Assert.Equal([$"{target}:ready"], received);
    }

    [Fact]
    public void ProviderImportsRequireDeclaredPluginDependencyAndContractAssembly()
    {
        var provider = Manifest(
            "community.provider",
            [],
            contracts: [ContractDependency(typeof(IPhase7GreetingContract).Assembly)]);
        var consumer = Manifest(
            "community.consumer",
            [],
            plugins: [new PluginManifestPluginDependency("community.provider", VersionRange.Parse("[1.0.0,2.0.0)"))],
            contracts: [ContractDependency(typeof(IPhase7GreetingContract).Assembly)]);
        var undeclaredConsumer = Manifest(
            "community.undeclared-consumer",
            [],
            contracts: [ContractDependency(typeof(IPhase7GreetingContract).Assembly)]);
        var registry = new PluginProviderRegistry();

        var export = registry.CreateExporter(provider, new PluginErrorFactory(provider.Identity))
            .Export<IPhase7GreetingContract>(new GreetingProvider());
        Assert.True(export.IsOk(out _));

        var imported = registry.CreateImports(consumer, new PluginErrorFactory(consumer.Identity))
            .Import<IPhase7GreetingContract>("community.provider");
        Assert.True(imported.IsOk(out var contract));
        Assert.Equal("hello phase7", contract!.Greet());

        var rejected = registry.CreateImports(undeclaredConsumer, new PluginErrorFactory(undeclaredConsumer.Identity))
            .Import<IPhase7GreetingContract>("community.provider");
        Assert.True(rejected.IsErr(out var error));
        Assert.Equal("plugin_dependency_required", error!.Code);
    }

    [Fact]
    public void ContractAssemblyResolverSharesDeclaredContractIdentity()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-contract-sharing-").FullName;
        try
        {
            var contractAssembly = typeof(IPhase7GreetingContract).Assembly;
            var contractFile = Path.GetFileName(contractAssembly.Location);
            var providerBundle = CreateBundle(root, "provider", contractAssembly.Location, contractFile);
            var consumerBundle = CreateBundle(root, "consumer", contractAssembly.Location, contractFile);
            var dependency = ContractDependency(contractAssembly);
            var provider = Manifest(
                "community.provider",
                [],
                bundleDirectory: providerBundle,
                entryAssemblyPath: Path.Combine(providerBundle, "PluginEntry.dll"),
                contracts: [dependency]);
            var consumer = Manifest(
                "community.consumer",
                [],
                bundleDirectory: consumerBundle,
                entryAssemblyPath: Path.Combine(consumerBundle, "PluginEntry.dll"),
                plugins: [new PluginManifestPluginDependency("community.provider", VersionRange.Parse("[1.0.0,2.0.0)"))],
                contracts: [dependency]);

            var admission = PluginContractAssemblyResolver.Create([provider, consumer]);

            Assert.Empty(admission.Failures);
            Assert.Equal(["community.provider", "community.consumer"], admission.Plugins.Select(static item => item.Identity.Id).ToArray());
            var requested = contractAssembly.GetName();
            Assert.True(admission.Resolver.TryResolve(provider, requested, out var providerAssembly));
            Assert.True(admission.Resolver.TryResolve(consumer, requested, out var consumerAssembly));
            Assert.Same(providerAssembly, consumerAssembly);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateBundle(string root, string name, string assemblyPath, string contractFile)
    {
        var bundle = Path.Combine(root, name);
        Directory.CreateDirectory(bundle);
        File.Copy(assemblyPath, Path.Combine(bundle, "PluginEntry.dll"));
        File.Copy(assemblyPath, Path.Combine(bundle, contractFile));
        return bundle;
    }

    private static PluginManifest Manifest(
        string id,
        PluginFeature[] features,
        string bundleDirectory = "/bundle",
        string entryAssemblyPath = "/bundle/PluginEntry.dll",
        ImmutableArray<PluginManifestPluginDependency> plugins = default,
        ImmutableArray<PluginManifestContractDependency> contracts = default)
    {
        var normalizedFeatures = features.Select(static item => item.Value).Order(StringComparer.Ordinal).ToImmutableArray();
        return new PluginManifest(
            new PluginIdentity(id, "1.0.0"),
            "PluginEntry.dll",
            "PluginEntry",
            NuGetVersion.Parse("1.0.0"),
            VersionRange.Parse("[1.0.0,2.0.0)"),
            features.ToFrozenSet(),
            plugins.IsDefault ? ImmutableArray<PluginManifestPluginDependency>.Empty : plugins,
            contracts.IsDefault ? ImmutableArray<PluginManifestContractDependency>.Empty : contracts,
            bundleDirectory,
            entryAssemblyPath,
            PluginManifestDigest.Compute(
                id,
                "1.0.0",
                "PluginEntry.dll",
                "PluginEntry",
                "[1.0.0, 2.0.0)",
                normalizedFeatures,
                plugins.IsDefault ? ImmutableArray<PluginManifestPluginDependency>.Empty : plugins,
                contracts.IsDefault ? ImmutableArray<PluginManifestContractDependency>.Empty : contracts));
    }

    private static PluginManifestContractDependency ContractDependency(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? throw new InvalidOperationException("The contract assembly has no name.");
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        using var stream = File.OpenRead(assembly.Location);
        return new PluginManifestContractDependency(
            Path.GetFileName(assembly.Location),
            name,
            VersionRange.Parse($"[{version},{version}]") ,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    public interface IPhase7GreetingContract
    {
        string Greet();
    }

    private sealed class GreetingProvider : IPhase7GreetingContract
    {
        public string Greet() => "hello phase7";
    }
}
