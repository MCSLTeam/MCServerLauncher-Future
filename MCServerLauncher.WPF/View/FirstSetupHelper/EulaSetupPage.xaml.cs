using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    EulaSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class EulaSetupPage
    {
        public EulaSetupPage()
        {
            InitializeComponent();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    if (SettingsManager.Get?.App != null && SettingsManager.Get.App.IsAppEulaAccepted)
                    {
                        Log.Information("[Set] Eula already accepted, skipping.");
                        var parent = this.TryFindParent<FirstSetup>();
                        parent?.GoDaemonSetup();
                    }
                }
            };
        }

        /// <summary>
        ///    Refuse Eula, then close app.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RefuseEula(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new()
            {
                Title = Lang.Tr["AreYouSure"],
                PrimaryButtonText = Lang.Tr["NotNow"],
                SecondaryButtonText = Lang.Tr["Disagree"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = Lang.Tr["FirstSetup_EulaDisagreeTip"]
                }
            };
            dialog.SecondaryButtonClick += (o, args) => Application.Current.Shutdown();
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>
        ///    Accept Eula with a scroll checker.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void AcceptEula(object sender, RoutedEventArgs e)
        {
            var isEulaReadFinished = EulaScrollViewer.VerticalOffset == EulaScrollViewer.ScrollableHeight;
            var parent = this.TryFindParent<FirstSetup>();
            if (!isEulaReadFinished)
            {
                ContentDialog fakeFinishedDialog = new()
                {
                    Title = Lang.Tr["AreYouSure"],
                    PrimaryButtonText = Lang.Tr["OK"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWithOverflow,
                        Text = Lang.Tr["FirstSetup_FakeEulaAgreeTip"]
                    }
                };
                try
                {
                    await fakeFinishedDialog.ShowAsync();
                }
                catch (Exception)
                {
                    // ignored
                }

                return;
            }

            ContentDialog dialog = new()
            {
                Title = Lang.Tr["AreYouSure"],
                PrimaryButtonText = Lang.Tr["Agree"],
                SecondaryButtonText = Lang.Tr["NotNow"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = Lang.Tr["FirstSetup_EulaAgreeTip"]
                }
            };
            dialog.PrimaryButtonClick += (o, args) => parent?.GoDaemonSetup();
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