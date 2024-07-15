using MCServerLauncher.Daemon.Helpers;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using MCServerLauncher.Daemon.Utils;
namespace MCServerLauncher.Daemon
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("MCServerLauncher.Daemon");
            BasicUtils.InitApp();
            Downloader.TestFileDownloader().Wait();
            //TestJavaScanner();
        }
        class Downloader
        {
            /// <summary>
            /// Download Test
            /// TEMP VERSION
            /// </summary>
            public static async Task TestFileDownloader()
            {
                int downloadSpeedLimit = 0; // SpeedLimits(B/s)
                FileDownloader downloader = new FileDownloader(downloadSpeedLimit);
                downloader.ProgressChanged += OnProgressChanged;
                downloader.DownloadCompleted += OnDownloadCompleted;
                downloader.DownloadError += OnDownloadError;

                string url = "https://cdn.polars.cc/minecraft-server-1.19.2.jar";
                string destinationPath = "D:\\minecraft-server-1.19.2.jar";

                await downloader.DownloadFileAsync(url, destinationPath);
            }

            private static void OnProgressChanged(int progressPercentage, double speed, long receivedBytes, long totalBytes)
            {
                double speedKB = speed / 1024; 
                double receivedMB = receivedBytes / (1024 * 1024); 
                double totalMB = totalBytes / (1024 * 1024); 
                Console.WriteLine($"Progress: {progressPercentage}% | Download Speed: {speedKB:F2} KB/s | {receivedMB:F2} MB / {totalMB:F2} MB");
            }

            private static void OnDownloadCompleted()
            {
                Console.WriteLine("Complete!");
            }

            private static void OnDownloadError(string errorMessage)
            {
                Console.WriteLine($"Error: {errorMessage}");
            }

        }
        public static async void TestJavaScanner()
        {
            JavaScanner Scanner = new();
            await Scanner.ScanJava();
        }
        public static void WriteTestJavaInfo()
        {
            Console.WriteLine(
                new JavaScanner.JavaInfo { 
                    Architecture = "x64",
                    Path = "tmp",
                    Version = "8"
                }
            );
        }
        public static async void TestCreateInstance()
        {
            InstanceManager Manager = new();
            JObject InstanceConfig = new()
            {
                ["instanceType"] = "MinecraftJavaServer",
                ["instanceCoreFilePath"] = "E:\\Desktop\\MCSL2-2.2.5.1-Windows-x64\\MCSL2\\Downloads\\Arclight-Whisper-forge-1.0.3.jar",
                ["instanceJavaRuntimePath"] = "C:\\Program Files\\Java\\jre1.8.0_291\\bin\\java.exe",
                ["instanceJvmMinimumMemory"] = 1024,
                ["instanceJvmMaximumMemory"] = 2048,
                ["instanceJvmArguments"] = new JArray
                {
                    "-XX:+UseG1GC",
                    "-XX:MaxGCPauseMillis=200",
                    "-XX:+UnlockExperimentalVMOptions",
                    "-XX:G1NewSizePercent=20",
                    "-XX:G1ReservePercent=20",
                    "-XX:G1HeapRegionSize=32M",
                    "-XX:G1HeapWastePercent=5",
                    "-XX:G1MixedGCCountTarget=4",
                    "-XX:InitiatingHeapOccupancyPercent=15",
                    "-XX:G1MixedGCLiveThresholdPercent=90",
                    "-XX:G1RSetUpdatingPauseTimePercent=5",
                    "-XX:SurvivorRatio=32",
                    "-XX:+PerfDisableSharedMem",
                    "-XX:MaxTenuringThreshold=1",
                    "-Dusing.aikars.flags=https://mcflags.emc.gs",
                    "-Daikars.new.flags=true"
                },
                ["instanceName"] = "TestInstance"
            };
            Console.WriteLine(JsonConvert.SerializeObject(InstanceConfig, Formatting.Indented));
            await Manager.CreateInstance(InstanceConfig);
        }
        }
}
