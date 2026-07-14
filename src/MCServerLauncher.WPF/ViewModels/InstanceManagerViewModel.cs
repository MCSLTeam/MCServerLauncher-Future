using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.ViewModels.Models;
using Serilog;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;
using TypedInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

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
            var connectionResult = await _daemonService.GetAsync(daemonConfig);
            if (connectionResult.IsErr(out var connectionError))
                throw DaemonErrorLocalization.ToException(connectionError!);

            var daemon = connectionResult.Unwrap();
            var memoryTotalBytes = await GetDaemonMemoryTotalBytesOrNullAsync(daemon, daemonConfig);
            var reportsResult = await daemon.Instances.ListInstanceReportsAsync(default);
            if (reportsResult.IsErr(out var reportsError))
                throw DaemonErrorLocalization.ToException(reportsError!);

            var catalog = daemon.InstanceCatalog.Current.Value;
            var instanceReports = reportsResult.Unwrap().Reports
                .Where(pair => catalog.Instances.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

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

    private void UpdateExistingCards(IReadOnlyDictionary<Guid, TypedInstanceReport> reports, Constants.DaemonConfigModel daemonConfig, ulong? memoryTotalBytes)
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
                existing.MemoryUsage = kvp.Value.PerformanceCounter.MemoryBytes;
                existing.MemoryTotalBytes = memoryTotalBytes;
            }
            else
            {
                AllInstances.Add(CreateInstanceCard(kvp.Key, kvp.Value, daemonConfig, memoryTotalBytes));
            }
        }
    }

    private InstanceCardModel CreateInstanceCard(Guid id, TypedInstanceReport report, Constants.DaemonConfigModel config, ulong? memoryTotalBytes)
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
            MemoryUsage = report.PerformanceCounter.MemoryBytes,
            MemoryTotalBytes = memoryTotalBytes
        };
    }

    private static async Task<ulong?> GetDaemonMemoryTotalBytesOrNullAsync(TypedDaemonClient daemon, Constants.DaemonConfigModel daemonConfig)
    {
        try
        {
            var systemInfoResult = await daemon.System.GetSystemInfoAsync(default);
            if (systemInfoResult.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);

            return systemInfoResult.Unwrap().Mem.TotalKilobytes * 1024UL;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[InstanceManager] Failed to get system info for daemon {Daemon}", daemonConfig.FriendlyName);
            return null;
        }
    }

    public void ApplyFilters()
    {
        var filtered = AllInstances.AsEnumerable();

        filtered = SelectedStatusFilter switch
        {
            "Running" => filtered.Where(c => c.Status == InstanceStatus.Running),
            "Stopped" => filtered.Where(c => c.Status == InstanceStatus.Stopped),
            "Crashed" => filtered.Where(c => c.Status == InstanceStatus.Crashed),
            _ => filtered
        };

        var searchText = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filtered = filtered.Where(card => MatchesSearch(card, searchText));
        }

        SyncFilteredInstances(filtered.ToList());
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
            var connectionResult = await _daemonService.GetAsync(instance.DaemonConfig);
            if (connectionResult.IsErr(out var connectionError))
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }

            var daemon = connectionResult.Unwrap();
            var startResult = await daemon.Instances.StartInstanceAsync(new InstanceReference(instance.InstanceId), default);
            if (startResult.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_StartingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            Log.Information("[InstanceManager] Started instance {0}", instance.InstanceId);
            await RefreshCardsInPlaceAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to start instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_StartFailed"], ex.Message), true, InfoBarSeverity.Error);
            await RefreshCardsInPlaceAsync();
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
            var connectionResult = await _daemonService.GetAsync(instance.DaemonConfig);
            if (connectionResult.IsErr(out var connectionError))
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }

            var daemon = connectionResult.Unwrap();
            var stopResult = await daemon.Instances.StopInstanceAsync(new InstanceReference(instance.InstanceId), default);
            if (stopResult.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_StoppingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshCardsInPlaceAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to stop instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_StopFailed"], ex.Message), true, InfoBarSeverity.Error);
            await RefreshCardsInPlaceAsync();
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
            var connectionResult = await _daemonService.GetAsync(instance.DaemonConfig);
            if (connectionResult.IsErr(out var connectionError))
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }

            var daemon = connectionResult.Unwrap();
            var restartResult = await daemon.RestartInstanceAsync(instance.InstanceId);
            if (restartResult.IsErr(out var restartError))
                throw DaemonErrorLocalization.ToException(restartError!);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_RestartingInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshCardsInPlaceAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to restart instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_RestartFailed"], ex.Message), true, InfoBarSeverity.Error);
            await RefreshCardsInPlaceAsync();
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
            var connectionResult = await _daemonService.GetAsync(instance.DaemonConfig);
            if (connectionResult.IsErr(out var connectionError))
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }

            var daemon = connectionResult.Unwrap();
            var haltResult = await daemon.Instances.HaltInstanceAsync(new InstanceReference(instance.InstanceId), default);
            if (haltResult.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);
            _notification.Push(Lang.Tr["Warning"], string.Format(Lang.Tr["InstanceCard_KillingInstance"], instance.InstanceName), false, InfoBarSeverity.Warning);
            await RefreshCardsInPlaceAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to kill instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_KillFailed"], ex.Message), true, InfoBarSeverity.Error);
            await RefreshCardsInPlaceAsync();
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
            var connectionResult = await _daemonService.GetAsync(instance.DaemonConfig);
            if (connectionResult.IsErr(out var connectionError))
            {
                _notification.Push(Lang.Tr["Status_Error"], Lang.Tr["ConnectDaemonFailedTip"], true, InfoBarSeverity.Error);
                return;
            }

            var daemon = connectionResult.Unwrap();
            var removeResult = await daemon.Instances.RemoveInstanceAsync(new InstanceReference(instance.InstanceId), default);
            if (removeResult.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);
            RemoveInstanceCard(instance);
            _notification.Push(Lang.Tr["Status_OK"], string.Format(Lang.Tr["InstanceCard_DeletedInstance"], instance.InstanceName), false, InfoBarSeverity.Success);
            await RefreshCardsInPlaceAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstanceManager] Failed to delete instance {0}", instance.InstanceId);
            _notification.Push(Lang.Tr["Status_Error"], string.Format(Lang.Tr["InstanceCard_DeleteFailed"], ex.Message), true, InfoBarSeverity.Error);
            await RefreshCardsInPlaceAsync();
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

    private async Task RefreshCardsInPlaceAsync()
    {
        await AutoRefreshAsync();
    }

    private void RemoveInstanceCard(InstanceCardModel instance)
    {
        AllInstances.Remove(instance);
        FilteredInstances.Remove(instance);
        ApplyFilters();
    }

    private void SyncFilteredInstances(IReadOnlyList<InstanceCardModel> filteredList)
    {
        for (var i = FilteredInstances.Count - 1; i >= 0; i--)
        {
            if (!filteredList.Contains(FilteredInstances[i]))
            {
                FilteredInstances.RemoveAt(i);
            }
        }

        for (var i = 0; i < filteredList.Count; i++)
        {
            var item = filteredList[i];
            var currentIndex = FilteredInstances.IndexOf(item);
            if (currentIndex < 0)
            {
                FilteredInstances.Insert(i, item);
            }
            else if (currentIndex != i)
            {
                FilteredInstances.Move(currentIndex, i);
            }
        }
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
