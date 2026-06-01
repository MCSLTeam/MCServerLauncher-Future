using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Microsoft.Win32;
using Serilog;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels;

public partial class ComponentManagerViewModel : ObservableObject
{
    private readonly INotificationService _notification;
    private IDaemon? _daemon;
    private string _instanceRoot = string.Empty;

    [ObservableProperty] private ObservableCollection<ComponentItemModel> _mods = new();
    [ObservableProperty] private ObservableCollection<ComponentItemModel> _plugins = new();
    [ObservableProperty] private bool _hasMods;
    [ObservableProperty] private bool _hasPlugins;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedTabIndex;

    public ComponentManagerViewModel(INotificationService notification)
    {
        _notification = notification;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var instanceId = InstanceDataManager.Instance.InstanceId;
            var daemonField = typeof(InstanceDataManager).GetField("_daemon",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _daemon = daemonField?.GetValue(InstanceDataManager.Instance) as IDaemon;

            if (_daemon == null)
            {
                Log.Error("[ComponentManager] Failed to get daemon connection");
                _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_DaemonUnavailable"],
                    true, InfoBarSeverity.Error);
                return;
            }

            _instanceRoot = $"/instances/{instanceId}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ComponentManager] Failed to initialize");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_daemon == null) return;
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            HasMods = await DirectoryExistsAsync($"{_instanceRoot}/mods");
            HasPlugins = await DirectoryExistsAsync($"{_instanceRoot}/plugins");

            Mods.Clear();
            Plugins.Clear();

            if (HasMods)
            {
                var items = await LoadComponentsAsync("mods", ComponentKind.Mod);
                foreach (var item in items) Mods.Add(item);
            }

            if (HasPlugins)
            {
                var items = await LoadComponentsAsync("plugins", ComponentKind.Plugin);
                foreach (var item in items) Plugins.Add(item);
            }

            // Auto-select first available tab
            if (HasMods) SelectedTabIndex = 0;
            else if (HasPlugins) SelectedTabIndex = 1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ComponentManager] Failed to refresh");
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> DirectoryExistsAsync(string path)
    {
        if (_daemon == null) return false;
        try
        {
            await _daemon.GetDirectoryInfoAsync(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<ComponentItemModel>> LoadComponentsAsync(string folder, ComponentKind kind)
    {
        var result = new List<ComponentItemModel>();
        if (_daemon == null) return result;

        var folderPath = $"{_instanceRoot}/{folder}";
        var (_, files, _) = await _daemon.GetDirectoryInfoAsync(folderPath);

        foreach (var file in files)
        {
            var name = file.Name;
            var lower = name.ToLowerInvariant();
            bool isJar = lower.EndsWith(".jar") || lower.EndsWith(".jar.disabled");
            if (!isJar) continue;

            bool isEnabled = !lower.EndsWith(".disabled");
            var item = new ComponentItemModel
            {
                FileName = name,
                VirtualPath = $"{folderPath}/{name}",
                IsEnabled = isEnabled,
                Kind = kind,
                FileSize = file.Meta.Size
            };

            // Try parse metadata for enabled jars (disabled also try by stripping suffix)
            var metadata = await TryDownloadAndParseAsync(item.VirtualPath, name);
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

    private async Task<JarMetadata?> TryDownloadAndParseAsync(string virtualPath, string fileName)
    {
        if (_daemon == null) return null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"mcsl_jar_{Guid.NewGuid():N}_{fileName}");
        try
        {
            var ctx = await _daemon.DownloadFileAsync(virtualPath, tempPath, 1024 * 1024);
            if (ctx.NetworkLoadTask != null) await ctx.NetworkLoadTask;
            return JarMetadataParser.Parse(tempPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ComponentManager] Failed to download/parse {0}", virtualPath);
            return null;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ComponentItemModel? item)
    {
        if (item == null || _daemon == null) return;
        try
        {
            string newName = item.IsEnabled
                ? item.FileName + ".disabled"
                : item.FileName.EndsWith(".disabled")
                    ? item.FileName.Substring(0, item.FileName.Length - ".disabled".Length)
                    : item.FileName;

            await _daemon.RenameFileAsync(item.VirtualPath, newName);

            var folderPath = item.VirtualPath.Substring(0, item.VirtualPath.LastIndexOf('/'));
            item.FileName = newName;
            item.VirtualPath = $"{folderPath}/{newName}";
            item.IsEnabled = !item.IsEnabled;

            _notification.Push(Lang.Tr["Success"],
                item.IsEnabled ? Lang.Tr["ComponentManager_Enabled"] : Lang.Tr["ComponentManager_Disabled"],
                false, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ComponentManager] Failed to toggle {0}", item.FileName);
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void Locate(ComponentItemModel? item)
    {
        if (item == null) return;
        try
        {
            System.Windows.Clipboard.SetText(item.VirtualPath);
            _notification.Push(Lang.Tr["ComponentManager_PathCopied"], item.VirtualPath,
                true, InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ComponentManager] Failed to copy path");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ComponentItemModel? item)
    {
        if (item == null || _daemon == null) return;

        var dialog = new ContentDialog
        {
            Title = Lang.Tr["ConfirmDelete"],
            Content = string.Format(Lang.Tr["ComponentManager_ConfirmDeleteMessage"], item.Title),
            PrimaryButtonText = Lang.Tr["Delete"],
            CloseButtonText = Lang.Tr["Cancel"],
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await _daemon.DeleteFileAsync(item.VirtualPath);
            if (item.Kind == ComponentKind.Mod) Mods.Remove(item);
            else Plugins.Remove(item);

            _notification.Push(Lang.Tr["Success"], Lang.Tr["ComponentManager_Deleted"],
                false, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ComponentManager] Failed to delete {0}", item.FileName);
            _notification.Push(Lang.Tr["Error"], ex.Message, true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task AddComponentAsync()
    {
        if (_daemon == null) return;

        var (folder, kind) = GetCurrentTargetFolder();
        if (folder == null)
        {
            _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_NoTargetFolder"],
                true, InfoBarSeverity.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Java Archive (*.jar)|*.jar|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        await UploadFilesAsync(dialog.FileNames, folder, kind);
    }

    public async Task HandleDroppedFilesAsync(string[] filePaths)
    {
        if (_daemon == null) return;
        var (folder, kind) = GetCurrentTargetFolder();
        if (folder == null)
        {
            _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_NoTargetFolder"],
                true, InfoBarSeverity.Warning);
            return;
        }

        var jars = filePaths
            .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (jars.Length == 0)
        {
            _notification.Push(Lang.Tr["Error"], Lang.Tr["ComponentManager_NoJarFiles"],
                true, InfoBarSeverity.Warning);
            return;
        }

        await UploadFilesAsync(jars, folder, kind);
    }

    private (string? folder, ComponentKind kind) GetCurrentTargetFolder()
    {
        if (SelectedTabIndex == 0 && HasMods) return ("mods", ComponentKind.Mod);
        if (SelectedTabIndex == 1 && HasPlugins) return ("plugins", ComponentKind.Plugin);
        if (HasMods) return ("mods", ComponentKind.Mod);
        if (HasPlugins) return ("plugins", ComponentKind.Plugin);
        return (null, ComponentKind.Mod);
    }

    private async Task UploadFilesAsync(string[] localPaths, string folder, ComponentKind kind)
    {
        if (_daemon == null) return;
        IsLoading = true;
        int success = 0;
        try
        {
            foreach (var local in localPaths)
            {
                try
                {
                    if (kind == ComponentKind.Mod && JarMetadataParser.IsClientSideMod(local))
                    {
                        _notification.Push(
                            Lang.Tr["Warning"],
                            string.Format(Lang.Tr["ComponentManager_ClientSideModBlocked"], Path.GetFileName(local)),
                            true,
                            InfoBarSeverity.Warning);
                        continue;
                    }

                    var fileName = Path.GetFileName(local);
                    var target = $"{_instanceRoot}/{folder}/{fileName}";
                    var ctx = await _daemon.UploadFileAsync(local, target, 1024 * 1024);
                    if (ctx.NetworkLoadTask != null) await ctx.NetworkLoadTask;
                    success++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[ComponentManager] Failed to upload {0}", local);
                }
            }

            _notification.Push(Lang.Tr["Success"],
                string.Format(Lang.Tr["ComponentManager_AddedCount"], success, localPaths.Length),
                false, InfoBarSeverity.Success);

            IsLoading = false;
            await RefreshAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
