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

        private Task<(ContentDialog, NewDaemonConnectionInput)> ConstructConnectDaemonDialog(string address = "", string? jwt = "")
        {
            NewDaemonConnectionInput newDaemonConnectionInput = new();
            newDaemonConnectionInput.wsEdit.Text = address;
            newDaemonConnectionInput.jwtEdit.Password = jwt;
            ContentDialog dialog = new()
            {
                Title = LanguageManager.Localize["ConnectDaemon"],
                PrimaryButtonText = LanguageManager.Localize["Connect"],
                SecondaryButtonText = LanguageManager.Localize["Cancel"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = newDaemonConnectionInput
            };
            return Task.FromResult<(ContentDialog, NewDaemonConnectionInput)>((dialog, newDaemonConnectionInput));
        }

        private async void AddDaemonConnection(object sender, RoutedEventArgs e)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await ConstructConnectDaemonDialog();
            dialog.PrimaryButtonClick += (o, args) => TryConnectDaemon(
                address: newDaemonConnectionInput.wsEdit.Text,
                jwt: newDaemonConnectionInput.jwtEdit.Password
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

        private async Task<RoutedEventHandler> EditDaemonConnection(object sender, RoutedEventArgs e, string address, string? jwt, string?friendlyName, DaemonBorder daemon)
        {
            (ContentDialog dialog, NewDaemonConnectionInput newDaemonConnectionInput) = await ConstructConnectDaemonDialog(address, jwt);
            dialog.PrimaryButtonClick += (o, args) =>
            {
                DaemonListView.Items.Remove(daemon);
                TryConnectDaemon(
                    address: newDaemonConnectionInput.wsEdit.Text,
                    jwt: newDaemonConnectionInput.jwtEdit.Password,
                    friendlyName: newDaemonConnectionInput.nameEdit.Text
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

        private void TryConnectDaemon(string address, string jwt, string friendlyName)
        {
            DaemonBorder daemon = new() { Address = address, JWT = jwt, Status = "ing", FriendlyName = friendlyName };
            daemon.ConnectionEditButton.Click += new RoutedEventHandler(async (sender, e) => await EditDaemonConnection(sender, e, daemon.Address, daemon.JWT, daemon.FriendlyName, daemon));
            DaemonListView.Items.Add(daemon);
            NextButton.IsEnabled = DaemonListView.Items.Count > 0;
        }

        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent?.GoWelcomeSetup();
        }
    }
}