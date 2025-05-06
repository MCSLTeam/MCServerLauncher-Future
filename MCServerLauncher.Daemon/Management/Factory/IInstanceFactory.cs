using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Factory;

/// <summary>
///     MC服务器实例工厂接口, 用于创建服务器实例并生成Daemon可识别的配置文件
/// </summary>
public interface IInstanceFactory
{
    Func<MinecraftInstance, Task<Result<Unit, Error>>>[] GetPostProcessors()
    {
        return Array.Empty<Func<MinecraftInstance, Task<Result<Unit, Error>>>>();
    }
}

/// <summary>
///     从压缩包创建服务器实例的工厂接口
/// </summary>
public interface IArchiveInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfig, Error>> CreateInstanceFromArchive(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器核心文件创建服务器实例的工厂接口
/// </summary>
public interface ICoreInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfig, Error>> CreateInstanceFromCore(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器实例创建脚本创建服务器实例的工厂接口
/// </summary>
public interface IScriptInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfig, Error>> CreateInstanceFromScript(InstanceFactorySetting setting);
}