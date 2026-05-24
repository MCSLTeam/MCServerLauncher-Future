using System.IO.Compression;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Detection;
using MCServerLauncher.Daemon.Management.Factory;

namespace MCServerLauncher.ProtocolTests;

public class InstanceTypeExtensionsTests
{
    [Fact]
    public void SupportsMinecraftBoardWidgets_IncludesExtendedJavaRuntimeTypes()
    {
        Assert.True(InstanceType.MCBanner.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCYouer.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCThermos.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCCrucible.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCTaiyitist.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCCatServer.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCArclight.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCCanvas.SupportsMinecraftBoardWidgets());
    }

    [Fact]
    public void SupportsMinecraftBoardWidgets_ExcludesProxyPluginAndBedrockTypes()
    {
        Assert.True(InstanceType.MCSponge.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCBungeeCord.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCVelocity.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCWaterfall.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCTravertine.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCViaVersion.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCGeyser.SupportsMinecraftBoardWidgets());
        Assert.False(InstanceType.MCBDS.SupportsMinecraftBoardWidgets());
    }

    [Fact]
    public void RequiresNumericMinecraftVersion_TreatsMcJavaAsFallback()
    {
        Assert.False(InstanceType.MCJava.RequiresNumericMinecraftVersion());
        Assert.True(InstanceType.MCPaper.RequiresNumericMinecraftVersion());
        Assert.False(InstanceType.MCVelocity.RequiresNumericMinecraftVersion());
    }
}

public class InstanceVersionDetectorTests
{
    [Fact]
    public void Detect_ReconcilesMcJavaToPaperFromPatchProperties()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "paper-1.20.4-496.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["patch.properties"] = "version=1.20.4-496\n"
            });

            var config = new InstanceConfig
            {
                Name = "paper",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCPaper, reconciled.InstanceType);
            Assert.Equal("1.20.4", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToVelocityWithManifestVersion()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "velocity.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Manifest-Version: 1.0\nImplementation-Version: 3.4.0-SNAPSHOT\n"
            });

            var config = new InstanceConfig
            {
                Name = "velocity",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCVelocity, reconciled.InstanceType);
            Assert.Equal("3.4.0-SNAPSHOT", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesCraftBukkitFromMetaInfVersionEntry()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "craftbukkit.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/version/anything-1.20.4-R0.1-SNAPSHOT.jar"] = string.Empty
            });

            var config = new InstanceConfig
            {
                Name = "craftbukkit",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCCraftBukkit,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCCraftBukkit, reconciled.InstanceType);
            Assert.Equal("1.20.4", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_LeavesMcJavaUnchangedWhenNoRuleMatches()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "server.jar");
            CreateArchive(jarPath, new Dictionary<string, string>());

            var config = new InstanceConfig
            {
                Name = "generic",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCJava, reconciled.InstanceType);
            Assert.Equal(string.Empty, reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesFactorySettingToForgeBeforeFactorySelection()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "forge-1.20.1-47.2.0.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["version.json"] = "{\"id\":\"1.20.1\"}"
            });

            var setting = new InstanceFactorySetting
            {
                Name = "forge",
                Source = jarPath,
                SourceType = SourceType.Core,
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(setting);

            Assert.Equal(InstanceType.MCForge, reconciled.InstanceType);
            Assert.Equal("1.20.1", reconciled.Version);

            InstanceFactoryRegistry.InitializeDefaults();
            var factory = InstanceFactoryRegistry.GetInstanceFactory(reconciled);
            Assert.NotNull(factory);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
            InstanceFactoryRegistry.Reset();
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcsl-detector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateArchive(string path, IReadOnlyDictionary<string, string> entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
