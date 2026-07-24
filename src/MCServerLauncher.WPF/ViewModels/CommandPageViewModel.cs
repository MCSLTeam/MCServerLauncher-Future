using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Serilog;
using System.Windows;

namespace MCServerLauncher.WPF.ViewModels;

public partial class CommandPageViewModel : ObservableObject
{
    private readonly INotificationService _notification;
    private readonly IDialogService _dialog;

    [ObservableProperty] private string _commandText = string.Empty;
    [ObservableProperty] private InstanceStatus _status = InstanceStatus.Stopped;

    public CommandPageViewModel(INotificationService notification, IDialogService dialog)
    {
        _notification = notification;
        _dialog = dialog;
        InstanceDataManager.Instance.ReportUpdated += OnReportUpdated;
        if (InstanceDataManager.Instance.CurrentReport is { } report)
        {
            Status = report.Status;
        }
    }

    public void Dispose()
    {
        InstanceDataManager.Instance.ReportUpdated -= OnReportUpdated;
    }

    public bool CanSendCommand => Status == InstanceStatus.Running;
    public bool CanStart => Status is InstanceStatus.Stopped or InstanceStatus.Crashed;
    public bool CanStop => Status is InstanceStatus.Running or InstanceStatus.Starting;
    public bool CanRestart => Status is InstanceStatus.Running or InstanceStatus.Starting;
    public bool CanKill => Status is InstanceStatus.Running or InstanceStatus.Starting or InstanceStatus.Stopping;

    partial void OnStatusChanged(InstanceStatus value)
    {
        OnPropertyChanged(nameof(CanSendCommand));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanKill));
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        var command = CommandText.Trim();
        if (string.IsNullOrWhiteSpace(command)) return;
        if (!CanSendCommand)
        {
            PushActionUnavailable("ConsoleCommand_SendUnavailable");
            return;
        }

        try
        {
            await InstanceDataManager.Instance.SendCommandAsync(command);
            CommandText = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandPage] Failed to send command: {0}", command);
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["SendCommandFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task StartInstanceAsync()
    {
        if (!CanStart)
        {
            PushActionUnavailable("InstanceCard_StartUnavailable");
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_StartConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_StartConfirmContent"], InstanceName),
            Lang.Tr["Start"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await InstanceDataManager.Instance.StartInstanceAsync();
            _notification.Push(Lang.Tr["Success"], Lang.Tr["StartCommandSentSuccess"], false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandPage] Failed to start instance");
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["InstanceCard_StartFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task StopInstanceAsync()
    {
        if (!CanStop)
        {
            PushActionUnavailable("InstanceCard_StopUnavailable");
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_StopConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_StopConfirmContent"], InstanceName),
            Lang.Tr["Stop"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await InstanceDataManager.Instance.StopInstanceAsync();
            _notification.Push(Lang.Tr["Success"], Lang.Tr["StopCommandSentSuccess"], false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandPage] Failed to stop instance");
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["InstanceCard_StopFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task KillInstanceAsync()
    {
        if (!CanKill)
        {
            PushActionUnavailable("InstanceCard_KillUnavailable");
            return;
        }

        var result = await _dialog.ShowCountdownConfirmAsync(
            Lang.Tr["InstanceCard_KillConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_KillConfirmContent"], InstanceName),
            Lang.Tr["Kill"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await InstanceDataManager.Instance.KillInstanceAsync();
            _notification.Push(Lang.Tr["Success"], Lang.Tr["KillCommandSentSuccess"], false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandPage] Failed to kill instance");
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["InstanceCard_KillFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task RestartInstanceAsync()
    {
        if (!CanRestart)
        {
            PushActionUnavailable("InstanceCard_RestartUnavailable");
            return;
        }

        var result = await _dialog.ShowConfirmAsync(
            Lang.Tr["InstanceCard_RestartConfirmTitle"],
            string.Format(Lang.Tr["InstanceCard_RestartConfirmContent"], InstanceName),
            Lang.Tr["Restart"], Lang.Tr["Cancel"]);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await InstanceDataManager.Instance.RestartInstanceAsync();
            _notification.Push(Lang.Tr["Success"], Lang.Tr["RestartCommandSentSuccess"], false, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CommandPage] Failed to restart instance");
            _notification.Push(Lang.Tr["Error"], string.Format(Lang.Tr["InstanceCard_RestartFailed"], ex.Message), true, iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error);
        }
    }

    private string InstanceName => InstanceDataManager.Instance.CurrentReport?.Config.Name
        ?? InstanceDataManager.Instance.InstanceId.ToString();

    private void OnReportUpdated(object? sender, InstanceReport? report)
    {
        if (report is null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Status = report.Status;
        }
        else
        {
            dispatcher.Invoke(() => Status = report.Status);
        }
    }

    private void PushActionUnavailable(string resourceKey)
    {
        _notification.Push(
            Lang.Tr["Warning"],
            string.Format(Lang.Tr[resourceKey], InstanceName),
            false,
            InfoBarSeverity.Warning);
    }
}
