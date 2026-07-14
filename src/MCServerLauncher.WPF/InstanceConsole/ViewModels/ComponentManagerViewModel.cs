using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Microsoft.Win32;
using Serilog;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.InstanceConsole.ViewModels;

public partial class ComponentManagerViewModel : ObservableObject
{
    private readonly INotificationService _notification;
    private TypedDaemonClient? _daemon;
    private string _instanceRoot = string.Empty;

    [ObservableProperty] private ObservableCollection<ComponentItemModel> _mods = new();
    [ObservableProperty] private ObservableCollection<ComponentItemModel> _plugins = new();
    [ObservableProperty] private bool _hasMods;
    [ObservableProperty] private bool _hasPlugins;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _supportsComponents;

    public ComponentManagerViewModel(INotificationService notification)
    {
        _notification = notification;
        Mods.CollectionChanged += OnComponentCollectionChanged;
        Plugins.CollectionChanged += OnComponentCollectionChanged;
    }

    public bool IsCurrentTabEmpty => SupportsComponents && IsCurrentTabSupported && CurrentItems.Count == 0;
    public bool IsCurrentTabSupported => SelectedTabIndex == 0 ? HasMods : HasPlugins;
    public string AddComponentText => SelectedTabIndex == 1
        ? Lang.Tr["ComponentManager_AddPlugin"]
        : Lang.Tr["ComponentManager_AddMod"];
    public ObservableCollection<ComponentItemModel> CurrentItems => SelectedTabIndex == 1 ? Plugins : Mods;

    public async Task InitializeAsync()
    {
        try
        {
            var instanceId = InstanceDataManager.Instance.InstanceId;
            var daemonField = typeof(InstanceDataManager).GetField("_daemon",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _daemon = daemonField?.GetValue(InstanceDataManager.Instance) as TypedDaemonClient;

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
            var result = await ComponentScanner.ScanAsync(_daemon, InstanceDataManager.Instance.InstanceId);
            HasMods = result.HasMods;
            HasPlugins = result.HasPlugins;
            SupportsComponents = result.SupportsComponents;

            Mods.Clear();
            Plugins.Clear();

            foreach (var item in result.Mods) Mods.Add(item);
            foreach (var item in result.Plugins) Plugins.Add(item);

            // Auto-select first available tab
            if (HasMods) SelectedTabIndex = 0;
            else if (HasPlugins) SelectedTabIndex = 1;
            else SelectedTabIndex = 0;
            OnTabStateChanged();
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

    [RelayCommand]
    private async Task ToggleEnabledAsync(ComponentItemModel? item)
    {
        if (item == null || _daemon == null) return;
        try
        {
            if (item.IsEnabled) await ComponentScanner.DisableAsync(_daemon, item);
            else await ComponentScanner.EnableAsync(_daemon, item);

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
            var deleteResult = await _daemon.Files.DeleteFileAsync(new PathRequest(item.VirtualPath), default);
            if (deleteResult.IsErr(out var deleteError))
                throw DaemonErrorLocalization.ToException(deleteError!);
            if (item.Kind == ComponentKind.Mod) Mods.Remove(item);
            else Plugins.Remove(item);
            OnTabStateChanged();

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

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnTabStateChanged();
    }

    partial void OnHasModsChanged(bool value)
    {
        OnTabStateChanged();
    }

    partial void OnHasPluginsChanged(bool value)
    {
        OnTabStateChanged();
    }

    partial void OnSupportsComponentsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCurrentTabEmpty));
    }

    private void OnTabStateChanged()
    {
        OnPropertyChanged(nameof(IsCurrentTabEmpty));
        OnPropertyChanged(nameof(IsCurrentTabSupported));
        OnPropertyChanged(nameof(AddComponentText));
        OnPropertyChanged(nameof(CurrentItems));
    }

    private void OnComponentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnTabStateChanged();
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
                    await using var stream = File.OpenRead(local);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                    stream.Position = 0;

                    var openResult = await _daemon.Files.OpenUploadAsync(
                        new UploadOpenRequest(target, stream.Length, hash),
                        default);
                    if (openResult.IsErr(out var openError))
                        throw DaemonErrorLocalization.ToException(openError!);

                    var session = openResult.Unwrap();
                    var completed = false;
                    try
                    {
                        var buffer = new byte[session.MaxChunkSize];
                        var offset = 0L;
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                        {
                            var writeResult = await _daemon.Files.WriteUploadChunkAsync(
                                new UploadChunkRequest(session.SessionId, offset, ImmutableArray.Create(buffer[..read])),
                                default);
                            if (writeResult.IsErr(out var writeError))
                            throw DaemonErrorLocalization.ToException(writeError!);

                            offset += read;
                        }

                        var closeResult = await _daemon.Files.CloseUploadAsync(session.SessionId, default);
                        if (closeResult.IsErr(out var closeError))
                        throw DaemonErrorLocalization.ToException(closeError!);

                        completed = true;
                    }
                    finally
                    {
                        if (!completed)
                        {
                            var cancelResult = await _daemon.Files.CancelUploadAsync(session.SessionId, default);
                            if (cancelResult.IsErr(out var cancelError))
                                Log.Warning("[ComponentManager] Failed to cancel upload {0}: {1}", session.SessionId, cancelError!.Message);
                        }
                    }

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
