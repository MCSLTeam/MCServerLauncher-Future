using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        if (DaemonsListManager.Get is not { Count: > 0 }) return;

        var connectionTasks = new List<Task>();
        foreach (var config in DaemonsListManager.Get)
        {
            var model = new DaemonCardModel
            {
                Config = config,
                Address = $"{(config.IsSecure ? "wss" : "ws")}://{config.EndPoint}:{config.Port}",
                FriendlyName = config.FriendlyName ?? Lang.Tr["Main_DaemonManagerNavMenu"],
                Status = "ing"
            };
            Daemons.Add(model);
            connectionTasks.Add(ConnectDaemonInternalAsync(model));
        }
        await Task.WhenAll(connectionTasks);
    }

    [RelayCommand]
    private async Task AddConnectionAsync()
    {
        (ContentDialog dialog, NewDaemonConnectionInput input) = await View.Components.Utils.ConstructConnectDaemonDialog();
        dialog.PrimaryButtonClick += (o, args) => TryConnectNewDaemon(
            endPoint: input.wsEdit.Text,
            port: input.portEdit.Text,
            isSecure: input.WebSocketScheme.SelectionBoxItem.ToString() == "wss://",
            token: input.tokenEdit.Password,
            friendlyName: input.friendlyNameEdit.Text
        );
        try { await dialog.ShowAsync(); }
        catch { }
    }

    [RelayCommand]
    private async Task EditDaemonAsync(DaemonCardModel daemon)
    {
        var originalConfig = daemon.Config;
        (ContentDialog dialog, NewDaemonConnectionInput input) = await View.Components.Utils.ConstructConnectDaemonDialog(
            originalConfig.EndPoint,
            originalConfig.Port.ToString(),
            originalConfig.IsSecure,
            originalConfig.Token,
            originalConfig.FriendlyName,
            isRetrying: false,
            isEditing: true
        );

        dialog.PrimaryButtonClick += async (o, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!int.TryParse(input.portEdit.Text, out int newPort))
                {
                    args.Cancel = true;
                    return;
                }

                string newEndPoint = input.wsEdit.Text;
                string newToken = input.tokenEdit.Password;
                bool newIsSecure = input.WebSocketScheme.SelectionBoxItem.ToString() == "wss://";
                string newFriendlyName = input.friendlyNameEdit.Text;

                if (string.IsNullOrWhiteSpace(newEndPoint) || string.IsNullOrWhiteSpace(newToken))
                {
                    args.Cancel = true;
                    return;
                }

                await _daemonService.RemoveAsync(originalConfig);
                DaemonsListManager.RemoveDaemon(originalConfig);
                Daemons.Remove(daemon);

                var newConfig = new Constants.DaemonConfigModel
                {
                    FriendlyName = newFriendlyName,
                    EndPoint = newEndPoint,
                    Port = newPort,
                    Token = newToken,
                    IsSecure = newIsSecure
                };

                var newModel = new DaemonCardModel
                {
                    Config = newConfig,
                    Address = $"{(newIsSecure ? "wss" : "ws")}://{newEndPoint}:{newPort}",
                    FriendlyName = newFriendlyName,
                    Status = "ing"
                };
                Daemons.Add(newModel);

                if (await ConnectDaemonInternalAsync(newModel))
                {
                    DaemonsListManager.AddDaemon(newConfig);
                }
                else
                {
                    Daemons.Remove(newModel);
                    args.Cancel = true;
                    await EditDaemonAsync(newModel);
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
            DaemonsListManager.RemoveDaemon(daemon.Config);
        }
        catch (Exception ex)
        {
            Log.Error($"[Daemon] Error occurred when deleting daemon: {ex}");
        }
    }

    private async void TryConnectNewDaemon(string endPoint, string port, string token, bool isSecure, string friendlyName)
    {
        if (!int.TryParse(port, out int intPort)) return;
        if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token)) return;

        var config = new Constants.DaemonConfigModel
        {
            FriendlyName = friendlyName,
            EndPoint = endPoint,
            Port = intPort,
            Token = token,
            IsSecure = isSecure
        };

        var model = new DaemonCardModel
        {
            Config = config,
            Address = $"{(isSecure ? "wss" : "ws")}://{endPoint}:{intPort}",
            FriendlyName = friendlyName,
            Status = "ing"
        };
        Daemons.Add(model);

        if (await ConnectDaemonInternalAsync(model))
        {
            DaemonsListManager.AddDaemon(config);
        }
        else
        {
            Daemons.Remove(model);
            await EditDaemonAsync(model);
        }
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

            model.Status = "ok";
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[Daemon] Error connecting to daemon({model.Address}): {e}");
            model.Status = "err";
            return false;
        }
    }
}
