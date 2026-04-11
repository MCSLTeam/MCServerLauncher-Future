using System.Reflection;
using MCServerLauncher.Daemon;

namespace MCServerLauncher.ProtocolTests;

public class TouchSocketHostingCompositionTests
{
    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonTouchSocketTransportProfile_FileLocksExpectedConfigChainAndInternalSurface()
    {
        var source = ReadSourceFile("MCServerLauncher.Daemon/Bootstrap/DaemonTouchSocketTransportProfile.cs");

        Assert.Contains("internal static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);

        var expectedOrder = new[]
        {
            "SetListenIPHosts(AppConfig.Get().Port)",
            "UseAspNetCoreContainer(collection)",
            "ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(a, collection, httpService, selectedRegistry))",
            "ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a))",
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
        var source = ReadSourceFile("MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("DaemonTouchSocketTransportProfile.CreateConfig", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TouchSocketConfig()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseAspNetCoreContainer(collection)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a))", source, StringComparison.Ordinal);
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
