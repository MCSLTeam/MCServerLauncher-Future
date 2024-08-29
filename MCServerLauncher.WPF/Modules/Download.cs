using Downloader;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MCServerLauncher.WPF.Modules
{
    public class Download
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
            version = version.ToLower().Replace("-", ".").Replace("_", ".").Replace("rc", "").Replace("pre", "")
                .Replace("snapshot", "0");
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

        public static List<string> SequenceMinecraftVersion(List<string> originalList)
        {
            return originalList.OrderByDescending(VersionComparator).ToList();
        }

        /// <summary>
        ///    下载常规文件。
        /// </summary>
        /// <param name="url">Download url.</param>
        /// <param name="savePath">Where to save the file.</param>
        /// <param name="fileName">The file name.</param>
        /// <param name="startHandler">Event handler of Start</param>
        /// <param name="progressHandler">Event handler of Progress</param>
        /// <param name="finishHandler">Event handler of Finish</param>
        /// <returns></returns>
        public DownloadService DownloadFile(
            string url,
            string savePath,
            EventHandler<DownloadStartedEventArgs> startHandler,
            EventHandler<DownloadProgressChangedEventArgs> progressHandler,
            EventHandler<AsyncCompletedEventArgs> finishHandler,
            string fileName = null
        )
        {
            DownloadConfiguration downloaderOption = new()
            {
                Timeout = 5000,
                ChunkCount = SettingsManager.AppSettings.Download.ThreadCnt,
                ParallelDownload = true,
                RequestConfiguration =
                {
                    UserAgent = Network.CommonUserAgent
                }
            };
            DownloadService downloader = new(downloaderOption);
            downloader.DownloadStarted += startHandler;
            downloader.DownloadProgressChanged += progressHandler;
            downloader.DownloadFileCompleted += finishHandler;
            return downloader;
        }
    }
}
