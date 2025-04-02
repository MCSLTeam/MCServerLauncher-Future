using System.Text;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Storage;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public static class InstanceConfigExtensions
{
    public static string GetWorkingDirectory(this InstanceConfig config)
    {
        return Path.Combine(FileManager.InstancesRoot, config.Uuid.ToString());
    }

    public static (string, string) GetLaunchScript(this InstanceConfig config)
    {
        return config.TargetType switch
        {
            TargetType.Jar => (config.JavaPath, $"{string.Join(" ", config.JavaArgs)} -jar {config.Target} nogui"),
            TargetType.Script => (
                Path.Combine(Directory.GetCurrentDirectory(), config.GetWorkingDirectory(), config.Target),
                "")
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