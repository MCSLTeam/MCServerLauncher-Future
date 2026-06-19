using Downloader;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System;
using System.ComponentModel;
using System.IO;
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
        private DispatcherTimer? UpdateUITimer;
        private bool _isServiceDisposed;

        private long ReceivedBytes;
        private long TotalBytesToReceive;
        private double ProgressPercentage;
        private double BytesPerSecondSpeed;
        //private int ActiveChunks;
        public DownloadProgressItem()
        {
            InitializeComponent();
            var downloadSettings = SettingsManager.Get?.Download;

            DownloadConfig = new DownloadConfiguration
            {
                Timeout = 5000,
                ChunkCount = downloadSettings?.ThreadCnt ?? 4,
                ParallelDownload = true,
                RequestConfiguration =
                {
                        UserAgent = Common.Network.HttpHelper.UserAgent
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
        public string SavePath { get; set; } = string.Empty;

        /// <summary>
        ///    Downloading file name.
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        ///    Download url.
        /// </summary>
        public string Url { get; set; } = string.Empty;

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
            if (string.IsNullOrWhiteSpace(Url)) throw new InvalidOperationException("Download URL is empty.");
            if (string.IsNullOrWhiteSpace(FileName)) throw new InvalidOperationException("Download file name is empty.");
            if (string.IsNullOrWhiteSpace(SavePath)) throw new InvalidOperationException("Download save path is empty.");

            DownloadFileName.Text = FileName;
            PauseIconAndText.Visibility = Visibility.Visible;
            ContinueIconAndText.Visibility = Visibility.Hidden;
            try
            {
                await DownloadServiceInstance.DownloadFileTaskAsync(Url, Path.Combine(SavePath, FileName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Dispatcher.Invoke(() =>
                {
                    RemoveFromDownloadHistory();
                    Notification.Push(
                        title: Lang.Tr["DownloadFailed"],
                        message: $"{FileName} {Lang.Tr["DownloadFailed"]}\n{exception.Message}",
                        isClosable: true,
                        severity: InfoBarSeverity.Error
                    );
                });
                CleanupDownloadService();
            }
        }

        /// <summary>
        /// Handle the started event of the download service.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DownloadStartedHandler(object? sender, DownloadStartedEventArgs e)

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
        private void UpdateUITick(object? sender, EventArgs e)
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
                //ChunkCountLabel.Text = $"{ActiveChunks} {Lang.Tr["Thread"]}";
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
        private void OnDownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs e)
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
        private void OnDownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            StopUiTimer();
            if (e.Cancelled)
            {
                Dispatcher.Invoke(() =>
                {
                    RemoveFromDownloadHistory();
                    Notification.Push(
                        title: Lang.Tr["DownloadCancelled"],
                        message: $"{FileName} {Lang.Tr["DownloadCancelled"]}",
                        isClosable: true,
                        severity: InfoBarSeverity.Warning
                    );
                });
                CleanupDownloadService();
                return;
            }
            if (e.Error is not null)
            {
                Dispatcher.Invoke(() =>
                {
                    RemoveFromDownloadHistory();
                    Notification.Push(
                        title: Lang.Tr["DownloadFailed"],
                        message: $"{FileName} {Lang.Tr["DownloadFailed"]}\n{e.Error}",
                        isClosable: true,
                        severity: InfoBarSeverity.Error
                    );
                });
                CleanupDownloadService();
                return;
            }
            Dispatcher.Invoke(() =>
            {
                RemoveFromDownloadHistory();
                Notification.Push(
                    title: Lang.Tr["DownloadFinished"],
                    message: $"{FileName} {Lang.Tr["DownloadFinished"]}",
                    isClosable: true,
                    severity: InfoBarSeverity.Success
                );
            });
            CleanupDownloadService();
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
            try
            {
                File.Delete(Path.Combine(SavePath, FileName));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void StopUiTimer()
        {
            Dispatcher.Invoke(() =>
            {
                if (UpdateUITimer is null) return;
                UpdateUITimer.Stop();
                UpdateUITimer.Tick -= UpdateUITick;
                UpdateUITimer = null;
            });
        }

        private void RemoveFromDownloadHistory()
        {
            DownloadHistoryFlyoutContent.Instance.DownloadsContainer.Children.Remove(this);
        }

        private void CleanupDownloadService()
        {
            if (_isServiceDisposed) return;
            DownloadServiceInstance.DownloadStarted -= DownloadStartedHandler;
            DownloadServiceInstance.DownloadProgressChanged -= OnDownloadProgressChanged;
            DownloadServiceInstance.DownloadFileCompleted -= OnDownloadFileCompleted;
            DownloadServiceInstance.Dispose();
            _isServiceDisposed = true;
        }
    }
}
