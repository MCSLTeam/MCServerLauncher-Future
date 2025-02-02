using Downloader;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Clipboard = MCServerLauncher.WPF.Modules.Clipboard;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///    DownloadProgressItem.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadProgressItem
    {
        private DispatcherTimer UpdateUITimer;

        private long ReceivedBytes;
        private long TotalBytesToReceive;
        private double ProgressPercentage;
        private double BytesPerSecondSpeed;
        //private int ActiveChunks;
        public DownloadProgressItem()
        {
            InitializeComponent();

            DownloadConfig = new DownloadConfiguration
            {
                Timeout = 5000,
                ChunkCount = SettingsManager.Get.Download.ThreadCnt,
                ParallelDownload = true,
                RequestConfiguration =
                {
                    UserAgent = Network.CommonUserAgent
                }
            };
            DownloadServiceInstance = new DownloadService(DownloadConfig);
            DownloadServiceInstance.DownloadStarted += DownloadStartedHandler;
            DownloadServiceInstance.DownloadProgressChanged += OnDownloadProgressChanged;
            DownloadServiceInstance.DownloadFileCompleted += OnDownloadFileCompleted;
        }

        /// <summary>
        ///    Where to save the file.
        /// </summary>
        public string SavePath { get; set; }

        /// <summary>
        ///    Downloading file name.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        ///    Download url.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        ///     Download instances.
        /// </summary>
        public DownloadConfiguration DownloadConfig { get; set; }
        public DownloadService DownloadServiceInstance { get; set; }

        /// <summary>
        ///   Start download.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task StartDownload()
        {
            if (DownloadConfig is null) throw new ArgumentNullException();
            DownloadFileName.Text = FileName;
            PauseIconAndText.Visibility = Visibility.Visible;
            ContinueIconAndText.Visibility = Visibility.Hidden;
            await DownloadServiceInstance.DownloadFileTaskAsync(Url, SavePath + "\\" + FileName);
        }

        /// <summary>
        /// Handle the started event of the download service.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DownloadStartedHandler(object sender, DownloadStartedEventArgs e)

        {
            Dispatcher.Invoke(() =>
            {
                //DownloadProgressBar.Value
                UpdateUITimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                UpdateUITimer.Tick += UpdateUITick;
                UpdateUITimer.Start();
            });
        }

        /// <summary>
        /// Update UI tick control.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UpdateUITick(object sender, EventArgs e)
        {
            if (DownloadProgressBar is not null)
            {
                DownloadProgressBar.Value = ProgressPercentage;
                SpeedPerSecondLabel.Text = BytesPerSecondSpeed switch
                {
                    > 1024 * 1024 => $"{BytesPerSecondSpeed / 1024 / 1024:F2} MB/s",
                    > 1024 => $"{BytesPerSecondSpeed / 1024:F2} KB/s",
                    _ => $"{BytesPerSecondSpeed:F2} B/s"
                };
                //ChunkCountLabel.Text = $"{ActiveChunks} {LanguageManager.Localize["Thread"]}";
                ChunkCountLabel.Text = "";
                if (TotalBytesToReceive > 0)
                {
                    FileSizeStatusLabel.Text = $"{ReceivedBytes / 1024.00 / 1024.00:F2} MB / {TotalBytesToReceive / 1024.00 / 1024.00:F2} MB";
                }
            }
        }

        /// <summary>
        /// Handle the download progress ui changed event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            ReceivedBytes = e.ReceivedBytesSize;
            TotalBytesToReceive = e.TotalBytesToReceive;
            ProgressPercentage = e.ProgressPercentage;
            BytesPerSecondSpeed = e.BytesPerSecondSpeed;
            //ActiveChunks = e.ActiveChunks;
        }

        /// <summary>
        /// Handle the download file completed event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                UpdateUITimer?.Stop();
            }
            catch { Console.WriteLine("Stop UITimer Failed"); }
            if (e.Cancelled)
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadHistoryFlyoutContent.Instance.DownloadsContainer.Children.Remove(this);
                    Notification.Push(
                        title: LanguageManager.Localize["DownloadCancelled"],
                        message: $"{FileName} {LanguageManager.Localize["DownloadCancelled"]}",
                        isClosable: true,
                        severity: InfoBarSeverity.Warning
                    );
                });
                return;
            }
            if (e.Error is not null)
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadHistoryFlyoutContent.Instance.DownloadsContainer.Children.Remove(this);
                    Notification.Push(
                        title: LanguageManager.Localize["DownloadFailed"],
                        message: $"{FileName} {LanguageManager.Localize["DownloadFailed"]}\n{e.Error}",
                        isClosable: true,
                        severity: InfoBarSeverity.Error
                    );
                });
                return;
            }
            Dispatcher.Invoke(() =>
            {
                DownloadHistoryFlyoutContent.Instance.DownloadsContainer.Children.Remove(this);
                Notification.Push(
                    title: LanguageManager.Localize["DownloadFinished"],
                    message: $"{FileName} {LanguageManager.Localize["DownloadFinished"]}",
                    isClosable: true,
                    severity: InfoBarSeverity.Success
                );
            });
        }

        /// <summary>
        /// Copy download url to clipboard.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CopyDownloadUrl(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Url);
        }

        /// <summary>
        /// Pause or continue download.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PauseOrContinueDownload(object sender, RoutedEventArgs e)
        {
            // THIS IS A MONKEY PATCH
            if (DownloadServiceInstance.IsPaused)
            {
                PauseIconAndText.Visibility = Visibility.Visible;
                ContinueIconAndText.Visibility = Visibility.Hidden;
                DownloadServiceInstance.Resume();
            }
            else
            {
                ContinueIconAndText.Visibility = Visibility.Visible;
                PauseIconAndText.Visibility = Visibility.Hidden;
                DownloadServiceInstance.Pause();
            }
        }

        /// <summary>
        /// Cancel download.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelDownload(object sender, RoutedEventArgs e)
        {
            DownloadServiceInstance.CancelAsync();
            Thread.Sleep(1000);
            try
            {
                File.Delete(SavePath + "\\" + FileName);
            }
            catch { }
        }
    }
}