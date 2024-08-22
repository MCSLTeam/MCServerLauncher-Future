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
                Title = "确定？",
                PrimaryButtonText = "再想想",
                SecondaryButtonText = "拒绝",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = "若您拒绝用户协议，本软件将退出。"
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
                    Title = "你认真的吗 ?!",
                    PrimaryButtonText = "明白",
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.WrapWithOverflow,
                        Text = "请确保您已完整阅读后再点击同意。"
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
                Title = "确定？",
                PrimaryButtonText = "同意",
                SecondaryButtonText = "还没读完",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = "请确保您已完整阅读并同意用户协议后再继续使用本软件。"
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