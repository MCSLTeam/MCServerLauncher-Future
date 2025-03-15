using MCServerLauncher.WPF.View.Components.Generic;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace MCServerLauncher.WPF.Modules
{
    public class DownloadManager
    {
        private static readonly Func<string, (int, int, int, int)> VersionToTuple = version =>
        {
            if (!version.Contains(".") && !version.Contains("-"))
                return version switch
                {
                    "horn" => (1, 19, 2, 0),
                    "GreatHorn" => (1, 19, 3, 0),
                    "Executions" => (1, 19, 4, 0),
                    "Trials" => (1, 20, 1, 0),
                    "Net" => (1, 20, 2, 0),
                    "Whisper" => (1, 20, 4, 0),
                    "general" => (0, 0, 0, 0),
                    "snapshot" => (0, 0, 0, 0),
                    "release" => (0, 0, 0, 0),
                    _ => (0, 0, 0, 0)
                };
            version = Regex.Replace(version.ToLower(), @"[-_]", ".")
                .Replace("rc", "").Replace("pre", "").Replace("snapshot", "0");
            var parts = version.Split('.');
            if (parts.Length == 2)
                return (int.Parse(parts[0]), int.Parse(parts[1]), 0, 0);
            if (parts.Length == 3)
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0);
            return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
            ;
        };

        private static readonly Func<string, string> VersionComparator = version =>
        {
            var versionTuple = VersionToTuple(version);
            return $"{versionTuple.Item1:D3}.{versionTuple.Item2:D3}.{versionTuple.Item3:D3}";
        };

        public static List<string?> SequenceMinecraftVersion(List<string>? originalList)
        {
            return (originalList ?? throw new ArgumentNullException(nameof(originalList))).OrderByDescending(VersionComparator!).ToList()!;
        }

        public async Task TriggerPreDownloadFile(string downloadUrl, string defaultFileName)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "All Files (*.*)|*.*",
                FileName = defaultFileName
            };
            saveFileDialog.ShowDialog();

            var saveFileName = saveFileDialog.FileName.Split('\\').Last();
            var savePath = saveFileDialog.FileName.Substring(0, saveFileDialog.FileName.Length - saveFileName.Length);
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
