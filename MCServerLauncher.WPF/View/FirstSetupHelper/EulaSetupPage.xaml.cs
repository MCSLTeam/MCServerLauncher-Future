using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    EulaSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class EulaSetupPage
    {
        private const int AcceptCountdownSeconds = 15;
        private readonly DispatcherTimer _acceptCountdownTimer;
        private int _acceptCountdownRemaining = AcceptCountdownSeconds;

        public EulaSetupPage()
        {
            InitializeComponent();
            _acceptCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _acceptCountdownTimer.Tick += AcceptCountdownTimerTick;
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

                    RefreshEulaUrl();
                    ResetAcceptCountdown();
                }
                else
                {
                    _acceptCountdownTimer.Stop();
                }
            };
        }

        internal static string GetEulaUrl(string? language)
        {
            return language switch
            {
                "en-US" => "https://future.mcsl.com.cn/en/eula.html",
                "ja-JP" => "https://future.mcsl.com.cn/ja/eula.html",
                "ru-RU" => "https://future.mcsl.com.cn/ru/eula.html",
                "zh-HK" or "zh-TW" => "https://future.mcsl.com.cn/zh-hant/eula.html",
                _ => "https://future.mcsl.com.cn/eula.html"
            };
        }

        private string CurrentEulaUrl => GetEulaUrl(SettingsManager.Get?.App?.Language);

        private void RefreshEulaUrl()
        {
            EulaUrlTextBlock.Text = CurrentEulaUrl;
        }

        private void ResetAcceptCountdown()
        {
            _acceptCountdownRemaining = AcceptCountdownSeconds;
            AcceptButton.IsEnabled = false;
            UpdateAcceptButtonText();
            _acceptCountdownTimer.Stop();
            _acceptCountdownTimer.Start();
        }

        private void AcceptCountdownTimerTick(object? sender, EventArgs e)
        {
            _acceptCountdownRemaining--;
            if (_acceptCountdownRemaining <= 0)
            {
                _acceptCountdownTimer.Stop();
                AcceptButton.IsEnabled = true;
                AcceptButtonContent.Content = Lang.Tr["Agree"];
                return;
            }

            UpdateAcceptButtonText();
        }

        private void UpdateAcceptButtonText()
        {
            AcceptButtonContent.Content = string.Format(
                Lang.Tr["FirstSetup_EulaContinueCountdown"],
                _acceptCountdownRemaining);
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
        ///    Open online EULA with the system browser.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenEulaInBrowser(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CurrentEulaUrl,
                UseShellExecute = true
            });
        }

        /// <summary>
        ///    Accept Eula.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void AcceptEula(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
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
