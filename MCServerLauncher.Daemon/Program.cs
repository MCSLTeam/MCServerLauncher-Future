using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
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

        // var manager = InstanceManager.Create();
        // await manager.TryRemoveInstance(Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b2"));
        // await CreateInstance(manager);
        // await RunMcServerAsync(manager, Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b2"));

        await ServeAsync();
    }

    public static void TestJavaScanner()
    {
        BasicUtils.InitApp();
        JavaScanner.ScanJava(true).Wait();
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

    public static async Task<Guid> CreateInstance(IInstanceManager manager)
    {
        Log.Information("[InstanceManager] All instance: {0}",
            JsonConvert.SerializeObject(manager.GetAllStatus(), Formatting.Indented));
        var setting = new InstanceFactorySetting
        {
            Uuid = Guid.Parse("fdbf680c-fe52-4f1d-89ba-a0d9d8b857b2"),
            Name = "1-21-1",
            InstanceType = InstanceType.Vanilla,
            Target = "server.jar",
            TargetType = TargetType.Jar,
            JavaPath = "java",
            JavaArgs = Array.Empty<string>(),
            SourceType = SourceType.Core,
            Source = "daemon/downloads/Vanilla-release-1.21.1-59353f.jar",
            McVersion = "1.21.1",
            UsePostProcess = false
        };
        if (await manager.TryAddInstance(setting))
        {
            Log.Information("[InstanceManager] Created Server: {0}({1})", setting.Name, setting.Uuid);
            return setting.Uuid;
        }

        Log.Information("[InstanceManager] Failed to create server");
        return Guid.Empty;
    }

    public static async Task RunMcServerAsync(IInstanceManager manager, Guid id)
    {
        if (manager.TryStartInstance(id, out var instance))
            await Task.WhenAny(
                Task.Run(() =>
                {
                    while (true) instance!.WriteLine(Console.ReadLine());
                }),
                Task.Run(instance!.WaitForExit)
            );
        else
            Log.Error("[InstanceManager] Failed to start server: {0}", $"{instance?.Config.Name}({id})");
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