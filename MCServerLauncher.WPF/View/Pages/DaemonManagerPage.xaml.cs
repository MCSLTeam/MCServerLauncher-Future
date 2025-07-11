using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    DaemonManagerPage.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonManagerPage
    {
        public DaemonManagerPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    DaemonCardContainer.Items.Clear();
                    if (DaemonsListManager.Get.Count > 0)
                    {
                        var connectionTasks = new List<Task>();
                        foreach (DaemonsListManager.DaemonConfigModel daemon in DaemonsListManager.Get)
                        {
                            DaemonCard daemonCard = new DaemonCard
                            {
                                Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}",
                                IsSecure = daemon.IsSecure,
                                EndPoint = daemon.EndPoint,
                                Port = daemon.Port,
                                Token = daemon.Token,
                                FriendlyName = daemon.FriendlyName ?? LanguageManager.Localize["Main_DaemonManagerNavMenu"],
                                Status = "ing",
                            };
                            DaemonCardContainer.Items.Add(daemonCard);
                            connectionTasks.Add(daemonCard.ConnectDaemon());
                        }
                        await Task.WhenAll(connectionTasks);
                    }
                }
            };
        }

        private async void AddDaemonConnection(object sender, RoutedEventArgs e)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog();
            dialog.PrimaryButtonClick += (o, args) => TryConnectDaemon(
                endPoint: newDaemonConnectionInput.wsEdit.Text,
                port: newDaemonConnectionInput.portEdit.Text,
                isSecure: newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://",
                token: newDaemonConnectionInput.tokenEdit.Password,
                friendlyName: newDaemonConnectionInput.friendlyNameEdit.Text
            );
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async Task EditDaemonConnection(string endPoint, int port, bool isSecure, string token, string friendlyName, DaemonCard daemon)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog(endPoint, port.ToString() ?? "", isSecure, token, friendlyName, isRetrying: true);
            dialog.PrimaryButtonClick += (o, args) =>
            {
                DaemonCardContainer.Items.Remove(daemon);
                TryConnectDaemon(
                    endPoint: newDaemonConnectionInput.wsEdit.Text,
                    port: newDaemonConnectionInput.portEdit.Text,
                    isSecure: newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://",
                    token: newDaemonConnectionInput.tokenEdit.Password,
                    friendlyName: newDaemonConnectionInput.friendlyNameEdit.Text
                );
            };
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async void TryConnectDaemon(string endPoint, string port, string token, bool isSecure, string friendlyName)
        {
            try
            {
                int IntPort = int.Parse(port);
            }
            catch (FormatException)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
            {
                return;
            }
            DaemonCard daemon = new() { EndPoint = endPoint, Port = int.Parse(port), IsSecure = isSecure, Token = token, Status = "ing", FriendlyName = friendlyName };
            daemon.Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}";
            DaemonCardContainer.Items.Add(daemon);
            if (await daemon.ConnectDaemon() is true)
            {
                DaemonsListManager.AddDaemon(
                    new DaemonsListManager.DaemonConfigModel
                    {
                        FriendlyName = friendlyName,
                        EndPoint = endPoint,
                        Port = int.Parse(port),
                        Token = token,
                        IsSecure = isSecure
                    }
                );
            }
            else
            {
                DaemonCardContainer.Items.Remove(daemon);
                await EditDaemonConnection(endPoint, int.Parse(port), isSecure, token, friendlyName, daemon);
            }
        }
    }
}