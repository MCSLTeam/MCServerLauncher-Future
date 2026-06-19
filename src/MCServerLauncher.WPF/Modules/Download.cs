using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.WPF.View.Components.Generic;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules
{
    public class DownloadManager
    {
        public static List<string?> SequenceMinecraftVersion(List<string>? originalList)
        {
            return McVersionSequencer.Sequence(originalList);
        }

        public async Task TriggerPreDownloadFile(string downloadUrl, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("Download URL is empty.", nameof(downloadUrl));
            if (string.IsNullOrWhiteSpace(defaultFileName))
                throw new ArgumentException("Download file name is empty.", nameof(defaultFileName));

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                FileName = defaultFileName
            };
            if (saveFileDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(saveFileDialog.FileName))
                return;

            var saveFileName = Path.GetFileName(saveFileDialog.FileName);
            var savePath = Path.GetDirectoryName(saveFileDialog.FileName);
            if (string.IsNullOrWhiteSpace(saveFileName) || string.IsNullOrWhiteSpace(savePath))
                return;

            await PreDownloadFile(url: downloadUrl, savePath, saveFileName);
        }
        /// <summary>
        ///    Download
        /// </summary>
        /// <param name="url">Download url.</param>
        /// <param name="savePath">Where to save the file.</param>
        /// <param name="fileName">The file name.</param>
        /// <param name="startHandler">Event handler of Start</param>
        /// <param name="progressHandler">Event handler of Progress</param>
        /// <param name="finishHandler">Event handler of Finish</param>
        /// <returns></returns>
        private async Task PreDownloadFile(
            string url,
            string savePath,
            string fileName
        )
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
