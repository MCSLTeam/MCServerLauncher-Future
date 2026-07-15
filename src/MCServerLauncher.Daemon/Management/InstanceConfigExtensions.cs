using System.Diagnostics;
using System.Runtime.InteropServices;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.API.Errors;
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

    public static string GetWorkingDirectory(this InstanceConfiguration config)
    {
        return Path.Combine(FileManager.InstancesRoot, config.InstanceId.ToString());
    }

    /// <summary>
    ///     验证实例配置
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public static Result<Unit, DaemonError> ValidateConfig(this InstanceConfig config)
    {
        return ValidateConfigCore(
            config.InstanceType,
            config.Version,
            config.TargetType,
            config.JavaPath,
            config.Uuid,
            config.GetWorkingDirectory(),
            config.Target);
    }

    public static Result<Unit, DaemonError> ValidateConfig(this InstanceConfiguration config)
    {
        return ValidateConfigCore(
            config.InstanceType,
            config.Version,
            config.TargetType,
            config.JavaPath,
            config.InstanceId,
            config.GetWorkingDirectory(),
            config.Target);
    }

    private static Result<Unit, DaemonError> ValidateConfigCore(
        InstanceType instanceType,
        string version,
        TargetType targetType,
        string javaPath,
        Guid instanceId,
        string workingDirectory,
        string target)
    {
        if (instanceType.RequiresNumericMinecraftVersion() && string.IsNullOrWhiteSpace(version))
            return ResultExt.Err<Unit>(new ValidationDaemonError(
                "instance.version.required",
                "mc_version could not be empty."));

        if (instanceType.RequiresNumericMinecraftVersion())
            try
            {
                McVersion.Of(version);
            }
            catch (ArgumentException)
            {
                return ResultExt.Err<Unit>(new ValidationDaemonError(
                    "instance.version.invalid",
                    "Could not parse mc_version."));
            }

        var javaPathValidation = ValidateJavaPathExists(targetType, javaPath);
        if (javaPathValidation.IsErr(out var javaPathError) && javaPathError is not null)
            return ResultExt.Err<Unit>(javaPathError);

        if (instanceId == Guid.Empty)
            return ResultExt.Err<Unit>(new ValidationDaemonError(
                "instance.id.empty",
                "uuid should not be empty."));

        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                workingDirectory,
                target,
                out _,
                out var targetError))
        {
            return ResultExt.Err<Unit>(targetError!);
        }

        return ResultExt.Ok(Unit.Default);
    }

    private static Result<Unit, DaemonError> ValidateJavaPathExists(TargetType targetType, string javaPath)
    {
        if (
            targetType is not TargetType.Jar
            || javaPath.Equals("java", StringComparison.OrdinalIgnoreCase)
        ) return ResultExt.Ok(Unit.Default);

        try
        {
            return File.Exists(javaPath)
                ? ResultExt.Ok(Unit.Default)
                : ResultExt.Err<Unit>(new ValidationDaemonError(
                    "instance.java_path.not_found",
                    "Could not find java in java_path."));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[InstanceConfig] Failed to inspect java_path.");
            return ResultExt.Err<Unit>(
                new InternalDaemonError("instance.java_path.inspect_failed", "Failed to inspect java_path."));
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

    public static Result<ProcessStartInfo, DaemonError> TryGetStartInfo(this InstanceConfig config)
    {
        return ResultExt.Try(static cfg => cfg.GetStartInfo(), config)
            .MapErr(static exception =>
            {
                Log.Error(exception, "[InstanceConfig] Failed to build instance start info.");
                return (DaemonError)new InternalDaemonError(
                    "instance.start_info.failed",
                    "Failed to build instance start info.");
            });
    }

    private static (string File, string ArgumentString) GetLaunchScript(this InstanceConfig config)
    {
        if (!InstanceTargetPathValidator.TryResolveTargetFile(
                config.GetWorkingDirectory(),
                config.Target,
                out var fullPath,
                out var targetError))
        {
            throw new ArgumentException(targetError!.Message, nameof(config));
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

}
