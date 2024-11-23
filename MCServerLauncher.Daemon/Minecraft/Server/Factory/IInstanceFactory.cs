namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

/// <summary>
///     MC服务器实例工厂接口, 用于创建服务器实例并生成Daemon可识别的配置文件
/// </summary>
public interface IInstanceFactory
{
    Func<Instance, Task>[] GetPostProcessors()
    {
        return Array.Empty<Func<Instance, Task>>();
    }
}

/// <summary>
///     从压缩包创建服务器实例的工厂接口
/// </summary>
public interface IArchiveInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromArchive(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器核心文件创建服务器实例的工厂接口
/// </summary>
public interface ICoreInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting);
}

/// <summary>
///     从服务器实例创建脚本创建服务器实例的工厂接口
/// </summary>
public interface IScriptInstanceFactory : IInstanceFactory
{
    Task<InstanceConfig> CreateInstanceFromScript(InstanceFactorySetting setting);
}

public static class InstanceFactoryExtensions
{
    /// <summary>
    ///     服务器实例创建的dispatcher
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="setting"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Task<InstanceConfig> CreateInstance(this IInstanceFactory factory, InstanceFactorySetting setting)
    {
        switch (setting.SourceType)
        {
            case SourceType.Archive:
                if (factory is IArchiveInstanceFactory archiveFactory)
                    return archiveFactory.CreateInstanceFromArchive(setting);
                break;
            case SourceType.Core:
                if (factory is ICoreInstanceFactory coreFactory) return coreFactory.CreateInstanceFromCore(setting);
                break;
            case SourceType.Script:
                if (factory is IScriptInstanceFactory scriptFactory)
                    return scriptFactory.CreateInstanceFromScript(setting);
                break;
        }

        throw new NotImplementedException($"No suitable factory found for SourceType.{setting.SourceType}");
    }
}