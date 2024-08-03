using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;
using System;
using System.Windows.Controls;
using MCServerLauncher.WPF.Helpers;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///     EulaSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class EulaSetupPage
    {
        public EulaSetupPage()
        {
            InitializeComponent();
        }
        private async void RefuseEula(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new()
            {
                Title = "确定？",
                PrimaryButtonText = "再想想",
                SecondaryButtonText = "拒绝",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock()
                {
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Text = "您拒绝了用户协议，本软件将无法继续使用。"
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
        private async void AcceptEula(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            ContentDialog dialog = new()
            {
                Title = "确定？",
                PrimaryButtonText = "同意",
                SecondaryButtonText = "还没读完",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = false,
                Content = new TextBlock()
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
