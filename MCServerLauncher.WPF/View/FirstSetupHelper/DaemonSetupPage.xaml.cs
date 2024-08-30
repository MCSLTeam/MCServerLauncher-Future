using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System;
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
            dialog.PrimaryButtonClick += (o, args) => parent.GoWelcomeSetup();
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }
        
    }
}