using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Minecraft.Extensions;

public static class InstanceConfigExtensions
{
    public static string GetWorkingDirectory(this InstanceConfig config)
    {
        return Path.Combine(FileManager.InstancesRoot, config.Uuid.ToString());
    }

    public static bool ValidateConfig(this InstanceConfig config)
    {
        if (config.InstanceType is not InstanceType.None)
            try
            {
                McVersion.Of(config.McVersion);
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

        if (
            config.TargetType is TargetType.Jar
            && config.JavaPath.ToLower() != "java"
            && !File.Exists(config.JavaPath)
        ) return false;

        if (config.Uuid == Guid.Empty) return false;

        return true;
    }


    public static ProcessStartInfo GetStartInfo(this InstanceConfig config)
    {
        var (target, args) = config.GetLaunchScript();

        var startInfo = new ProcessStartInfo(target, args)
        {
            UseShellExecute = false,
            WorkingDirectory = config.GetWorkingDirectory(),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true
        };

        var originPath = Environment.GetEnvironmentVariable("PATH");
        startInfo.EnvironmentVariables["PATH"] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{Path.GetDirectoryName(config.JavaPath)};{originPath}"
            : $"{Path.GetDirectoryName(config.JavaPath)}:{originPath}";

        foreach (var (key, pattern) in config.Env)
        {
            if (pattern.TryApply(startInfo.EnvironmentVariables, (k, m) => m[k], out var applied))
            {
                startInfo.EnvironmentVariables[key] = applied;
            }

            Log.Warning("[InstanceConfig] Could apply env pattern={0}, skip.", pattern.ToString());
        }

        return startInfo;
    }

    private static (string File, string ArgumentString) GetLaunchScript(this InstanceConfig config)
    {
        var fullPath = Path.GetFullPath(Path.Combine(config.GetWorkingDirectory(), config.Target));
        return config.TargetType switch
        {
            TargetType.Jar => (config.JavaPath, $"{string.Join(" ", config.Arguments)} -jar {config.Target} nogui"),
            TargetType.Script => (fullPath, ""),
            TargetType.Executable => (fullPath, string.Join(" ", config.Arguments))
        };
    }

    /// <summary>
    ///     生成同意 EULA 的文本
    /// </summary>
    /// <returns></returns>
    private static string[] GenerateEula()
    {
        var text = new string[3];
        text[0] =
            "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).";
        text[1] = "#" + DateTime.Now.ToString("ddd MMM dd HH:mm:ss zzz yyyy");
        text[2] = "eula=true";
        return text;
    }

    /// <summary>
    ///     为实例生成 EULA 文件
    /// </summary>
    /// <param name="config"></param>
    public static async Task FixEula(this InstanceConfig config)
    {
        var eulaPath = Path.Combine(config.GetWorkingDirectory(), "eula.txt");
        var text = File.Exists(eulaPath)
            ? (await File.ReadAllLinesAsync(eulaPath)).Select(x => eulaPath.Trim().StartsWith("eula") ? "eula=true" : x)
            .ToArray()
            : GenerateEula();
        await File.WriteAllLinesAsync(eulaPath, text, Encoding.UTF8);
    }
}