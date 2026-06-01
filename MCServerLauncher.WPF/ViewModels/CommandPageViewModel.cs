using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using Serilog;

namespace MCServerLauncher.WPF.ViewModels;

public partial class CommandPageViewModel : ObservableObject
{
    private readonly INotificationService _notification;

    [ObservableProperty] private string _commandText = string.Empty;

    public CommandPageViewModel(INotificationService notification)
    {
        _notification = notification;
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        var command = CommandText.Trim();
        if (string.IsNullOrWhiteSpace(command)) return;

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
}
