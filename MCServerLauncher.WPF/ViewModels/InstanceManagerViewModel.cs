using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.ViewModels.Models;
using Serilog;

namespace MCServerLauncher.WPF.ViewModels;

public partial class InstanceManagerViewModel : ObservableObject
{
    private readonly IDaemonConnectionService _daemonService;
    private readonly INotificationService _notification;
    private readonly IDialogService _dialog;
    private readonly INavigationService _navigation;

    public ObservableCollection<InstanceCardModel> AllInstances { get; } = [];
    public ObservableCollection<InstanceCardModel> FilteredInstances { get; } = [];
    public ObservableCollection<string> DaemonFilterItems { get; } = [];
    public IReadOnlyList<RefreshIntervalOption> RefreshIntervalOptions { get; } = RefreshIntervalOptionCatalog.All;

    [ObservableProperty] private int _selectedDaemonIndex;
    [ObservableProperty] private string _selectedStatusFilter = "All";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorState;
    [ObservableProperty] private bool _autoRefreshEnabled = GetStoredRefreshInterval() > 0;
    [ObservableProperty] private int _refreshIntervalSeconds = RefreshIntervalOptionCatalog.Normalize(GetStoredRefreshInterval());

    public InstanceManagerViewModel(
        IDaemonConnectionService daemonService,
        INotificationService notification,
        IDialogService dialog,
        INavigationService navigation)
    {
        _daemonService = daemonService;
        _notification = notification;
        _dialog = dialog;
        _navigation = navigation;
    }

    public void LoadDaemonFilterItems()
    {
        DaemonFilterItems.Clear();
        if (DaemonsListManager.Get is { Count: > 0 })
        {
            foreach (var daemon in DaemonsListManager.Get)
            {
                DaemonFilterItems.Add($"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]");
            }
        }
        SelectedDaemonIndex = 0;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        AllInstances.Clear();
        FilteredInstances.Clear();
        ErrorState = null;

        if (DaemonsListManager.Get is not { Count: > 0 })
        {
            ErrorState = "no_daemon";
            return;
        }

        IsLoading = true;
        await LoadDaemonInstancesAsync();
        ApplyFilters();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task AutoRefreshAsync()
    {
        if (DaemonsListManager.Get is not { Count: > 0 }) return;
        await LoadDaemonInstancesAsync(isAutoRefresh: true);
        ApplyFilters();
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

    private async Task LoadDaemonInstancesAsync(bool isAutoRefresh = false)
    {
        if (DaemonsListManager.Get is not { Count: > 0 })
        {
            if (!isAutoRefresh) ErrorState = "no_daemon";
            return;
        }

        var daemonIndex = SelectedDaemonIndex;
        if (daemonIndex < 0 || daemonIndex >= DaemonsListManager.Get.Count) return;

        var daemonConfig = DaemonsListManager.Get[daemonIndex];
        try
        {
            var daemon = await _daemonService.GetAsync(daemonConfig)
                ?? throw new Exception("Daemon is offline or unreachable.");
            var memoryTotalBytes = await TryGetDaemonMemoryTotalBytesAsync(daemon, daemonConfig);
            var instanceReports = await DaemonExtensions.GetAllReportsAsync(daemon);

            if (instanceReports == null || instanceReports.Count == 0)
            {
                if (!isAutoRefresh)
                {
                    ErrorState = "no_instance";
                }
                return;
            }

            if (isAutoRefresh)
            {
                UpdateExistingCards(instanceReports, daemonConfig, memoryTotalBytes);
            }
            else
            {
                foreach (var kvp in instanceReports)
                {
                    AllInstances.Add(CreateInstanceCard(kvp.Key, kvp.Value, daemonConfig, memoryTotalBytes));
                }
            }
        }
        catch (Exception ex)
        {
            if (!isAutoRefresh) ErrorState = "load_error";
            Log.Error($"[InstanceManager] Failed to load instances: {ex.Message}");
        }
    }

    private void UpdateExistingCards(Dictionary<Guid, InstanceReport> reports, Constants.DaemonConfigModel daemonConfig, ulong memoryTotalBytes)
    {
        var existingIds = AllInstances.Select(c => c.InstanceId).ToList();
        var newIds = reports.Keys.ToList();

        var idsToRemove = existingIds.Except(newIds).ToList();
        foreach (var id in idsToRemove)
        {
            var card = AllInstances.FirstOrDefault(c => c.InstanceId == id);
            if (card != null) AllInstances.Remove(card);
        }

        foreach (var kvp in reports)
        {
            var existing = AllInstances.FirstOrDefault(c => c.InstanceId == kvp.Key);
            if (existing != null)
            {
                existing.Status = kvp.Value.Status;
                existing.CpuUsage = kvp.Value.PerformanceCounter.Cpu;
                existing.MemoryUsage = kvp.Value.PerformanceCounter.Memory;
                existing.MemoryTotalBytes = memoryTotalBytes;
            }
            else
            {
                AllInstances.Add(CreateInstanceCard(kvp.Key, kvp.Value, daemonConfig, memoryTotalBytes));
            }
        }
    }

    private InstanceCardModel CreateInstanceCard(Guid id, InstanceReport report, Constants.DaemonConfigModel config, ulong memoryTotalBytes)
    {
        return new InstanceCardModel
        {
            DaemonConfig = config,
            StartCommand = StartInstanceCommand,
            StopCommand = StopInstanceCommand,
            RestartCommand = RestartInstanceCommand,
            KillCommand = KillInstanceCommand,
            DeleteCommand = DeleteInstanceCommand,
            InstanceId = id,
            InstanceName = report.Config.Name,
            InstanceType = report.Config.InstanceType.ToString(),
            Version = report.Config.Version ?? "",
            Status = report.Status,
            CpuUsage = report.PerformanceCounter.Cpu,
            MemoryUsage = report.PerformanceCounter.Memory,
            MemoryTotalBytes = memoryTotalBytes
        };
    }

    private static async Task<ulong> TryGetDaemonMemoryTotalBytesAsync(IDaemon daemon, Constants.DaemonConfigModel daemonConfig)
    {
        try
        {
            var systemInfo = await daemon.GetSystemInfoAsync();
            return systemInfo.Mem.Total * 1024UL;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[InstanceManager] Failed to get system info for daemon {Daemon}", daemonConfig.FriendlyName);
            return 0;
        }
    }

    public void ApplyFilters()
    {
        var filtered = AllInstances.AsEnumerable();

        filtered = SelectedStatusFilter switch
        {
            "Starting" => filtered.Where(c => c.Status == InstanceStatus.Starting),
            "Running" => filtered.Where(c => c.Status == InstanceStatus.Running),
            "Stopping" => filtered.Where(c => c.Status == InstanceStatus.Stopping),
            "Stopped" => filtered.Where(c => c.Status == InstanceStatus.Stopped),
            "Crashed" => filtered.Where(c => c.Status == InstanceStatus.Crashed),
            _ => filtered
        };

        var searchText = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(card => MatchesSearch(card, searchText));
        }

        var filteredList = filtered.ToList();

        FilteredInstances.Clear();
        foreach (var card in filteredList)
        {
            FilteredInstances.Add(card);
        }
    }

    [RelayCommand]
    private async Task StartInstanceAsync(InstanceCardModel instance)
    {
        if (!instance.CanStart)
        {
            PushActionUnavailable("InstanceCard_StartUnavailable", instance);
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_StartConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_StartConfirmContent"], instance.InstanceName),
            Lang.Tr["Start"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var daemon = await _daemonService.GetAsync(instance.DaemonConfig);
            if (daemon == null)
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }
            await daemon.StartInstanceAsync(instance.InstanceId);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_StartingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            Log.Information("[InstanceManager] Started instance {0}", instance.InstanceId);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to start instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_StartFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task StopInstanceAsync(InstanceCardModel instance)
    {
        if (!instance.CanStop)
        {
            PushActionUnavailable("InstanceCard_StopUnavailable", instance);
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_StopConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_StopConfirmContent"], instance.InstanceName),
            Lang.Tr["Stop"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var daemon = await _daemonService.GetAsync(instance.DaemonConfig);
            if (daemon == null)
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }
            await daemon.StopInstanceAsync(instance.InstanceId);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_StoppingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to stop instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_StopFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task RestartInstanceAsync(InstanceCardModel instance)
    {
        if (!instance.CanRestart)
        {
            PushActionUnavailable("InstanceCard_RestartUnavailable", instance);
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_RestartConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_RestartConfirmContent"], instance.InstanceName),
            Lang.Tr["Restart"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var daemon = await _daemonService.GetAsync(instance.DaemonConfig);
            if (daemon == null)
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }
            await daemon.RestartInstanceAsync(instance.InstanceId);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_RestartingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to restart instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_RestartFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task KillInstanceAsync(InstanceCardModel instance)
    {
        if (!instance.CanKill)
        {
            PushActionUnavailable("InstanceCard_KillUnavailable", instance);
            return;
        }

        var result = await _dialog.ShowCountdownConfirmAsync(
            Lang.Tr["InstanceCard_KillConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_KillConfirmContent"], instance.InstanceName),
            Lang.Tr["Kill"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var daemon = await _daemonService.GetAsync(instance.DaemonConfig);
            if (daemon == null)
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }
            await daemon.KillInstanceAsync(instance.InstanceId);
            _notification.Push(Lang.Tr["Warning"], string.Format(Lang.Tr["InstanceCard_KillingInstance"], instance.InstanceName), false, InfoBarSeverity.Warning);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to kill instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_KillFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteInstanceAsync(InstanceCardModel instance)
    {
        if (!instance.CanDelete)
        {
            PushActionUnavailable("InstanceCard_DeleteUnavailable", instance);
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_DeleteConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_DeleteConfirmContent"], instance.InstanceName),
            Lang.Tr["Delete"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var daemon = await _daemonService.GetAsync(instance.DaemonConfig);
            if (daemon == null)
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }
            await daemon.RemoveInstanceAsync(instance.InstanceId);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_DeletedInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to delete instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_DeleteFailed"], ex.Message), true, InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void OpenConsole(InstanceCardModel instance)
    {
        if (instance.DaemonConfig == null)
        {
            _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["InstanceCard_NoDaemonConfig"], true, InfoBarSeverity.Error);
            return;
        }
        Instance.InitializeNewInstanceConsole(instance.DaemonConfig, instance.InstanceId);
    }

    private static int GetStoredRefreshInterval()
    {
        return SettingsManager.Get?.Instance?.AutoRefreshInterval ?? 5;
    }

    private static bool MatchesSearch(InstanceCardModel card, string searchText)
    {
        return Contains(card.InstanceName, searchText)
               || Contains(card.InstanceType, searchText)
               || Contains(card.Version, searchText)
               || Contains(card.StatusText, searchText)
               || Contains(card.Status.ToString(), searchText)
               || Contains(card.InstanceId.ToString(), searchText)
               || Contains(card.DaemonConfig.FriendlyName, searchText)
               || Contains(card.DaemonConfig.EndPoint, searchText);
    }

    private static bool Contains(string? value, string searchText)
    {
        return value?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void PushActionUnavailable(string resourceKey, InstanceCardModel instance)
    {
        _notification.Push(
            Lang.Tr["Warning"],
            string.Format(Lang.Tr[resourceKey], instance.InstanceName),
            false,
            InfoBarSeverity.Warning);
    }
}
