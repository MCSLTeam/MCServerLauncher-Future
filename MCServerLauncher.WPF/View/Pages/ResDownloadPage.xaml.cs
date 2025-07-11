using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.ResDownloadProvider;
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage
    {
        public readonly FastMirrorProvider? FastMirror = new();
        public readonly MCSLSyncProvider? MCSLSync = new();
        public readonly MSLAPIProvider? MSLAPI = new();
        public readonly PolarsMirrorProvider? PolarsMirror = new();
        public readonly ZCloudFileProvider? ZCloudFile = new();

        public ResDownloadPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };
        }

        /// <summary>
        ///    Refresh current download provider
        /// </summary>
        public async void Refresh(object sender, RoutedEventArgs e)
        {
            ShowLoadingLayer();
            if (CurrentResDownloadProvider.Content is IResDownloadProvider provider)
            {
                if (SettingsManager.Get?.Download?.DownloadSource != provider.ResProviderName)
                {
                    IResDownloadProvider currentResDownloadProvider = ToggleResDownloadProvider();
                    await currentResDownloadProvider.Refresh();
                }
                else
                {
                    await provider.Refresh();
                }
            }
            else
            {
                IResDownloadProvider currentResDownloadProvider = ToggleResDownloadProvider();
                await currentResDownloadProvider.Refresh();
            }
        }

        private IResDownloadProvider ToggleResDownloadProvider()
        {
            IResDownloadProvider? currentResDownloadProvider = SettingsManager.Get?.Download?.DownloadSource switch
            {
                "FastMirror" => FastMirror,
                "PolarsMirror" => PolarsMirror,
                "ZCloudFile" => ZCloudFile,
                "MSLAPI" => MSLAPI,
                "MCSLSync" => MCSLSync,
                _ => null
            };
            Subtitle.Text = $"{Lang.Tr["ResDownloadTipPrefix"]} {currentResDownloadProvider!.ResProviderName} {Lang.Tr["ResDownloadTipSuffix"]}";
            CurrentResDownloadProvider.Content = currentResDownloadProvider;
            return currentResDownloadProvider;
        }

        public void ShowLoadingLayer()
        {
            LoadingLayer.Visibility = Visibility.Visible;
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeInAnimation);
        }

        public void HideLoadingLayer()
        {
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
            fadeOutAnimation.Completed += (s, e) =>
            {
                LoadingLayer.Visibility = Visibility.Hidden;
            };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }
    }
}