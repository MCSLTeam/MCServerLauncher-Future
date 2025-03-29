using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Server;

// TODO 异步方法添加 cancellationToken
public interface IInstanceManager
{
    /// <summary>
    ///     尝试添加一个服务器实例
    /// </summary>
    /// <param name="setting"></param>
    /// <returns></returns>
    Task<bool> TryAddInstance(InstanceFactorySetting setting);

    /// <summary>
    ///     尝试移除一个服务器实例即实例文件夹，服务器必须是停止状态。
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    /// ƒ
    Task<bool> TryRemoveInstance(Guid instanceId);

    /// <summary>
    ///     尝试启动一个服务器实例，如果服务器正在运行，返回false
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <param name="instance">启动的服务器实例</param>
    /// <returns></returns>
    bool TryStartInstance(Guid instanceId, out Instance? instance);

    /// <summary>
    ///     尝试停止一个服务器实例, 如果服务器不在运行，返回false
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    bool TryStopInstance(Guid instanceId);

    /// <summary>
    ///     向服务器进程的stdin发送消息
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <param name="message">消息</param>
    void SendToInstance(Guid instanceId, string message);

    /// <summary>
    ///     杀死服务器进程
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    Task KillInstance(Guid instanceId);

    /// <summary>
    ///     获取服务器实例状态
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    Task<InstanceStatus> GetInstanceStatus(Guid instanceId);


    /// <summary>
    ///     获取所有服务器实例状态
    /// </summary>
    /// <returns></returns>
    Task<Dictionary<Guid, InstanceStatus>> GetAllStatus();

    Task StopAllInstances(CancellationToken ct = default);
}