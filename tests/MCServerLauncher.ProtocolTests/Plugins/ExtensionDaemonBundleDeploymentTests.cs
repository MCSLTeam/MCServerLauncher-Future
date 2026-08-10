using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Plugins.Configuration;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.ExternalCompileFixture;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests.Plugins;

public sealed class ExtensionDaemonBundleDeploymentTests
{
    [Fact]
    public async Task InstallAndRemoveDaemonBundleUsesUploadStagingAndRequiresRestart()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-extension-deploy-").FullName;
        var stagedDirectory = Path.Combine(FileManager.UploadRoot, "extension-tests");
        Directory.CreateDirectory(stagedDirectory);
        var stagedBundle = Path.Combine(stagedDirectory, $"external-compile-{Guid.NewGuid():N}.zip");
        try
        {
            CreateExternalCompileBundle(stagedBundle, "external-compile");
            var configPath = Path.Combine(root, "config.json");
            var config = new AppConfig(11452, "secret", "main-token", plugins: new DaemonPluginsConfig());
            var app = new LocalExtensionDeploymentApplication(
                config,
                Path.Combine(root, "plugins"),
                configPath);
            var request = new ExtensionDaemonBundleInstallRequest(
                "external-compile",
                Path.GetRelativePath(FileManager.Root, stagedBundle),
                await Sha256Async(stagedBundle));

            var beforeInstall = await app.GetDaemonBundleStatusAsync(
                new ExtensionDaemonBundleStatusRequest("external-compile"),
                CancellationToken.None);
            Assert.True(beforeInstall.IsOk(out var beforeInstallResult), ErrorMessage(beforeInstall));
            Assert.Equal("not_installed", beforeInstallResult!.Status);
            Assert.False(beforeInstallResult.RestartRequired);

            var installed = await app.InstallDaemonBundleAsync(request, CancellationToken.None);

            Assert.True(installed.IsOk(out var installResult), ErrorMessage(installed));
            Assert.Equal("installed_pending_restart", installResult!.Status);
            Assert.True(installResult.RestartRequired);
            Assert.True(File.Exists(Path.Combine(root, "plugins", "external-compile", "mcsl-plugin.json")));
            Assert.True(config.Plugins.Entries.TryGetValue("external-compile", out var enabled));
            Assert.True(enabled!.Enabled);

            var afterInstall = await app.GetDaemonBundleStatusAsync(
                new ExtensionDaemonBundleStatusRequest("external-compile"),
                CancellationToken.None);
            Assert.True(afterInstall.IsOk(out var afterInstallResult), ErrorMessage(afterInstall));
            Assert.Equal("installed_enabled", afterInstallResult!.Status);
            Assert.False(afterInstallResult.RestartRequired);

            var removed = await app.RemoveDaemonBundleAsync(
                new ExtensionDaemonBundleRemoveRequest("external-compile"),
                CancellationToken.None);

            Assert.True(removed.IsOk(out var removeResult), ErrorMessage(removed));
            Assert.Equal("removed_pending_restart", removeResult!.Status);
            Assert.True(removeResult.RestartRequired);
            Assert.False(Directory.Exists(Path.Combine(root, "plugins", "external-compile")));
            Assert.True(config.Plugins.Entries.TryGetValue("external-compile", out var disabled));
            Assert.False(disabled!.Enabled);

            var afterRemove = await app.GetDaemonBundleStatusAsync(
                new ExtensionDaemonBundleStatusRequest("external-compile"),
                CancellationToken.None);
            Assert.True(afterRemove.IsOk(out var afterRemoveResult), ErrorMessage(afterRemove));
            Assert.Equal("disabled_missing", afterRemoveResult!.Status);
            Assert.False(afterRemoveResult.RestartRequired);
        }
        finally
        {
            if (File.Exists(stagedBundle))
                File.Delete(stagedBundle);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallDaemonBundleRejectsSourcesOutsideUploadStaging()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-extension-deploy-").FullName;
        var contained = Path.Combine(FileManager.Root, "contained", $"external-compile-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(contained)!);
        try
        {
            CreateExternalCompileBundle(contained, "external-compile");
            var app = new LocalExtensionDeploymentApplication(
                new AppConfig(11452, "secret", "main-token", plugins: new DaemonPluginsConfig()),
                Path.Combine(root, "plugins"),
                Path.Combine(root, "config.json"));

            var result = await app.InstallDaemonBundleAsync(
                new ExtensionDaemonBundleInstallRequest(
                    "external-compile",
                    Path.GetRelativePath(FileManager.Root, contained),
                    await Sha256Async(contained)),
                CancellationToken.None);

            Assert.True(result.IsErr(out var error));
            Assert.Equal("extension.bundle.source_outside_uploads", error!.Code);
            Assert.False(Directory.Exists(Path.Combine(root, "plugins", "external-compile")));
        }
        finally
        {
            if (File.Exists(contained))
                File.Delete(contained);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateExternalCompileBundle(string path, string pluginId)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var manifest = $$"""
        {
          "package": { "id": "{{pluginId}}", "version": "1.0.0" },
          "entry": {
            "assembly": "PluginEntry.dll",
            "type": "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin"
          },
          "requires": {
            "api": "[1.0.0,2.0.0)",
            "features": ["event.publish", "instance.query", "rpc.register"]
          }
        }
        """;
        WriteEntry(archive, "mcsl-plugin.json", Encoding.UTF8.GetBytes(manifest));
        WriteEntry(archive, "PluginEntry.dll", File.ReadAllBytes(typeof(ExternalCompilePlugin).Assembly.Location));
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(bytes);
    }

    private static string ErrorMessage(Result<ExtensionDaemonBundleDeploymentResult, DaemonError> result) =>
        result.IsErr(out var error) ? error!.Message : string.Empty;

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}
