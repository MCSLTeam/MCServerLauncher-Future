using MCServerLauncher.Daemon.Helpers;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.Daemon
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine($"MCServerLauncher.Daemon v{BasicUtils.AppVersion}");
            BasicUtils.InitApp();
            //TestJavaScanner();
            Serve();
        }

        public static async void TestJavaScanner()
        {
            JavaScanner scanner = new();
            await scanner.ScanJava();
        }

        public static void WriteTestJavaInfo()
        {
            Console.WriteLine(
                new JavaScanner.JavaInfo
                {
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
                ["instanceCoreFilePath"] =
                    "E:\\Desktop\\MCSL2-2.2.5.1-Windows-x64\\MCSL2\\Downloads\\Arclight-Whisper-forge-1.0.3.jar",
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

        public static void TestServer()
        {
            BasicUtils.InitApp();
            Serve();
        }
        
        static void Serve()
        {
            // DI
            var containerBuilder = new ServiceCollection();

            ConfigureServices(containerBuilder);

            var container = containerBuilder.BuildServiceProvider(
#if DEBUG
                new ServiceProviderOptions() // TODO 生产模式应删去
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                }
#endif
            );

            var server = container.GetRequiredService<IServer>();
            server.Start();
        }

        /// <summary>
        /// 配置DI服务
        /// </summary>
        /// <param name="services"></param>
        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<IServer, Server>();

            services.AddScoped<ServerBehavior>();

            services.AddScoped<IActionService, ActionService>();
            services.AddScoped<IEventService, EventService>();

            services.AddSingleton<IJsonService, JsonService>();
            services.AddSingleton<IUserService, UserService>();

            // logger
            services.AddSingleton<RemoteLogHelper>();
        }
    }
}