using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    DaemonSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonSetupPage : Page
    {
        public DaemonSetupPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    if (DaemonsListManager.Get.Count > 0)
                    {
                        if (SettingsManager.Get?.App != null && SettingsManager.Get.App.IsFirstSetupFinished) return;
                        DaemonListView.Items.Clear();
                        foreach (var daemon in DaemonsListManager.Get)
                        {
                            TryConnectDaemon(
                                daemon.EndPoint,
                                daemon.Port.ToString(),
                                daemon.Token,
                                daemon.IsSecure,
                                daemon.FriendlyName
                            );
                        }
                    }
                }
            };
        }

        /// <summary>
        ///    Skip adding daemons.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Skip(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            ContentDialog dialog = new()
            {
                Title = LanguageManager.Localize["AreYouSure"],
                PrimaryButtonText = LanguageManager.Localize["TempSkip"],
                SecondaryButtonText = LanguageManager.Localize["Back"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = LanguageManager.Localize["FirstSetup_SkipConnectDaemonTip"]
                }
            };
            dialog.PrimaryButtonClick += (o, args) => parent?.GoWelcomeSetup();
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
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

        private async Task<RoutedEventHandler> EditDaemonConnection(object sender, RoutedEventArgs e, string endPoint, int port, bool isSecure, string token, string friendlyName, DaemonSetupCard daemon)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog(endPoint, port.ToString() ?? "", isSecure, token, friendlyName);
            dialog.PrimaryButtonClick += (o, args) =>
            {
                DaemonListView.Items.Remove(daemon);
                TryConnectDaemon(
                    endPoint: newDaemonConnectionInput.wsEdit.Text,
                    port: newDaemonConnectionInput.portEdit.Text,
                    isSecure: newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://" ,
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
            return (o, args) => { };
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
            DaemonSetupCard daemon = new() { EndPoint = endPoint, Port = int.Parse(port), IsSecure = isSecure, Token = token, Status = "ing", FriendlyName = friendlyName };
            daemon.Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}";
            DaemonListView.Items.Add(daemon);
            daemon.ConnectionEditButton.Click += new RoutedEventHandler(async (sender, e) => await EditDaemonConnection(sender, e, daemon.EndPoint, daemon.Port, daemon.IsSecure, daemon.Token, daemon.FriendlyName, daemon));
            NextButton.IsEnabled = DaemonListView.Items.Count > 0;
            await daemon.ConnectDaemon();
        }

        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent?.GoWelcomeSetup();
        }
    }
}