using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    DaemonSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonSetupPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        private const string LocalDaemonDownloadUrl = "";

        public DaemonSetupPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible && RemoteSetupPanel.Visibility == Visibility.Visible)
                {
                    await LoadExistingDaemonsAsync();
                }
            };
        }

        private async void UseLocalDaemon(object sender, RoutedEventArgs e)
        {
            await DownloadLocalDaemonAsync();
        }

        private async void UseRemoteDaemon(object sender, RoutedEventArgs e)
        {
            await ShowRemoteSetupAsync();
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
                Title = Lang.Tr["AreYouSure"],
                PrimaryButtonText = Lang.Tr["TempSkip"],
                SecondaryButtonText = Lang.Tr["Back"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = Lang.Tr["FirstSetup_SkipConnectDaemonTip"]
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
            await AddRemoteDaemonConnectionAsync();
        }

        private async Task AddRemoteDaemonConnectionAsync()
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog();
            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && await TryConnectDaemon(
                        endPoint: newDaemonConnectionInput.wsEdit.Text,
                        port: newDaemonConnectionInput.portEdit.Text,
                        isSecure: newDaemonConnectionInput.WebSocketScheme.SelectionBoxItem.ToString() == "wss://",
                        token: newDaemonConnectionInput.tokenEdit.Password,
                        friendlyName: newDaemonConnectionInput.friendlyNameEdit.Text
                    ))
                {
                    await AskAddAnotherHostAsync();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async Task DownloadLocalDaemonAsync()
        {
            if (string.IsNullOrWhiteSpace(LocalDaemonDownloadUrl))
            {
                ContentDialog unavailableDialog = new()
                {
                    Title = Lang.Tr["FirstSetup_DaemonLocalDownload"],
                    PrimaryButtonText = Lang.Tr["OK"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWithOverflow,
                        Text = Lang.Tr["FirstSetup_DaemonLocalDownloadUnavailable"]
                    }
                };

                try
                {
                    await unavailableDialog.ShowAsync();
                }
                catch (Exception)
                {
                    // ignored
                }

                await AskAddRemoteHostAfterLocalAsync();
                return;
            }

            var targetPath = Path.Combine(AppContext.BaseDirectory, "daemon.exe");
            using HttpClient httpClient = new();
            await using var sourceStream = await httpClient.GetStreamAsync(LocalDaemonDownloadUrl);
            await using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);
            NextButton.IsEnabled = true;
            await AskAddRemoteHostAfterLocalAsync();
        }

        private async Task<RoutedEventHandler> EditDaemonConnection(object sender, RoutedEventArgs e, string endPoint, int port, bool isSecure, string token, string friendlyName, DaemonSetupCard daemon)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await Utils.ConstructConnectDaemonDialog(endPoint, port.ToString() ?? "", isSecure, token, friendlyName);
            dialog.PrimaryButtonClick += async (o, args) =>
            {
                DaemonListView.Items.Remove(daemon);
                await TryConnectDaemon(
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

        private async Task<bool> TryConnectDaemon(string endPoint, string port, string token, bool isSecure, string friendlyName)
        {
            try
            {
                int IntPort = int.Parse(port);
            }
            catch (FormatException)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(endPoint) || string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }
            DaemonSetupCard daemon = new() { EndPoint = endPoint, Port = int.Parse(port), IsSecure = isSecure, Token = token, Status = "ing", FriendlyName = friendlyName };
            daemon.Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}";
            DaemonListView.Items.Add(daemon);
            daemon.ConnectionEditButton.Click += new RoutedEventHandler(async (sender, e) => await EditDaemonConnection(sender, e, daemon.EndPoint, daemon.Port, daemon.IsSecure, daemon.Token, daemon.FriendlyName, daemon));
            NextButton.IsEnabled = DaemonListView.Items.Count > 0;
            return await daemon.ConnectDaemon();
        }

        private async Task LoadExistingDaemonsAsync()
        {
            if (DaemonsListManager.Get is not { Count: > 0 } daemons)
            {
                DaemonListView.Items.Clear();
                NextButton.IsEnabled = false;
                return;
            }

            var parent = this.TryFindParent<FirstSetup>();
            if (SettingsManager.Get?.App != null
                && SettingsManager.Get.App.IsFirstSetupFinished
                && parent?.IsDebugSession != true)
            {
                return;
            }

            DaemonListView.Items.Clear();
            var isDebugSession = parent?.IsDebugSession == true;
            foreach (var daemonConfig in daemons)
            {
                var card = CreateDaemonCard(daemonConfig);
                DaemonListView.Items.Add(card);
                if (isDebugSession)
                {
                    card.Status = GetExistingDaemon(daemonConfig)?.ConnectionState ==
                        MCServerLauncher.DaemonClient.DaemonConnectionState.Ready
                        ? "ok"
                        : "err";
                    continue;
                }

                var result = await DaemonsWsManager.Get(daemonConfig);
                card.Status = result.IsOk(out var daemon) &&
                    daemon!.ConnectionState == MCServerLauncher.DaemonClient.DaemonConnectionState.Ready
                    ? "ok"
                    : "err";
            }

            NextButton.IsEnabled = DaemonListView.Items.Count > 0;
        }

        private DaemonSetupCard CreateDaemonCard(Constants.DaemonConfigModel daemonConfig)
        {
            var card = new DaemonSetupCard
            {
                EndPoint = daemonConfig.EndPoint ?? string.Empty,
                Port = daemonConfig.Port,
                IsSecure = daemonConfig.IsSecure,
                Token = daemonConfig.Token ?? string.Empty,
                Status = "ing",
                FriendlyName = daemonConfig.FriendlyName ?? string.Empty
            };

            card.Address = $"{(card.IsSecure ? "wss" : "ws")}://{card.EndPoint}:{card.Port}";
            card.ConnectionEditButton.Click += new RoutedEventHandler(async (sender, e) =>
                await EditDaemonConnection(sender, e, card.EndPoint, card.Port, card.IsSecure, card.Token, card.FriendlyName, card));

            return card;
        }

        private static MCServerLauncher.DaemonClient.DaemonClient? GetExistingDaemon(Constants.DaemonConfigModel daemonConfig)
        {
            return DaemonsWsManager.TryGetExisting(daemonConfig, out var daemon) ? daemon : null;
        }

        private async Task AskAddAnotherHostAsync()
        {
            var parent = this.TryFindParent<FirstSetup>();
            ContentDialog dialog = new()
            {
                Title = Lang.Tr["FirstSetup_DaemonAddAnotherHostTitle"],
                PrimaryButtonText = Lang.Tr["FirstSetup_DaemonAddAnotherHost"],
                SecondaryButtonText = Lang.Tr["FirstSetup_DaemonFinishAdding"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = Lang.Tr["FirstSetup_DaemonAddAnotherHostTip"]
                }
            };
            dialog.SecondaryButtonClick += (o, args) => parent?.GoWelcomeSetup();
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async Task AskAddRemoteHostAfterLocalAsync()
        {
            var parent = this.TryFindParent<FirstSetup>();
            ContentDialog dialog = new()
            {
                Title = Lang.Tr["FirstSetup_DaemonAddAnotherHostTitle"],
                PrimaryButtonText = Lang.Tr["FirstSetup_DaemonAddAnotherHost"],
                SecondaryButtonText = Lang.Tr["FirstSetup_DaemonFinishAdding"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = Lang.Tr["FirstSetup_DaemonAddAnotherHostTip"]
                }
            };

            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await ShowRemoteSetupAsync();
                    return;
                }

                parent?.GoWelcomeSetup();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private async Task ShowRemoteSetupAsync()
        {
            LocalChoicePanel.Visibility = Visibility.Collapsed;
            RemoteSetupPanel.Visibility = Visibility.Visible;
            DaemonActionButtonContent.Icon = SegoeFluentIcons.ConnectApp;
            DaemonActionButtonContent.Content = Lang.Tr["ConnectDaemon"];
            await LoadExistingDaemonsAsync();
        }

        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent?.GoWelcomeSetup();
        }
    }
}
