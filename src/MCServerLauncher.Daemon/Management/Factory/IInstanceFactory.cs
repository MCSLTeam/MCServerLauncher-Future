using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.Minecraft;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Management.Minecraft;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;

namespace MCServerLauncher.Daemon.Management.Factory;

/// <summary>
///     MC服务器实例工厂接口, 用于创建服务器实例并生成Daemon可识别的配置文件
/// </summary>
public interface IInstanceFactory
{
    Func<MinecraftInstance, Task<Result<Unit, DaemonError>>>[] GetPostProcessors()
    {
        return Array.Empty<Func<MinecraftInstance, Task<Result<Unit, DaemonError>>>>();
    }
}

/// <summary>
///     从压缩包创建服务器实例的工厂接口
/// </summary>
public interface IArchiveInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromArchive(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     从服务器核心文件创建服务器实例的工厂接口
/// </summary>
public interface ICoreInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromCore(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     从服务器实例创建脚本创建服务器实例的工厂接口
/// </summary>
public interface IScriptInstanceFactory : IInstanceFactory
{
    Task<Result<InstanceConfiguration, DaemonError>> CreateInstanceFromScript(
        InstanceFactoryConfiguration setting,
        CancellationToken cancellationToken = default);
}
