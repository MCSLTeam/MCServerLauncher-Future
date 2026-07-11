using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using Serilog;

namespace MCServerLauncher.Daemon.Management;

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
        if (config.InstanceType.RequiresNumericMinecraftVersion() && string.IsNullOrWhiteSpace(config.McVersion))
            return ResultExt.Err<Unit>("mc_version could not be empty");

        if (config.InstanceType.RequiresNumericMinecraftVersion())
            try
            {
                McVersion.Of(config.McVersion);
            }
            catch (ArgumentException)
            {
                return ResultExt.Err<Unit>("Could not parse mc_version");
            }

        var javaPathValidation = config.ValidateJavaPathExists();
        if (javaPathValidation.IsErr(out var javaPathError) && javaPathError is not null)
            return ResultExt.Err<Unit>(javaPathError);

        if (config.Uuid == Guid.Empty) return ResultExt.Err<Unit>("uuid should not be empty");

        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                config.GetWorkingDirectory(),
                config.Target,
                out _,
                out var targetError))
        {
            return ResultExt.Err<Unit>(targetError!);
        }

        return ResultExt.Ok(Unit.Default);
    }

    private static Result<Unit, Error> ValidateJavaPathExists(this InstanceConfig config)
    {
        if (
            config.TargetType is not TargetType.Jar
            || config.JavaPath.Equals("java", StringComparison.OrdinalIgnoreCase)
        ) return ResultExt.Ok(Unit.Default);

        try
        {
            return File.Exists(config.JavaPath)
                ? ResultExt.Ok(Unit.Default)
                : ResultExt.Err<Unit>("Could not found java in java_path");
        }
        catch (Exception ex)
        {
            return ResultExt.Err<Unit>(new Error("Failed to inspect java_path").CauseBy(ex));
        }
    }

    public static bool IsMinecraftJavaInstance(InstanceType type)
    {
        return type.IsMinecraftJavaRuntimeType();
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
    public static bool CanSafeCastTo<TInstance>(this InstanceConfig config)
        where TInstance : IInstance
    {
        if (typeof(TInstance) == typeof(MinecraftInstance)) return config.InstanceType.IsMinecraftJavaRuntimeType();

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
        if (config.CanSafeCastTo<MinecraftInstance>()) return new MinecraftInstance(config);

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

    public static Result<ProcessStartInfo, Error> TryGetStartInfo(this InstanceConfig config)
    {
        return ResultExt.Try(static cfg => cfg.GetStartInfo(), config)
            .MapErr(ex => new Error("Failed to build instance start info").CauseBy(ex));
    }

    private static (string File, string ArgumentString) GetLaunchScript(this InstanceConfig config)
    {
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                config.GetWorkingDirectory(),
                config.Target,
                out var fullPath,
                out var targetError))
        {
            throw new ArgumentException(targetError!.Cause, nameof(config));
        }

        var isMcServer = config.CanSafeCastTo<MinecraftInstance>();
        return config.TargetType switch
        {
            TargetType.Jar => (
                config.JavaPath,
                $"{string.Join(" ", config.Arguments)} -jar {config.Target}" + (isMcServer ? " nogui" : "")
            ),
            TargetType.Script => (fullPath, ""),
            TargetType.Executable => (fullPath, string.Join(" ", config.Arguments)),
            _ => throw new ArgumentOutOfRangeException(nameof(config.TargetType), config.TargetType, "Unknown target type")
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

        var eula = await ReadOrGenerateEula(eulaPath);
        if (eula.IsErr(out var readError) && readError is not null) return ResultExt.Err<Unit>(readError);

        return await WriteEula(eulaPath, eula.Unwrap());
    }

    private static async Task<Result<string[], Error>> ReadOrGenerateEula(string eulaPath)
    {
        var result = await ResultExt.TryAsync(async path =>
        {
            return File.Exists(path)
                ? (await File.ReadAllLinesAsync(path))
                .Select(x => x.Trim().StartsWith("eula") ? "eula=true" : x)
                .ToArray()
                : GenerateEula();
        }, eulaPath);

        return result.MapErr(ex => new Error("Failed to read EULA").CauseBy(ex));
    }

    private static async Task<Result<Unit, Error>> WriteEula(string eulaPath, string[] text)
    {
        var result = await ResultExt.TryAsync(async state =>
        {
            var (path, lines) = state;
            await File.WriteAllLinesAsync(path, lines, Encoding.UTF8);
        }, (eulaPath, text));

        return result.MapErr(ex => new Error("Failed to write EULA").CauseBy(ex));
    }
}
