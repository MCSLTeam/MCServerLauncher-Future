using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote
{
    internal class Utils
    {
        /// <summary>
        ///     根据给定的FileStream计算文件SHA1,计算完成后恢复文件读取位置
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        public static Task<string> FileSha1(FileStream fs, uint bufferSize = 16384)
        {
            return Task.Run(() =>
            {
                using (var sha1 = SHA1.Create())
                {
                    var ptr = fs.Position;
                    fs.Seek(0, SeekOrigin.Begin);

                    var buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);

                    sha1.TransformFinalBlock(buffer, 0, 0);

                    var hashBytes = sha1.Hash!;

                    fs.Seek(ptr, SeekOrigin.Begin);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            });
        }

        public static async Task<string?> HttpPost(string url)
        {
            var response = await Network.SendPostRequest(url,"");
            return response.StatusCode == HttpStatusCode.OK
                ? await response.Content.ReadAsStringAsync()
                : null;
        }
    }

    /// <summary>
    ///     用于表示上传/下载任务的速度和ETA,同时提供了Updated事件
    /// </summary>
    public class NetworkLoadSpeed
    {
        public NetworkLoadSpeed()
        {
            Window = new SpeedWindow(WindowSize);
        }

        /// <summary>
        ///     总字节数，只有当被赋值时,才会计算ETA, 否则为无限大
        /// </summary>
        public long? TotalBytes { get; set; }

        /// <summary>
        ///     滑动窗口大小
        /// </summary>
        public uint WindowSize { get; set; } = 1024;

        private SpeedWindow Window { get; }

        public DateTime StartTime { get; private set; }
        public DateTime LastUpdateTime { get; private set; }

        /// <summary>
        ///     下载速度(B/s) & ETA(s)
        /// </summary>
        private event Action<(double, double)>? Updated;

        /// <summary>
        ///     更新下载速度
        /// </summary>
        /// <param name="bytes">增加的字节数</param>
        /// <returns>(speed (B/s) , eta (s))</returns>
        public (double, double) Push(int bytes)
        {
            if (Window.IsEmpty) StartTime = DateTime.Now;
            LastUpdateTime = DateTime.Now;

            Window.Push(bytes);
            var speed = Window.GetSpeed();
            var eta = GetEta();
            Updated?.Invoke((speed, eta));
            return (speed, eta);
        }

        public double GetSpeed()
        {
            return Window.GetSpeed();
        }

        public double GetEta()
        {
            if (Window.IsEmpty) return double.PositiveInfinity;
            var duration = DateTime.Now - StartTime;
            var averageSpeed = duration.TotalSeconds == 0 ? 0 : Window.AccumulatedBytes / duration.TotalSeconds;
            return TotalBytes.HasValue
                ? (TotalBytes.Value - Window.AccumulatedBytes) / averageSpeed
                : double.PositiveInfinity;
        }

        /// <summary>
        ///     速度计算窗口,内部使用循环队列
        /// </summary>
        public class SpeedWindow
        {
            public SpeedWindow(uint windowSize)
            {
                WindowSize = windowSize;
                Window = new (DateTime, long)[windowSize + 1];
            }

            public uint WindowSize { get; }
            private (DateTime, long)[] Window { get; }
            private uint Tail { get; set; }
            private uint Head { get; set; }
            public int AccumulatedBytes { get; private set; }

            public bool IsEmpty => Head == Tail;
            public bool IsFull => (Head + 1) % (WindowSize + 1) == Tail;

            public void Push(int bytes)
            {
                AccumulatedBytes += bytes;
                Window[Head] = (DateTime.Now, AccumulatedBytes);
                Head = (Head + 1) % (WindowSize + 1);
                if (IsFull)
                    Tail = (Tail + 1) % (WindowSize + 1);
            }

            public double GetSpeed()
            {
                if (IsEmpty)
                    return 0;

                var (startTime, startBytes) = Window[Tail];
                var (endTime, endBytes) = Window[Head];

                try
                {
                    return (endBytes - startBytes) / (endTime - startTime).TotalSeconds;
                }
                catch (DivideByZeroException)
                {
                    return 0;
                }
            }
        }
    }
}