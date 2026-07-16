using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.View.Components.Generic;
using iNKORE.UI.WPF.Modern.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Modules
{
    public class DownloadManager
    {
        public static List<string?> SequenceMinecraftVersion(List<string>? originalList)
        {
            return McVersionSequencer.Sequence(originalList);
        }

        /// <summary>
        /// 资源下载：可选本机和/或一个或多个守护进程（推送到 caches/downloads/）。
        /// </summary>
        public async Task TriggerPreDownloadFile(string downloadUrl, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("Download URL is empty.", nameof(downloadUrl));
            if (string.IsNullOrWhiteSpace(defaultFileName))
                throw new ArgumentException("Download file name is empty.", nameof(defaultFileName));

            var choice = await ShowDestinationDialogAsync(defaultFileName);
            if (choice is null) return;

            string? localDir = null;
            string? localName = null;

            if (choice.SaveLocal)
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "All Files (*.*)|*.*",
                    FileName = defaultFileName
                };
                if (saveFileDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(saveFileDialog.FileName))
                {
                    localName = Path.GetFileName(saveFileDialog.FileName);
                    localDir = Path.GetDirectoryName(saveFileDialog.FileName);
                }
                else if (choice.DaemonConfigs.Count == 0)
                {
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(localDir) && !string.IsNullOrWhiteSpace(localName))
            {
                await PreDownloadFile(downloadUrl, localDir, localName);
            }

            if (choice.DaemonConfigs.Count == 0) return;

            var tempPath = Path.Combine(Path.GetTempPath(), "mcsl-res-" + Guid.NewGuid().ToString("N") + "-" + defaultFileName);
            try
            {
                await DownloadUrlToFileAsync(downloadUrl, tempPath);
                var fileName = Path.GetFileName(defaultFileName);
                var daemonPath = $"caches/downloads/{fileName}";

                foreach (var config in choice.DaemonConfigs)
                {
                    try
                    {
                        var daemon = await DaemonsWsManager.Get(config);
                        if (daemon is null)
                        {
                            Log.Warning("[Download] Daemon offline, skip: {0}", config.FriendlyName ?? config.EndPoint);
                            continue;
                        }

                        var uploadContext = await daemon.UploadFileAsync(tempPath, daemonPath, 1024 * 1024);
                        if (uploadContext.NetworkLoadTask != null)
                            await uploadContext.NetworkLoadTask;

                        Log.Information("[Download] Pushed {0} to {1} as {2} done={3}",
                            fileName, config.FriendlyName ?? config.EndPoint, daemonPath, uploadContext.Done);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Download] Push to daemon failed");
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private sealed class DestinationChoice
        {
            public bool SaveLocal { get; init; }
            public List<Constants.DaemonConfigModel> DaemonConfigs { get; init; } = new();
        }

        private static async Task<DestinationChoice?> ShowDestinationDialogAsync(string fileName)
        {
            var daemons = DaemonsListManager.Get ?? new List<Constants.DaemonConfigModel>();
            var localCheck = new CheckBox
            {
                Content = "本机 / This device",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var list = new StackPanel { Orientation = Orientation.Vertical };
            var daemonChecks = new List<(CheckBox Box, Constants.DaemonConfigModel Config)>();
            foreach (var config in daemons)
            {
                var label = string.IsNullOrWhiteSpace(config.FriendlyName)
                    ? $"{config.EndPoint}:{config.Port}"
                    : $"{config.FriendlyName} ({config.EndPoint}:{config.Port})";
                var box = new CheckBox
                {
                    Content = label,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                list.Children.Add(box);
                daemonChecks.Add((box, config));
            }

            if (daemons.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "尚未添加守护进程。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
            }

            var root = new StackPanel { Margin = new Thickness(4) };
            root.Children.Add(new TextBlock
            {
                Text = $"将「{fileName}」保存到本机，和/或推送到一个或多个守护进程（默认 caches/downloads/）。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 420
            });
            root.Children.Add(localCheck);
            root.Children.Add(new TextBlock
            {
                Text = "守护进程",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 4)
            });
            root.Children.Add(list);

            var dialog = new ContentDialog
            {
                Title = "选择下载位置",
                Content = new ScrollViewer
                {
                    Content = root,
                    MaxHeight = 360,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "开始下载",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return null;

            var selected = daemonChecks.Where(x => x.Box.IsChecked == true).Select(x => x.Config).ToList();
            var saveLocal = localCheck.IsChecked == true;
            if (!saveLocal && selected.Count == 0) return null;

            return new DestinationChoice
            {
                SaveLocal = saveLocal,
                DaemonConfigs = selected
            };
        }

        private static async Task DownloadUrlToFileAsync(string url, string destPath)
        {
            using var http = new HttpClient();
            await using var stream = await http.GetStreamAsync(url);
            await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs);
        }

        private async Task PreDownloadFile(string url, string savePath, string fileName)
        {
            DownloadProgressItem item = new()
            {
                SavePath = savePath,
                FileName = fileName,
                Url = url
            };
            DownloadHistoryFlyoutContent.Instance.DownloadsContainer.Children.Insert(0, item);
            Log.Information("[Download] Task added: {0} from {1}", Path.Combine(savePath, fileName), url);
            await item.StartDownload();
        }
    }
}
