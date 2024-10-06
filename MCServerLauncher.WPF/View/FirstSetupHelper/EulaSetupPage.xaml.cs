using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
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
                Title = LanguageManager.Localize["AreYouSure"],
                PrimaryButtonText = LanguageManager.Localize["NotNow"],
                SecondaryButtonText = LanguageManager.Localize["Disagree"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = LanguageManager.Localize["FirstSetup_EulaDisagreeTip"]
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
                    Title = LanguageManager.Localize["AreYouSure"],
                    PrimaryButtonText = LanguageManager.Localize["OK"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWithOverflow,
                        Text = LanguageManager.Localize["FirstSetup_FakeEulaAgreeTip"]
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
                Title = LanguageManager.Localize["AreYouSure"],
                PrimaryButtonText = LanguageManager.Localize["Agree"],
                SecondaryButtonText = LanguageManager.Localize["NotNow"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = LanguageManager.Localize["FirstSetup_EulaAgreeTip"]
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