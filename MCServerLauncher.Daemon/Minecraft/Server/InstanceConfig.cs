using System.Text;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public class InstanceConfig
{
    /// <summary>
    ///     配置的固定文件名
    /// </summary>
    public const string FileName = "daemon_instance.json";

    /// <summary>
    ///     控制台输入编码
    /// </summary>
    public Encoding InputEncoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     服务器工作目录
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    ///     java虚拟机参数列表
    /// </summary>
    public string[] JavaArgs { get; set; }

    /// <summary>
    ///     java虚拟机路径
    /// </summary>
    public string JavaPath { get; set; }

    /// <summary>
    ///     服务器名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     控制台输出编码
    /// </summary>
    public Encoding OutputEncoding { get; set; } = Encoding.UTF8;

    /// <summary>
    ///     服务器类型(vanilla, fabric, forge ...)
    /// </summary>
    public InstanceType InstanceType { get; set; }

    /// <summary>
    ///     服务器启动目标(jar文件名, 脚本文件名)
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    ///     服务器启动目标类型(jar, script[bat, sh])
    /// </summary>
    public TargetType TargetType { get; set; }

    public (string, string) GetLaunchScript()
    {
        return TargetType switch
        {
            TargetType.Jar => ("java", $"{string.Join(" ", JavaArgs)} -jar {Target} nogui"),
            TargetType.Script => (Path.Combine(Directory.GetCurrentDirectory(), WorkingDirectory, Target), "")
        };
    }
}

public static class InstanceConfigExtensions
{
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
        var eulaPath = Path.Combine(config.WorkingDirectory, "eula.txt");
        var text = File.Exists(eulaPath)
            ? (await File.ReadAllLinesAsync(eulaPath)).Select(x => eulaPath.Trim().StartsWith("eula") ? "eula=true" : x)
            .ToArray()
            : GenerateEula();
        await File.WriteAllLinesAsync(eulaPath, text, Encoding.UTF8);
    }
}