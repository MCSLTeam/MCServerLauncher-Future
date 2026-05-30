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
        Assert.True(InstanceType.MCSpongeVanilla.SupportsMinecraftBoardWidgets());
        Assert.True(InstanceType.MCSpongeForge.SupportsMinecraftBoardWidgets());
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

    [Fact]
    public void Detect_ReconcilesMcJavaToFabricFromInstallProperties()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "fabric-1.20.4-0.19.2-1.1.1.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["install.properties"] = "fabric-loader-version=0.19.2\ngame-version=1.20.4\n",
                ["META-INF/MANIFEST.MF"] = "Implementation-Version: 1.1.1\n"
            });

            var config = new InstanceConfig
            {
                Name = "fabric",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCFabric, reconciled.InstanceType);
            Assert.Equal("1.20.4", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToLegacyForgeFromInstallProfile()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "forge-1.10.2-12.18.3.2511.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["install_profile.json"] = "{\"install\":{\"minecraft\":\"1.10.2\",\"filePath\":\"forge-1.10.2-12.18.3.2511-universal.jar\"}}"
            });

            var config = new InstanceConfig
            {
                Name = "forge-legacy",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCForge, reconciled.InstanceType);
            Assert.Equal("1.10.2", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToArclightFromInstallerMetadata()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "arclight-1.20.4-forge.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/installer.json"] = "{\"installer\":{\"minecraft\":\"1.20.4\",\"forge\":\"49.1.0\"}}",
                ["META-INF/MANIFEST.MF"] = "Implementation-Version: arclight-1.20.4-1.0.4-80ec5df\n"
            });

            var config = new InstanceConfig
            {
                Name = "arclight",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCArclight, reconciled.InstanceType);
            Assert.Equal("1.20.4", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToCatServerFromManifestBranch()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "catserver-1.18.2.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Git-Branch: 1.18.2\nMain-Class: foxlaunch.FoxServerLauncher\n"
            });

            var config = new InstanceConfig
            {
                Name = "catserver",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCCatServer, reconciled.InstanceType);
            Assert.Equal("1.18.2", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToSpongeVanillaFromManifestVersion()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "spongevanilla-1.20.1.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Implementation-Version: 1.20.1-11.0.0-RC1359\n"
            });

            var config = new InstanceConfig
            {
                Name = "spongevanilla",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCSpongeVanilla, reconciled.InstanceType);
            Assert.Equal("1.20.1-11.0.0-RC1359", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToSpongeForgeFromManifestVersion()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "spongeforge-1.12.2.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Implementation-Version: 1.12.2-2838-7.4.8\n"
            });

            var config = new InstanceConfig
            {
                Name = "spongeforge",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCSpongeForge, reconciled.InstanceType);
            Assert.Equal("1.12.2-2838-7.4.8", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_ReconcilesMcJavaToSpongeNeoFromManifestVersion()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "spongeneo-1.21.10.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Implementation-Version: 1.21.10-21.10.64-17.0.1-RC2598\n"
            });

            var config = new InstanceConfig
            {
                Name = "spongeneo",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCJava,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCSpongeNeo, reconciled.InstanceType);
            Assert.Equal("1.21.10", reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
        }
    }

    [Fact]
    public void Detect_LeavesSpecificFabricInstallerUnchangedWhenOnlyInstallerVersionExists()
    {
        var workingDirectory = CreateTempDirectory();
        try
        {
            var jarPath = Path.Combine(workingDirectory, "fabric-installer-1.1.1.jar");
            CreateArchive(jarPath, new Dictionary<string, string>
            {
                ["META-INF/MANIFEST.MF"] = "Implementation-Title: FabricInstaller\nImplementation-Version: 1.1.1\nMain-Class: net.fabricmc.installer.ServerLauncher\n"
            });

            var config = new InstanceConfig
            {
                Name = "fabric-installer",
                Target = Path.GetFileName(jarPath),
                TargetType = TargetType.Jar,
                InstanceType = InstanceType.MCFabric,
                Version = string.Empty,
                JavaPath = "java"
            };

            var reconciled = InstanceVersionDetector.Reconcile(config, workingDirectory);

            Assert.Equal(InstanceType.MCFabric, reconciled.InstanceType);
            Assert.Equal(string.Empty, reconciled.Version);
        }
        finally
        {
            Directory.Delete(workingDirectory, true);
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
