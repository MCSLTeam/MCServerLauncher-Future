using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using MCServerLauncher.WPF.ViewModels.Models;
using Serilog;

namespace MCServerLauncher.WPF.ViewModels;

public partial class DaemonManagerViewModel : ObservableObject
{
    private readonly IDaemonConnectionService _daemonService;
    private readonly INotificationService _notification;
    private readonly IDialogService _dialog;

    public ObservableCollection<DaemonCardModel> Daemons { get; } = [];
    public ObservableCollection<DaemonCardModel> FilteredDaemons { get; } = [];
    public IReadOnlyList<RefreshIntervalOption> RefreshIntervalOptions { get; } = RefreshIntervalOptionCatalog.All;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _autoRefreshEnabled = GetStoredRefreshInterval() > 0;
    [ObservableProperty] private int _refreshIntervalSeconds = RefreshIntervalOptionCatalog.Normalize(GetStoredRefreshInterval());

    public DaemonManagerViewModel(
        IDaemonConnectionService daemonService,
        INotificationService notification,
        IDialogService dialog)
    {
        _daemonService = daemonService;
        _notification = notification;
        _dialog = dialog;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Daemons.Clear();
        FilteredDaemons.Clear();

        if (DaemonsListManager.Get is not { Count: > 0 }) return;

        var connectionTasks = new List<Task>();
        foreach (var config in DaemonsListManager.Get)
        {
            var model = new DaemonCardModel
            {
                Config = config,
                Address = FormatAddress(config),
                FriendlyName = GetFriendlyName(config),
                Status = "ing"
            };
            Daemons.Add(model);
            connectionTasks.Add(ConnectDaemonInternalAsync(model));
        }
        await Task.WhenAll(connectionTasks);
        ApplyFilters();
    }

    [RelayCommand]
    private async Task AutoRefreshAsync()
    {
        if (Daemons.Count == 0) return;
        await Task.WhenAll(Daemons.Select(ConnectDaemonInternalAsync));
        ApplyFilters();
    }

    [RelayCommand]
    private async Task AddConnectionAsync()
    {
        (ContentDialog dialog, NewDaemonConnectionInput input) = await View.Components.Utils.ConstructConnectDaemonDialog();
        dialog.PrimaryButtonClick += async (o, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!await TryConnectNewDaemonAsync(input))
                {
                    dialog.Title = Lang.Tr["ConnectDaemonFailedTip"];
                    args.Cancel = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };
        try { await dialog.ShowAsync(); }
        catch { }
    }

    [RelayCommand]
    private async Task EditDaemonAsync(DaemonCardModel daemon)
    {
        var originalConfig = daemon.Config;
        (ContentDialog dialog, NewDaemonConnectionInput input) = await View.Components.Utils.ConstructConnectDaemonDialog(
            originalConfig.EndPoint ?? string.Empty,
            originalConfig.Port.ToString(),
            originalConfig.IsSecure,
            originalConfig.Token ?? string.Empty,
            originalConfig.FriendlyName ?? string.Empty,
            isRetrying: false,
            isEditing: true
        );

        dialog.PrimaryButtonClick += async (o, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!TryCreateConfig(input, out var newConfig))
                {
                    args.Cancel = true;
                    return;
                }

                var newModel = new DaemonCardModel
                {
                    Config = newConfig,
                    Address = FormatAddress(newConfig),
                    FriendlyName = GetFriendlyName(newConfig),
                    Status = "ing"
                };

                await _daemonService.RemoveAsync(originalConfig);
                if (await ConnectDaemonInternalAsync(newModel))
                {
                    DaemonsListManager.RemoveDaemon(originalConfig);
                    DaemonsListManager.AddDaemon(newConfig);
                    ApplyModel(daemon, newModel);
                    ApplyFilters();
                }
                else
                {
                    args.Cancel = true;
                    await ConnectDaemonInternalAsync(daemon);
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        try { await dialog.ShowAsync(); }
        catch { }
    }

    [RelayCommand]
    private async Task DeleteDaemonAsync(DaemonCardModel daemon)
    {
        var result = await _dialog.ShowCountdownConfirmAsync(
            Lang.Tr["ConfirmDelete"],
            string.Format(Lang.Tr["ConfirmDeleteDaemonMessage"], daemon.FriendlyName),
            Lang.Tr["Delete"],
            Lang.Tr["Cancel"]
        );

        if (result != ContentDialogResult.Primary) return;

        try
        {
            await _daemonService.RemoveAsync(daemon.Config);
            Daemons.Remove(daemon);
            ApplyFilters();
            DaemonsListManager.RemoveDaemon(daemon.Config);
            _notification.Push(Lang.Tr["Status_OK"], Lang.Tr["DaemonDeleted"], false, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error($"[Daemon] Error occurred when deleting daemon: {ex}");
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["DaemonDeleteFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    private async Task<bool> TryConnectNewDaemonAsync(NewDaemonConnectionInput input)
    {
        if (!TryCreateConfig(input, out var config)) return false;

        var model = new DaemonCardModel
        {
            Config = config,
            Address = FormatAddress(config),
            FriendlyName = GetFriendlyName(config),
            Status = "ing"
        };
        Daemons.Add(model);
        ApplyFilters();

        if (await ConnectDaemonInternalAsync(model))
        {
            ApplyFilters();
            DaemonsListManager.AddDaemon(config);
            return true;
        }

        Daemons.Remove(model);
        ApplyFilters();
        return false;
    }

    private async Task<bool> ConnectDaemonInternalAsync(DaemonCardModel model)
    {
        try
        {
            var daemon = await _daemonService.GetAsync(model.Config);
            if (daemon == null)
            {
                model.Status = "err";
                return false;
            }

            var systemInfo = await daemon.GetSystemInfoAsync();
            var systemName = systemInfo.Os.Name;
            var cpuVendor = systemInfo.Cpu.Vendor;

            if (systemName.Contains("Windows NT")) model.SystemType = "Windows";
            else if (systemName.Contains("Unix"))
            {
                model.SystemType = cpuVendor.Contains("Apple") ? "Darwin" : "Linux";
            }

            UpdateResourceUsage(model, systemInfo);
            model.Status = "ok";
            ApplyFilters();
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[Daemon] Error connecting to daemon({model.Address}): {e}");
            model.Status = "err";
            ApplyFilters();
            return false;
        }
    }

    private static bool TryCreateConfig(NewDaemonConnectionInput input, out Constants.DaemonConfigModel config)
    {
        config = new Constants.DaemonConfigModel();

        if (!int.TryParse(input.portEdit.Text, out var port)) return false;

        var endPoint = input.wsEdit.Text.Trim();
        var token = input.tokenEdit.Password;
        var friendlyName = input.friendlyNameEdit.Text.Trim();

        if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(token)) return false;

        config = new Constants.DaemonConfigModel
        {
            FriendlyName = friendlyName,
            EndPoint = endPoint,
            Port = port,
            Token = token,
            IsSecure = input.WebSocketScheme.SelectionBoxItem.ToString() == "wss://"
        };
        return true;
    }

    private static string FormatAddress(Constants.DaemonConfigModel config)
    {
        return $"{(config.IsSecure ? "wss" : "ws")}://{config.EndPoint}:{config.Port}";
    }

    private static string GetFriendlyName(Constants.DaemonConfigModel config)
    {
        return string.IsNullOrWhiteSpace(config.FriendlyName)
            ? Lang.Tr["Main_DaemonManagerNavMenu"]
            : config.FriendlyName;
    }

    private static void ApplyModel(DaemonCardModel target, DaemonCardModel source)
    {
        target.Config = source.Config;
        target.Address = source.Address;
        target.FriendlyName = source.FriendlyName;
        target.Status = source.Status;
        target.SystemType = source.SystemType;
        target.CpuUsage = source.CpuUsage;
        target.MemoryUsage = source.MemoryUsage;
        target.DriveUsage = source.DriveUsage;
        target.CpuUsageText = source.CpuUsageText;
        target.MemoryUsageText = source.MemoryUsageText;
        target.DriveUsageText = source.DriveUsageText;
        target.ResourceSummary = source.ResourceSummary;
        target.SystemVersion = source.SystemVersion;
        target.DaemonVersion = source.DaemonVersion;
        target.DriveUsageTooltip = source.DriveUsageTooltip;
    }

    private static void UpdateResourceUsage(DaemonCardModel model, MCServerLauncher.Common.ProtoType.Status.SystemInfo systemInfo)
    {
        model.CpuUsage = ClampPercentage(systemInfo.Cpu.Usage);
        model.MemoryUsage = CalculateUsagePercentage(systemInfo.Mem.Total, systemInfo.Mem.Free);
        model.SystemVersion = $"{systemInfo.Os.Name} ({systemInfo.Os.Arch})";
        model.DaemonVersion = string.IsNullOrWhiteSpace(systemInfo.DaemonVersion) ? "--" : systemInfo.DaemonVersion;

        var usedMemory = systemInfo.Mem.Total > systemInfo.Mem.Free ? systemInfo.Mem.Total - systemInfo.Mem.Free : 0;
        var drives = systemInfo.Drives is { Length: > 0 } ? systemInfo.Drives : [systemInfo.Drive];
        var totalDrive = drives.Aggregate(0UL, (sum, drive) => sum + drive.Total);
        var freeDrive = drives.Aggregate(0UL, (sum, drive) => sum + drive.Free);
        var usedDrive = totalDrive > freeDrive ? totalDrive - freeDrive : 0;

        model.DriveUsage = CalculateUsagePercentage(totalDrive, freeDrive);
        model.CpuUsageText = $"{model.CpuUsage:F2}% ({systemInfo.Cpu.CoreCount}C / {systemInfo.Cpu.ThreadCount}T)";
        model.MemoryUsageText = $"{model.MemoryUsage:F2}% ({FormatSize(usedMemory * 1024d)} / {FormatSize(systemInfo.Mem.Total * 1024d)})";
        model.DriveUsageText = $"{model.DriveUsage:F2}% ({FormatSize(usedDrive)} / {FormatSize(totalDrive)})";
        model.DriveUsageTooltip = string.Join(Environment.NewLine, drives.Select(FormatDriveUsage));
        model.ResourceSummary = $"{Lang.Tr["Daemon_CpuUsage"]} {model.CpuUsage:F2}% | {Lang.Tr["Daemon_MemoryUsage"]} {model.MemoryUsage:F2}% | {Lang.Tr["Daemon_DriveUsage"]} {model.DriveUsage:F2}%";
    }

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        SettingsManager.SaveSetting("Instance.AutoRefreshInterval", value ? RefreshIntervalSeconds : 0);
    }

    partial void OnRefreshIntervalSecondsChanged(int value)
    {
        var normalizedValue = RefreshIntervalOptionCatalog.Normalize(value);
        if (value != normalizedValue)
        {
            RefreshIntervalSeconds = normalizedValue;
            return;
        }

        if (AutoRefreshEnabled)
        {
            SettingsManager.SaveSetting("Instance.AutoRefreshInterval", RefreshIntervalSeconds);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var searchText = SearchText.Trim();
        var filtered = Daemons.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(model => MatchesSearch(model, searchText));
        }

        FilteredDaemons.Clear();
        foreach (var model in filtered)
        {
            FilteredDaemons.Add(model);
        }
    }

    private static bool MatchesSearch(DaemonCardModel model, string searchText)
    {
        return Contains(model.FriendlyName, searchText)
               || Contains(model.Address, searchText)
               || Contains(model.Status, searchText)
               || Contains(model.SystemType, searchText)
               || Contains(model.SystemVersion, searchText)
               || Contains(model.DaemonVersion, searchText)
               || Contains(model.Config.FriendlyName, searchText)
               || Contains(model.Config.EndPoint, searchText)
               || Contains(model.Config.Port.ToString(), searchText);
    }

    private static bool Contains(string? value, string searchText)
    {
        return value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static double CalculateUsagePercentage(ulong total, ulong free)
    {
        if (total == 0) return 0;
        var used = total > free ? total - free : 0;
        return ClampPercentage(used * 100d / total);
    }

    private static double ClampPercentage(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        return Math.Clamp(value, 0, 100);
    }

    private static string FormatSize(double bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var suffixIndex = 0;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:F2} {suffixes[suffixIndex]}";
    }

    private static string FormatDriveUsage(MCServerLauncher.Common.ProtoType.Status.DriveInformation drive)
    {
        var used = drive.Total > drive.Free ? drive.Total - drive.Free : 0;
        var usage = CalculateUsagePercentage(drive.Total, drive.Free);
        var name = string.IsNullOrWhiteSpace(drive.Name) ? drive.DriveFormat : drive.Name;
        return $"{name} {usage:F2}% ({FormatSize(used)} / {FormatSize(drive.Total)})";
    }

    private static int GetStoredRefreshInterval()
    {
        return SettingsManager.Get?.Instance?.AutoRefreshInterval ?? 5;
    }
}
