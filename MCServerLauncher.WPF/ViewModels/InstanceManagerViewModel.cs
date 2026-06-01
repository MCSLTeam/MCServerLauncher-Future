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

    [ObservableProperty] private int _selectedDaemonIndex;
    [ObservableProperty] private string _selectedStatusFilter = "All";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorState;

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
                UpdateExistingCards(instanceReports, daemonConfig);
            }
            else
            {
                foreach (var kvp in instanceReports)
                {
                    AllInstances.Add(CreateInstanceCard(kvp.Key, kvp.Value, daemonConfig));
                }
            }
        }
        catch (Exception ex)
        {
            if (!isAutoRefresh) ErrorState = "load_error";
            Log.Error($"[InstanceManager] Failed to load instances: {ex.Message}");
        }
    }

    private void UpdateExistingCards(Dictionary<Guid, InstanceReport> reports, Constants.DaemonConfigModel daemonConfig)
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
            }
            else
            {
                AllInstances.Add(CreateInstanceCard(kvp.Key, kvp.Value, daemonConfig));
            }
        }
    }

    private static InstanceCardModel CreateInstanceCard(Guid id, InstanceReport report, Constants.DaemonConfigModel config)
    {
        return new InstanceCardModel
        {
            DaemonConfig = config,
            InstanceId = id,
            InstanceName = report.Config.Name,
            InstanceType = report.Config.InstanceType.ToString(),
            Version = report.Config.Version ?? "",
            Status = report.Status,
            CpuUsage = report.PerformanceCounter.Cpu,
            MemoryUsage = report.PerformanceCounter.Memory
        };
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
        var result = await _dialog.ShowConfirmAsync(
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
        var result = await _dialog.ShowCountdownConfirmAsync(
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

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var instance in FilteredInstances)
            instance.IsSelected = true;
        SelectedCount = FilteredInstances.Count;
    }

    [RelayCommand]
    private void CancelSelection()
    {
        foreach (var instance in AllInstances)
            instance.IsSelected = false;
        SelectedCount = 0;
    }

    public void OnSelectionChanged(InstanceCardModel instance)
    {
        SelectedCount = AllInstances.Count(i => i.IsSelected);
    }

    [RelayCommand]
    private async Task BatchStartAsync()
    {
        var selected = AllInstances.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["BatchStartConfirmTitle"],
            string.Format(Lang.Tr["BatchStartConfirmContent"], selected.Count),
            Lang.Tr["Start"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        int success = 0, fail = 0;
        foreach (var card in selected)
        {
            try
            {
                var daemon = await _daemonService.GetAsync(card.DaemonConfig);
                if (daemon != null) { await daemon.StartInstanceAsync(card.InstanceId); success++; }
                else fail++;
            }
            catch (Exception ex) { Log.Error(ex, "[InstanceManager] Batch start failed for {0}", card.InstanceId); fail++; }
        }

        _notification.Push(Lang.Tr["BatchStartComplete"],
            string.Format(Lang.Tr["BatchOperationResult"], success, fail), false,
            fail > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        CancelSelection();
        await Task.Delay(2000);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task BatchStopAsync()
    {
        var selected = AllInstances.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["BatchStopConfirmTitle"],
            string.Format(Lang.Tr["BatchStopConfirmContent"], selected.Count),
            Lang.Tr["Stop"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        int success = 0, fail = 0;
        foreach (var card in selected)
        {
            try
            {
                var daemon = await _daemonService.GetAsync(card.DaemonConfig);
                if (daemon != null) { await daemon.StopInstanceAsync(card.InstanceId); success++; }
                else fail++;
            }
            catch (Exception ex) { Log.Error(ex, "[InstanceManager] Batch stop failed for {0}", card.InstanceId); fail++; }
        }

        _notification.Push(Lang.Tr["BatchStopComplete"],
            string.Format(Lang.Tr["BatchOperationResult"], success, fail), false,
            fail > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        CancelSelection();
        await Task.Delay(2000);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var selected = AllInstances.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = await _dialog.ShowCountdownConfirmAsync(
            Lang.Tr["BatchDeleteConfirmTitle"],
            string.Format(Lang.Tr["BatchDeleteConfirmContent"], selected.Count),
            Lang.Tr["Delete"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        int success = 0, fail = 0;
        foreach (var card in selected)
        {
            try
            {
                var daemon = await _daemonService.GetAsync(card.DaemonConfig);
                if (daemon != null) { await daemon.RemoveInstanceAsync(card.InstanceId); success++; }
                else fail++;
            }
            catch (Exception ex) { Log.Error(ex, "[InstanceManager] Batch delete failed for {0}", card.InstanceId); fail++; }
        }

        _notification.Push(Lang.Tr["BatchDeleteComplete"],
            string.Format(Lang.Tr["BatchOperationResult"], success, fail), false,
            fail > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        CancelSelection();
        await Task.Delay(1000);
        await RefreshAsync();
    }
}
