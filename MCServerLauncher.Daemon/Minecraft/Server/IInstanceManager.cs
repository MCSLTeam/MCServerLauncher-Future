using System.Diagnostics;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public interface IInstanceManager
{
    /// <summary>
    ///     尝试添加一个服务器实例
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <param name="config">服务器配置</param>
    /// <param name="serverFactory">服务器实例工厂，会被异步执行，根据<see cref="ServerConfig" />执行服务器安装</param>
    /// <returns></returns>
    Task<bool> TryAddServer(string instanceName, ServerConfig config, Action<ServerConfig> serverFactory);

    /// <summary>
    ///     尝试移除一个服务器实例即实例文件夹，服务器必须是停止状态。
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <returns></returns>
    Task<bool> TryRemoveServer(string instanceName);

    /// <summary>
    ///     尝试启动一个服务器实例，如果服务器正在运行，返回false
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <param name="process">服务器进程</param>
    /// <returns></returns>
    bool TryStartServer(string instanceName, out Process? process);

    /// <summary>
    ///     尝试停止一个服务器实例, 如果服务器不在运行，返回false
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <returns></returns>
    bool TryStopServer(string instanceName);

    /// <summary>
    ///     向服务器进程的stdin发送消息
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <param name="message">消息</param>
    void SendToServer(string instanceName, string message);

    /// <summary>
    ///     杀死服务器进程
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    void KillServer(string instanceName);

    /// <summary>
    ///     获取服务器实例状态
    /// </summary>
    /// <param name="instanceName">实例名称</param>
    /// <returns></returns>
    InstanceStatus GetServerStatus(string instanceName);


    /// <summary>
    ///     获取所有服务器实例状态
    /// </summary>
    /// <returns></returns>
    IDictionary<string, InstanceStatus> GetAllStatus();
}