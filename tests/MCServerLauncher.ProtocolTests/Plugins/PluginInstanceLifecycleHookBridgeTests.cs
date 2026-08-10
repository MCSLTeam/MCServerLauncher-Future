using System.Collections.Frozen;
using System.Collections.Immutable;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Plugins;
using NuGet.Versioning;
using RustyOptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class PluginInstanceLifecycleHookBridgeTests
{
    [Fact]
    public async Task PublishStatusChangeInvokesRegisteredLifecycleHook()
    {
        using var host = DomainEventPortTestHost.Create();
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreateManifest();
        var hook = new RecordingLifecycleHook();
        var registered = registry
            .CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity))
            .Register<IPluginInstanceLifecycleHook>("hook.instance.lifecycle", hook);
        Assert.True(registered.IsOk(out _));
        using var bridge = new PluginInstanceLifecycleHookBridge(
            registry,
            host.Port,
            NullLogger<PluginInstanceLifecycleHookBridge>.Instance);
        var instanceId = Guid.NewGuid();

        await host.Port.PublishAsync(new InstanceStatusChangedDomainEvent(instanceId, InstanceStatus.Running));

        var lifecycleEvent = Assert.Single(hook.Events);
        Assert.Equal(instanceId.ToString("D"), lifecycleEvent.InstanceId);
        Assert.Equal(nameof(InstanceStatus.Running), lifecycleEvent.Stage);
    }

    private static PluginManifest CreateManifest() =>
        new(
            new PluginIdentity("community.example.lifecycle", "1.0.0"),
            "Lifecycle.dll",
            "Community.Example.Lifecycle.Plugin",
            NuGetVersion.Parse("1.0.0"),
            VersionRange.Parse("[1.0.0,2.0.0)"),
            Array.Empty<PluginFeature>().ToFrozenSet(),
            ImmutableArray<PluginManifestPluginDependency>.Empty,
            ImmutableArray<PluginManifestContractDependency>.Empty,
            [new PluginManifestExtensionPoint(
                "hook.instance.lifecycle",
                PluginExtensionPointKind.Hook,
                0,
                null,
                ImmutableArray<string>.Empty,
                null)],
            ImmutableArray<PluginManifestCommand>.Empty,
            "/tmp/community.example.lifecycle",
            "/tmp/community.example.lifecycle/Lifecycle.dll",
            "digest");

    private sealed class RecordingLifecycleHook : IPluginInstanceLifecycleHook
    {
        public List<PluginInstanceLifecycleEvent> Events { get; } = [];

        public ValueTask<Result<Unit, DaemonError>> OnLifecycleAsync(
            PluginInstanceLifecycleEvent lifecycleEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(lifecycleEvent);
            return ValueTask.FromResult(PluginResult.Ok());
        }
    }
}
