using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.Modules;

public sealed record ComponentScanResult(
    bool HasMods,
    bool HasPlugins,
    IReadOnlyList<ComponentItemModel> Mods,
    IReadOnlyList<ComponentItemModel> Plugins)
{
    public bool SupportsComponents => HasMods || HasPlugins;
}

public static class ComponentScanner
{
    public static async Task<ComponentScanResult> ScanAsync(IDaemon daemon, Guid instanceId)
    {
        var instanceRoot = $"/instances/{instanceId}";
        var hasMods = await DirectoryExistsAsync(daemon, $"{instanceRoot}/mods");
        var hasPlugins = await DirectoryExistsAsync(daemon, $"{instanceRoot}/plugins");

        var mods = hasMods
            ? await LoadComponentsAsync(daemon, $"{instanceRoot}/mods", ComponentKind.Mod)
            : [];
        var plugins = hasPlugins
            ? await LoadComponentsAsync(daemon, $"{instanceRoot}/plugins", ComponentKind.Plugin)
            : [];

        return new ComponentScanResult(hasMods, hasPlugins, mods, plugins);
    }

    public static async Task DisableAsync(IDaemon daemon, ComponentItemModel item)
    {
        if (!item.IsEnabled) return;
        await RenameAsync(daemon, item, item.FileName + ".disabled");
        item.IsEnabled = false;
    }

    public static async Task EnableAsync(IDaemon daemon, ComponentItemModel item)
    {
        if (item.IsEnabled) return;
        var newName = item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? item.FileName[..^".disabled".Length]
            : item.FileName;
        await RenameAsync(daemon, item, newName);
        item.IsEnabled = true;
    }

    public static async Task RenameAsync(IDaemon daemon, ComponentItemModel item, string newName)
    {
        await daemon.RenameFileAsync(item.VirtualPath, newName);
        var folderPath = item.VirtualPath[..item.VirtualPath.LastIndexOf('/')];
        item.FileName = newName;
        item.VirtualPath = $"{folderPath}/{newName}";
    }

    private static async Task<bool> DirectoryExistsAsync(IDaemon daemon, string path)
    {
        try
        {
            await daemon.GetDirectoryInfoAsync(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<ComponentItemModel>> LoadComponentsAsync(IDaemon daemon, string folderPath, ComponentKind kind)
    {
        var result = new List<ComponentItemModel>();
        var (_, files, _) = await daemon.GetDirectoryInfoAsync(folderPath);

        foreach (var file in files)
        {
            var name = file.Name;
            var lower = name.ToLowerInvariant();
            var isJar = lower.EndsWith(".jar", StringComparison.Ordinal) || lower.EndsWith(".jar.disabled", StringComparison.Ordinal);
            if (!isJar) continue;

            var item = new ComponentItemModel
            {
                FileName = name,
                VirtualPath = $"{folderPath}/{name}",
                IsEnabled = !lower.EndsWith(".disabled", StringComparison.Ordinal),
                Kind = kind,
                FileSize = file.Meta.Size
            };

            var metadata = await TryDownloadAndParseAsync(daemon, item.VirtualPath, name);
            if (metadata != null)
            {
                item.DisplayName = metadata.DisplayName;
                item.Version = metadata.Version;
                item.IsClientSideOnly = metadata.IsClientSideOnly;
            }

            result.Add(item);
        }

        return result;
    }

    private static async Task<JarMetadata?> TryDownloadAndParseAsync(IDaemon daemon, string virtualPath, string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"mcsl_jar_{Guid.NewGuid():N}_{fileName}");
        try
        {
            var ctx = await daemon.DownloadFileAsync(virtualPath, tempPath, 1024 * 1024);
            if (ctx.NetworkLoadTask != null) await ctx.NetworkLoadTask;
            return JarMetadataParser.Parse(tempPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ComponentScanner] Failed to download/parse {0}", virtualPath);
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
