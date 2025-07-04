using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management.Extensions;

// TODO 文件操作全部转换为Result
public static class InstanceConfigExtensions
{
    /// <summary>
    ///     获取实例工作目录
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public static string GetWorkingDirectory(this InstanceConfig config)
    {
        return Path.Combine(FileManager.InstancesRoot, config.Uuid.ToString());
    }

    /// <summary>
    ///     验证实例配置
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public static Result<Unit, Error> ValidateConfig(this InstanceConfig config)
    {
        if (config.InstanceType is not InstanceType.None)
            try
            {
                McVersion.Of(config.McVersion);
            }
            catch (IndexOutOfRangeException)
            {
                return ResultExt.Err<Unit>("Could not parse mc_version");
            }

        if (
            config.TargetType is TargetType.Jar
            && config.JavaPath.ToLower() != "java"
            && !File.Exists(config.JavaPath)
        ) return ResultExt.Err<Unit>("Could not found java in java_path");

        if (config.Uuid == Guid.Empty) return ResultExt.Err<Unit>("uuid should not be empty");

        return ResultExt.Ok(Unit.Default);
    }

    public static InstanceConfig AllocateNewUuid(this InstanceConfig config, Func<IEnumerable<Guid>> uuidSetFunc)
    {
        while (true)
        {
            var set = new HashSet<Guid>(uuidSetFunc.Invoke());

            if (!set.Contains(config.Uuid)) return config;

            config.Uuid = Guid.NewGuid();
        }
    }

    /// <summary>
    ///     按照配置文件, 检查配置文件拥有者(<see cref="IInstance" />)是否可以转换成指定实例类型
    /// </summary>
    /// <param name="config"></param>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns></returns>
    public static bool CanCastTo<TInstance>(this InstanceConfig config)
        where TInstance : IInstance
    {
        if (typeof(TInstance) == typeof(MinecraftInstance)) return config.InstanceType is not InstanceType.None;

        if (typeof(TInstance) == typeof(GenericInstance)) return true;

        return false;
    }

    /// <summary>
    ///     根据配置文件, 创建实例
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public static IInstance CreateInstance(this InstanceConfig config)
    {
        if (config.CanCastTo<MinecraftInstance>()) return new MinecraftInstance(config);

        return new GenericInstance(config);
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
                startInfo.EnvironmentVariables[key] = applied;

            Log.Warning("[InstanceConfig] Could apply env pattern={0}, skip.", pattern.ToString());
        }

        return startInfo;
    }

    private static (string File, string ArgumentString) GetLaunchScript(this InstanceConfig config)
    {
        var fullPath = Path.GetFullPath(Path.Combine(config.GetWorkingDirectory(), config.Target));
        var isMcServer = config.CanCastTo<MinecraftInstance>();
        return config.TargetType switch
        {
            TargetType.Jar => (
                config.JavaPath,
                $"{string.Join(" ", config.Arguments)} -jar {config.Target}" + (isMcServer ? " nogui" : "")
            ),
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
    public static async Task<Result<Unit, Error>> FixEula(this InstanceConfig config)
    {
        var eulaPath = Path.Combine(config.GetWorkingDirectory(), "eula.txt");
        return await ResultExt.TryAsync(async Task () =>
        {
            var text = File.Exists(eulaPath)
                ? (await File.ReadAllLinesAsync(eulaPath))
                .Select(x => eulaPath.Trim().StartsWith("eula") ? "eula=true" : x)
                .ToArray()
                : GenerateEula();
            await File.WriteAllLinesAsync(eulaPath, text, Encoding.UTF8);
        }).MapTask(result => result.MapErr(ex => new Error().CauseBy(ex)));
    }
}