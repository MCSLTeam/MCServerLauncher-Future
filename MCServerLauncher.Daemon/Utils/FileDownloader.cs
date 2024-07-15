using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Threading;

namespace MCServerLauncher.Daemon.Utils
{
    internal class FileDownloader
    {
        public event Action<int> ProgressChanged;
        public event Action DownloadCompleted;
        public event Action<double> SpeedChanged;

        private int _downloadSpeedLimit;

        public FileDownloader(int downloadSpeedLimit = 0)
        {
            _downloadSpeedLimit = downloadSpeedLimit;
        }

        public async Task DownloadFileAsync(string url, string destinationPath)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = true;

            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            {
                long totalBytes = response.ContentLength;
                long receivedBytes = 0;
                long bytesReceivedThisSecond = 0;
                DateTime start = DateTime.Now;
                DateTime lastSpeedReportTime = DateTime.Now;

                using (Stream responseStream = response.GetResponseStream())
                {
                    using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            receivedBytes += bytesRead;
                            bytesReceivedThisSecond += bytesRead;
                            int progressPercentage = (int)((receivedBytes * 100) / totalBytes);
                            ProgressChanged?.Invoke(progressPercentage);

                            DateTime now = DateTime.Now;
                            if ((now - lastSpeedReportTime).TotalSeconds >= 1)
                            {
                                double speed = bytesReceivedThisSecond / (now - lastSpeedReportTime).TotalSeconds;
                                SpeedChanged?.Invoke(speed);
                                bytesReceivedThisSecond = 0;
                                lastSpeedReportTime = now;
                            }

                            if (_downloadSpeedLimit > 0)
                            {
                                double elapsedSeconds = (now - start).TotalSeconds;
                                start = now;
                                double expectedElapsedSeconds = bytesRead / (double)_downloadSpeedLimit;

                                if (expectedElapsedSeconds > elapsedSeconds)
                                {
                                    int delay = (int)((expectedElapsedSeconds - elapsedSeconds) * 1000);
                                    await Task.Delay(delay);
                                }
                            }
                        }
                    }
                }
            }

            DownloadCompleted?.Invoke();
        }
    }
}