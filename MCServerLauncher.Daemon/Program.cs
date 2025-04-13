﻿using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Storage;
using Newtonsoft.Json;
using Serilog;

namespace MCServerLauncher.Daemon;

public class Program
{
    private static async Task Main(string[] args)
    {
        if (!Application.Init()) Environment.Exit(1);

        var app = new Application();
        await app.ServeAsync();
    }

    public static void TestJavaScanner()
    {
        Application.Init();
        JavaScanner.ScanJava(true).Wait();
    }

    public static void WriteTestJavaInfo()
    {
        System.Console.WriteLine(
            new JavaInfo
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
            Source = "https://download.fastmirror.net/download/Vanilla/release/1.21.1-59353f",
            McVersion = "1.21.1",
            UsePostProcess = false
        };
        if (await manager.TryAddInstance(setting) is not null)
        {
            Log.Information("[InstanceManager] Created Server: {0}({1})", setting.Name, setting.Uuid);
            return setting.Uuid;
        }

        Log.Information("[InstanceManager] Failed to create server");
        return Guid.Empty;
    }

    public static async Task RunMcServerAsync(IInstanceManager manager, Guid id)
    {
        var instance = await manager.TryStartInstance(id);
        if (instance is not null)
            await Task.WhenAny(
                Task.Run(() =>
                {
                    while (true) instance.WriteLine(System.Console.ReadLine());
                }),
                Task.Run(instance!.WaitForExit)
            );
        else
            Log.Error("[InstanceManager] Failed to start server: {0}", $"{instance?.Config.Name}({id})");
    }
}