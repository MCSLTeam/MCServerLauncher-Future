using MCServerLauncher.Common.Network;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MCServerLauncher.Daemon;

public class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine($"MCServerLauncher.Daemon v{BasicUtils.AppVersion}");
        BasicUtils.InitApp();
        // var info = await SlpClient.GetStatusModern("pve-net.xiexilin.com", 30042);
        // Log.Information("[SlpClient] Get Server List Ping data: {0}",
        //     JsonConvert.SerializeObject(info?.Payload, Formatting.Indented));
        // Log.Information("[SlpClient] Latency: {0}ms", info?.Latency.Milliseconds);


        // Serve();
        await ServeAsync();
        // await RunMcServerAsync();
    }

    public static void TestJavaScanner()
    {
        BasicUtils.InitApp();
        JavaScanner.ScanJava();
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

    public static async Task RunMcServerAsync()
    {
        InstanceConfig config = new()
        {
            WorkingDirectory = @"./instance",
            JavaArgs = Array.Empty<string>(),
            JavaPath = "C:\\Program Files\\Common Files\\Oracle\\Java\\javapath\\java.exe",
            Name = "TestServer",
            InstanceType = InstanceType.Fabric,
            Target = "run.bat",
            TargetType = TargetType.Script
        };
        Instance instance = new(config);
        instance.Start();
        await Task.WhenAny(
            Task.Run(() =>
            {
                while (true) instance.ServerProcess.StandardInput.WriteLine(Console.ReadLine());
            }),
            Task.Run(instance.ServerProcess.WaitForExit)
        );
    }

    /// <summary>
    ///     app开始服务,包含配置DI和HttpServer启动
    /// </summary>
    private static async Task ServeAsync()
    {
        var app = new Application();
        await app.StartAsync();
    }
}