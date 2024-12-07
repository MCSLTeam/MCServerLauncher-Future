using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
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
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    DaemonCardContainer.Items.Clear();
                    if (DaemonsListManager.Get.Count > 0)
                    {
                        foreach (DaemonsListManager.DaemonConfigModel daemon in DaemonsListManager.Get)
                        {
                            DaemonCard daemonCard = new DaemonCard
                            {
                                Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}",
                                IsSecure = daemon.IsSecure,
                                EndPoint = daemon.EndPoint,
                                Port = daemon.Port,
                                Username = daemon.Username,
                                Password = daemon.Password,
                                FriendlyName = daemon.FriendlyName ?? LanguageManager.Localize["Main_DaemonManagerNavMenu"],
                            };
                            DaemonCardContainer.Items.Add(daemonCard);
                            daemonCard.ConnectDaemon();
                        }
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
                isSecure: newDaemonConnectionInput.SecureWebSocketCheckBox.IsChecked == true,
                user: newDaemonConnectionInput.userEdit.Text,
                pwd: newDaemonConnectionInput.pwdEdit.Password,
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

        private async Task EditDaemonConnection(string endPoint, int port, bool isSecure, string user, string pwd, string friendlyName, DaemonCard daemon)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog(endPoint, port.ToString() ?? "", isSecure, user, pwd, friendlyName, isRetrying: true);
            dialog.PrimaryButtonClick += (o, args) =>
            {
                DaemonCardContainer.Items.Remove(daemon);
                TryConnectDaemon(
                    endPoint: newDaemonConnectionInput.wsEdit.Text,
                    port: newDaemonConnectionInput.portEdit.Text,
                    isSecure: newDaemonConnectionInput.SecureWebSocketCheckBox.IsChecked == true,
                    user: newDaemonConnectionInput.userEdit.Text,
                    pwd: newDaemonConnectionInput.pwdEdit.Password,
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

        private async void TryConnectDaemon(string endPoint, string port, string user, string pwd, bool isSecure, string friendlyName)
        {
            try
            {
                int IntPort = int.Parse(port);
            }
            catch (FormatException)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(pwd))
            {
                return;
            }
            DaemonCard daemon = new() { EndPoint = endPoint, Port = int.Parse(port), IsSecure = isSecure, Username = user, Password = pwd, Status = "ing", FriendlyName = friendlyName };
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
                        Username = user,
                        Password = pwd,
                        IsSecure = isSecure
                    }
                );
            }
            else
            {
                DaemonCardContainer.Items.Remove(daemon);
                await EditDaemonConnection(endPoint, int.Parse(port), isSecure, user, pwd, friendlyName, daemon);
            }
        }
    }
}