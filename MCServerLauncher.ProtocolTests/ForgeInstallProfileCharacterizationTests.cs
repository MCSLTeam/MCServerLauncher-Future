using System.Text.Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V1Json;
using MCServerLauncher.Daemon.Management.Installer.MinecraftForge.V2Json;
using MCServerLauncher.ProtocolTests.Fixtures.ForgeInstallProfile;
using MCServerLauncher.ProtocolTests.Helpers;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using Version = MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json.Version;

namespace MCServerLauncher.ProtocolTests;

public class ForgeInstallProfileCharacterizationTests
{
    private static readonly JsonSerializerOptions Options = InstallProfileJsonSettings.Settings;

    // ──────────────────────────── Artifact Parsing Tests ────────────────────────────

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void Artifact_SimpleDescriptor()
    {
        var descriptor = "net.minecraftforge:forge:1.12.2-14.23.5.2860";
        var artifact = Artifact.FromDescriptor(descriptor);

        Assert.Equal("net.minecraftforge", artifact.Domain);
        Assert.Equal("forge", artifact.Name);
        Assert.Equal("1.12.2-14.23.5.2860", artifact.Version);
        Assert.Equal("jar", artifact.Extension);
        Assert.Null(artifact.Classifier);
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void Artifact_WithExtension()
    {
        var descriptor = "net.minecraftforge:forge:1.12.2-14.23.5.2860:universal@zip";
        var artifact = Artifact.FromDescriptor(descriptor);

        Assert.Equal("net.minecraftforge", artifact.Domain);
        Assert.Equal("forge", artifact.Name);
        Assert.Equal("1.12.2-14.23.5.2860", artifact.Version);
        Assert.Equal("universal", artifact.Classifier);
        Assert.Equal("zip", artifact.Extension);
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void Artifact_Roundtrip()
    {
        var simple = "net.minecraftforge:forge:1.12.2-14.23.5.2860";
        var artifact = Artifact.FromDescriptor(simple);
        Assert.Equal(simple, artifact.Descriptor);

        var withExt = "de.oceanlabs.mcp:mcp_config:1.12.2-20200226.224830@zip";
        var artifactExt = Artifact.FromDescriptor(withExt);
        Assert.Equal(withExt, artifactExt.Descriptor);
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void Artifact_Path_ReturnsCorrectRelativePath()
    {
        var descriptor = "net.minecraftforge:forge:1.12.2-14.23.5.2860";
        var artifact = Artifact.FromDescriptor(descriptor);

        var expected = $"net{Path.DirectorySeparatorChar}minecraftforge{Path.DirectorySeparatorChar}forge{Path.DirectorySeparatorChar}1.12.2-14.23.5.2860{Path.DirectorySeparatorChar}forge-1.12.2-14.23.5.2860.jar";
        Assert.Equal(expected, artifact.Path);
    }

    // ──────────────────────────── V1 Install Profile Tests ────────────────────────────

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V1_Profile_Forge152()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge152Dir, "install_profile.json"));

        var profile = StjJsonSerializer.Deserialize<ForgeInstallerV1.ProfileFile>(json, Options);
        Assert.NotNull(profile);

        // Install assertions
        Assert.NotNull(profile.Install.Path);
        Assert.StartsWith("net.minecraftforge", profile.Install.Path.Descriptor);
        Assert.Equal("1.5.2", profile.Install.Minecraft);
        Assert.NotNull(profile.Install.FilePath);

        // VersionInfo assertions
        Assert.NotEmpty(profile.VersionInfo.Libraries);
        foreach (var lib in profile.VersionInfo.Libraries)
        {
            Assert.NotNull(lib.Name);
        }
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V1_Profile_Forge1710()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge1710Dir, "install_profile.json"));

        var profile = StjJsonSerializer.Deserialize<ForgeInstallerV1.ProfileFile>(json, Options);
        Assert.NotNull(profile);

        // Install assertions
        Assert.NotNull(profile.Install.Path);
        Assert.StartsWith("net.minecraftforge", profile.Install.Path.Descriptor);
        Assert.Equal("1.7.10", profile.Install.Minecraft);
        Assert.NotNull(profile.Install.FilePath);

        // VersionInfo assertions
        Assert.NotEmpty(profile.VersionInfo.Libraries);
        foreach (var lib in profile.VersionInfo.Libraries)
        {
            Assert.NotNull(lib.Name);
        }
    }

    // ──────────────────────────── V2 Install Profile Tests ────────────────────────────

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V2_Profile_Forge1122()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge1122Dir, "install_profile.json"));

        var install = StjJsonSerializer.Deserialize<InstallV1>(json, Options);
        Assert.NotNull(install);

        Assert.Equal(0, install.Spec);
        Assert.NotNull(install.Path);
        Assert.Equal("1.12.2", install.Minecraft);
        Assert.Equal("/version.json", install.Json);
        Assert.NotEmpty(install.Libraries);

        foreach (var lib in install.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V2_Profile_Forge1165()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge1165Dir, "install_profile.json"));

        var install = StjJsonSerializer.Deserialize<InstallV1>(json, Options);
        Assert.NotNull(install);

        Assert.Equal(0, install.Spec);
        Assert.Equal("1.16.5", install.Minecraft);
        Assert.NotNull(install.Path);
        Assert.NotEmpty(install.Libraries);

        foreach (var lib in install.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V2_Profile_Forge1214()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge1214Dir, "install_profile.json"));

        var install = StjJsonSerializer.Deserialize<InstallV1>(json, Options);
        Assert.NotNull(install);

        Assert.Equal(1, install.Spec);
        Assert.Equal("1.21.4", install.Minecraft);
        Assert.NotNull(install.Path);
        Assert.NotEmpty(install.Libraries);

        foreach (var lib in install.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V2_Profile_NeoForge211227()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.NeoForge211227Dir, "install_profile.json"));

        var install = StjJsonSerializer.Deserialize<InstallV1>(json, Options);
        Assert.NotNull(install);

        Assert.Equal(1, install.Spec);
        Assert.Equal("1.21.1", install.Minecraft);
        // NeoForge install profiles do not have a "path" field
        Assert.Null(install.Path);
        Assert.NotEmpty(install.Libraries);

        foreach (var lib in install.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void V2_Profile_Cleanroom()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Cleanroom058Dir, "install_profile.json"));

        var install = StjJsonSerializer.Deserialize<InstallV1>(json, Options);
        Assert.NotNull(install);

        Assert.Equal(0, install.Spec);
        Assert.Equal("1.12.2", install.Minecraft);
        Assert.NotNull(install.Path);
        Assert.Equal("com.cleanroommc:cleanroom:0.5.8-alpha", install.Path.Descriptor);
        Assert.NotEmpty(install.Libraries);

        foreach (var lib in install.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }

    // ──────────────────────────── Version Manifest Tests ────────────────────────────

    [Fact]
    [Trait("Category", "ForgeInstallProfile")]
    public void Version_Forge1122()
    {
        var json = File.ReadAllText(Path.Combine(ForgeInstallProfileFixturePaths.Forge1122Dir, "version.json"));

        var version = StjJsonSerializer.Deserialize<Version>(json, Options);
        Assert.NotNull(version);

        Assert.Equal("1.12.2-forge-14.23.5.2861", version.Id);
        Assert.NotEmpty(version.Libraries);
        // version.json has no top-level "downloads" field; DownloadDictionary is expected to be null
        Assert.Null(version.DownloadDictionary);

        // Verify library downloads are parsed correctly
        foreach (var lib in version.Libraries)
        {
            Assert.NotNull(lib.Name);
            Assert.NotNull(lib.Downloads);
            Assert.NotNull(lib.Downloads.Artifact);
        }
    }
}
