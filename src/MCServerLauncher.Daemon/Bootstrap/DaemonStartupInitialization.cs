using System.Reflection;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Status;
using Serilog;

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonStartupInitialization
{
    private static void InitLogger()
    {
        var logConfig = new LoggerConfiguration();

        logConfig = AppConfig.Get().Verbose ? logConfig.MinimumLevel.Verbose() : logConfig.MinimumLevel.Information();

        Log.Logger = logConfig
            .WriteTo.Async(a => a.File($"{FileManager.LogRoot}/daemon-.txt", rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void InitDataDirectory()
    {
        List<string> dataFolders =
        [
            FileManager.Root,
            FileManager.InstancesRoot,
            FileManager.LogRoot,
            FileManager.ContainedRoot
        ];

        foreach (var dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
            Directory.CreateDirectory(dataFolder!);
    }

    public static async Task<bool> InitializeAsync()
    {
        InitLogger();
        InitDataDirectory();
        Log.Information("MCServerLauncher.Daemon v{0}", Assembly.GetExecutingAssembly().GetName().Version!);

        ContainedFiles.ExtractContained();
        FileManager.StartFileSessionsWatcher();

        try
        {
            // windows下预先检查CIM是否可用
            await SystemInfoHelper.GetSystemInfo();
        }
        catch (AggregateException e)
        {
            Log.Error("Could not Init Application: {0}",
                string.Join("\n", e.InnerExceptions.Select(x => x.ToString())));
            return false;
        }

        InstanceFactoryRegistry.InitializeDefaults();

        var selectedRegistry = ActionHandlerRegistryRuntime.Initialize(AppConfig.Get().UseGeneratedActionRegistry);
        Log.Information("[ActionHandlerRegistry] Using {Mode} registry path", selectedRegistry.Mode);

        return true;
    }
}
