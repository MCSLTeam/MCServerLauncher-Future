using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Storage;
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
        // var info = await SlpClient.GetStatusModern("balabala", 11451);
        // Log.Information("[SlpClient] Get Server List Ping data: {0}",
        //     JsonConvert.SerializeObject(info?.Payload, Formatting.Indented));
        // Log.Information("[SlpClient] Latency: {0}ms", info?.Latency.Milliseconds);

        // var manager = Minecraft.Server.InstanceManager.Create();
        // await manager.TryRemoveServer("1-21-1");
        // await CreateInstance(manager);
        // await RunMcServerAsync(manager, "1-21-1");

        await ServeAsync();
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

    public static async Task<bool> CreateInstance(IInstanceManager manager)
    {
        Log.Information("[InstanceManager] All instance: {0}",
            JsonConvert.SerializeObject(manager.GetAllStatus(), Formatting.Indented));
        var setting = new InstanceFactorySetting
        {
            Name = "1-21-1",
            InstanceType = InstanceType.Vanilla,
            Target = "server.jar",
            TargetType = TargetType.Jar,
            JavaPath = "java",
            JavaArgs = Array.Empty<string>(),
            SourceType = SourceType.Core,
            Source = "daemon/downloads/Vanilla-release-1.21.1-59353f.jar"
        };
        if (await manager.TryAddInstance(setting, new VanillaFactory()))
        {
            Log.Information("[InstanceManager] Created Server: {0}", setting.Name);
            return true;
        }

        Log.Information("[InstanceManager] Failed to create server");
        return false;
    }

    public static async Task RunMcServerAsync(IInstanceManager manager, string name)
    {
        if (manager.TryStartInstance(name, out var instance))
            await Task.WhenAny(
                Task.Run(() =>
                {
                    while (true) instance.ServerProcess.StandardInput.WriteLine(Console.ReadLine());
                }),
                Task.Run(instance.ServerProcess.WaitForExit)
            );
        else
            Log.Error("[InstanceManager] Failed to start server: {0}", name);
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