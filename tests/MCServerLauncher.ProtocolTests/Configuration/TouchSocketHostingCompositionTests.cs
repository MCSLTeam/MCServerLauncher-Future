using System.Reflection;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.ProtocolTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.ProtocolTests;

[Collection(DaemonInstanceStorageIsolationCollection.Name)]
public class TouchSocketHostingCompositionTests
{
    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonTouchSocketTransportProfile_FileLocksExpectedConfigChainAndInternalSurface()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonTouchSocketTransportProfile.cs");

        Assert.Contains("internal static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);

        var expectedOrder = new[]
        {
            "SetListenIPHosts(AppConfig.Get().Port)",
            "UseAspNetCoreContainer(collection)",
            "ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(",
            "ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a, legacyEventQueueControl))",
        };

        var positions = expectedOrder
            .Select(marker => source.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        for (var i = 0; i < expectedOrder.Length; i++)
        {
            Assert.True(positions[i] >= 0, $"Expected marker '{expectedOrder[i]}' not found in transport profile source.");
        }

        for (var i = 1; i < expectedOrder.Length; i++)
        {
            Assert.True(positions[i] > positions[i - 1], $"Marker '{expectedOrder[i]}' must appear after '{expectedOrder[i - 1]}' in transport profile source.");
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_SetupAsync_UsesTransportProfileHelperWithoutInlineTouchSocketConfigChain()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("DaemonTouchSocketTransportProfile.CreateConfig", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TouchSocketConfig()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseAspNetCoreContainer(collection)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a))", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_ServeAsync_LogsBindEndpointSeparatelyFromConnectableUrls()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("\"[Remote] Bind endpoint: {BindEndpoint}\"", source, StringComparison.Ordinal);
        Assert.Contains("\"[Remote] Ws Server connect URLs: {ConnectUrls}\"", source, StringComparison.Ordinal);
        Assert.Contains("GetConnectableAuthorities", source, StringComparison.Ordinal);
        Assert.Contains("IPAddress.Loopback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ws://{RemoteAddress}/api/v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://{RemoteAddress}/", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_Shutdown_DelegatesToTheInternalApplicationLifecyclePort()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("GetRequiredService<IDaemonRuntimeLifecycle>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IInstanceManager>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<FileSessionCoordinator>()", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonAssembly_DoesNotExportTransportProfileHelper()
    {
        var exportedTypeNames = typeof(Application).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(exportedTypeNames, name => name.Contains("DaemonTouchSocketTransportProfile", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedTypeNames, name => name.Contains("TransportProfile", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task DaemonServiceComposition_ResolvesOneSharedApplicationGraph()
    {
        Directory.CreateDirectory(FileManager.InstancesRoot);
        var services = new ServiceCollection();
        services.AddLogging();
        var httpService = new HttpService();
        var selectedRegistry = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry: true);
        var config = new TouchSocketConfig()
            .UseAspNetCoreContainer(services)
            .ConfigureContainer(registrator => DaemonServiceComposition.ConfigureContainer(
                registrator,
                services,
                httpService,
                selectedRegistry));

        await httpService.SetupAsync(config);

        var resolver = httpService.Resolver;
        var application = resolver.GetRequiredService<IDaemonApplication>();
        Assert.Same(resolver.GetRequiredService<IInstanceApplication>(), application.Instances);
        Assert.Same(resolver.GetRequiredService<IFileApplication>(), application.Files);
        Assert.Same(resolver.GetRequiredService<ISystemApplication>(), application.System);
        Assert.Same(resolver.GetRequiredService<IEventRuleApplication>(), application.EventRules);
        Assert.Same(FileSessionCoordinator.Shared, resolver.GetRequiredService<FileSessionCoordinator>());
        Assert.NotNull(resolver.GetRequiredService<IDaemonRuntimeLifecycle>());

        var catalogAccessor = resolver.GetRequiredService<IFrozenProtocolCatalogAccessor>();
        Assert.False(catalogAccessor.TryGet(out _));

        DaemonServiceComposition.AttachDaemonLifecycle(httpService);

        Assert.True(catalogAccessor.TryGet(out var attachedCatalog));
        var catalogComposition = resolver.GetRequiredService<BuiltInProtocolCatalogComposition>();
        Assert.Same(catalogComposition, resolver.GetRequiredService<BuiltInProtocolCatalogComposition>());
        Assert.Same(catalogComposition.Catalog, attachedCatalog);
        Assert.Same(catalogComposition.Catalog, catalogAccessor.GetRequired());
        Assert.Same(
            resolver.GetRequiredService<FrozenProtocolCatalogAccessor>(),
            catalogAccessor);
        Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, catalogComposition.Catalog.Rpcs.Count);
        Assert.Equal(BuiltInProtocolDefinitions.Events.Length, catalogComposition.Catalog.Events.Count);

        var manager = resolver.GetRequiredService<InstanceManager>();
        Assert.Same(manager.InstanceSnapshotSource, resolver.GetRequiredService<IInstanceSnapshotSource>());

        var trigger = resolver.GetRequiredService<EventTriggerService>();
        var legacyAdapter = resolver.GetRequiredService<LegacyDomainEventAdapter>();
        var eventBridge = resolver.GetRequiredService<InstanceDomainEventBridge>();
        trigger.Stop();
        legacyAdapter.Stop();
        eventBridge.Dispose();
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonServiceComposition_ConfiguresTheSharedFileCoordinatorBeforeStartup()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs");
        var acquire = source.IndexOf("var fileSessionCoordinator = FileSessionCoordinator.Shared", StringComparison.Ordinal);
        var configure = source.IndexOf(
            "fileSessionCoordinator.ConfigureDownloadSessionLimit(AppConfig.Get().FileDownloadSessions)",
            StringComparison.Ordinal);
        var register = source.IndexOf("a.RegisterSingleton(fileSessionCoordinator)", StringComparison.Ordinal);
        var start = source.IndexOf("GetRequiredService<FileSessionCoordinator>().Start()", StringComparison.Ordinal);

        Assert.True(acquire >= 0);
        Assert.True(configure > acquire);
        Assert.True(register > configure);
        Assert.True(start > register);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_AttachesTheFrozenCatalogAfterSetupAndBeforeStartingTheListener()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");
        var setupMethod = source.IndexOf("public static async Task SetupAsync()", StringComparison.Ordinal);
        var setup = source.IndexOf("await HttpService.SetupAsync(", setupMethod, StringComparison.Ordinal);
        var attach = source.IndexOf("DaemonServiceComposition.AttachDaemonLifecycle(HttpService)", setupMethod, StringComparison.Ordinal);
        var serveMethod = source.IndexOf("public static async Task ServeAsync()", StringComparison.Ordinal);
        var start = source.IndexOf("await HttpService.StartAsync()", serveMethod, StringComparison.Ordinal);

        Assert.True(setupMethod >= 0);
        Assert.True(setup > setupMethod);
        Assert.True(attach > setup);
        Assert.True(serveMethod > attach);
        Assert.True(start > serveMethod);
        Assert.Equal(attach, source.LastIndexOf("DaemonServiceComposition.AttachDaemonLifecycle(HttpService)", StringComparison.Ordinal));
    }

    private static string ReadSourceFile(string relativePath)
    {
        var repoRoot = ResolveRepoRoot();
        var fullPath = Path.Combine(repoRoot, relativePath);

        Assert.True(File.Exists(fullPath), $"Source file not found: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MCServerLauncher.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
