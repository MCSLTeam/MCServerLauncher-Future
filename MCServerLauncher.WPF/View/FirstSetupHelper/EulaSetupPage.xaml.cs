using System;
using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Helpers;

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
                Title = LanguageManager.Instance["AreYouSure"],
                PrimaryButtonText = "再想想",
                SecondaryButtonText = LanguageManager.Instance["Disagree"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = LanguageManager.Instance["EulaDisagreeTip"]
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
                    Title = LanguageManager.Instance["AreYouSure"],
                    PrimaryButtonText = LanguageManager.Instance["OK"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWithOverflow,
                        Text = LanguageManager.Instance["FakeEulaAgreeTip"]
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
                Title = LanguageManager.Instance["AreYouSure"],
                PrimaryButtonText = LanguageManager.Instance["Agree"],
                SecondaryButtonText = LanguageManager.Instance["NotNow"],
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = LanguageManager.Instance["EulaAgreeTip"]
                }
            };
            dialog.PrimaryButtonClick += (o, args) => parent.GoDaemonSetup();
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