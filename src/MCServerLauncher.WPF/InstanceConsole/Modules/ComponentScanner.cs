using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using MCServerLauncher.WPF.Services;
using Serilog;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

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
    public static async Task<ComponentScanResult> ScanAsync(TypedDaemonClient daemon, Guid instanceId)
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

    public static async Task DisableAsync(TypedDaemonClient daemon, ComponentItemModel item)
    {
        if (!item.IsEnabled) return;
        await RenameAsync(daemon, item, item.FileName + ".disabled");
        item.IsEnabled = false;
    }

    public static async Task EnableAsync(TypedDaemonClient daemon, ComponentItemModel item)
    {
        if (item.IsEnabled) return;
        var newName = item.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? item.FileName[..^".disabled".Length]
            : item.FileName;
        await RenameAsync(daemon, item, newName);
        item.IsEnabled = true;
    }

    public static async Task RenameAsync(TypedDaemonClient daemon, ComponentItemModel item, string newName)
    {
        await EnsureSuccessAsync(daemon.Files.RenameFileAsync(new PathRenameRequest(item.VirtualPath, newName), default));
        var folderPath = item.VirtualPath[..item.VirtualPath.LastIndexOf('/')];
        item.FileName = newName;
        item.VirtualPath = $"{folderPath}/{newName}";
    }

    private static async Task<bool> DirectoryExistsAsync(TypedDaemonClient daemon, string path)
    {
        try
        {
            var result = await daemon.Files.GetDirectoryInfoAsync(new PathRequest(path), default);
            return result.IsOk(out _);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<ComponentItemModel>> LoadComponentsAsync(TypedDaemonClient daemon, string folderPath, ComponentKind kind)
    {
        var result = new List<ComponentItemModel>();
        var directoryResult = await daemon.Files.GetDirectoryInfoAsync(new PathRequest(folderPath), default);
        if (directoryResult.IsErr(out var error))
            throw DaemonErrorLocalization.ToException(error!);

        var files = directoryResult.Unwrap().Files;

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

    private static async Task<JarMetadata?> TryDownloadAndParseAsync(TypedDaemonClient daemon, string virtualPath, string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"mcsl_jar_{Guid.NewGuid():N}_{fileName}");
        try
        {
            var sessionResult = await daemon.Files.OpenDownloadAsync(new DownloadOpenRequest(virtualPath), default);
            if (sessionResult.IsErr(out var sessionError))
                throw DaemonErrorLocalization.ToException(sessionError!);

            var session = sessionResult.Unwrap();
            try
            {
                await using var stream = File.Create(tempPath);
                var offset = 0L;
                while (true)
                {
                    var chunkResult = await daemon.Files.ReadDownloadChunkAsync(
                        new DownloadChunkRequest(session.SessionId, offset, session.MaxChunkSize),
                        default);
                    if (chunkResult.IsErr(out var chunkError))
                        throw DaemonErrorLocalization.ToException(chunkError!);

                    var chunk = chunkResult.Unwrap();
                    if (chunk.Offset != offset)
                        throw new InvalidDataException("The daemon returned a download chunk at an unexpected offset.");

                    await stream.WriteAsync(chunk.Data.AsMemory());
                    offset += chunk.Data.Length;
                    if (chunk.IsFinal)
                        break;
                }
            }
            finally
            {
                var closeResult = await daemon.Files.CloseDownloadAsync(session.SessionId, default);
                if (closeResult.IsErr(out var closeError))
                    Log.Warning("[ComponentScanner] Failed to close download {0}: {1}", session.SessionId, closeError!.Message);
            }

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

    private static async Task EnsureSuccessAsync(Task<RustyOptions.Result<RustyOptions.Unit, DaemonError>> task)
    {
        var result = await task;
        if (result.IsErr(out var error))
            throw DaemonErrorLocalization.ToException(error!);
    }
}
