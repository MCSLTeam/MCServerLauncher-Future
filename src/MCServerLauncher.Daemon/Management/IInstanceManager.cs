using System.Collections.Concurrent;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Utils;
using RustyOptions;
using InstanceConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceConfiguration;
using InstanceFactoryConfiguration = MCServerLauncher.Common.Contracts.Instances.InstanceFactoryConfiguration;
using InstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.InstanceSettingsResult;
using UpdateInstanceSettingsRequest = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsRequest;
using UpdateInstanceSettingsResult = MCServerLauncher.Common.Contracts.Instances.UpdateInstanceSettingsResult;

namespace MCServerLauncher.Daemon.Management;

internal interface IInstanceManager
{
    public ConcurrentDictionary<Guid, IInstance> Instances { get; }
    public ConcurrentDictionary<Guid, IInstance> RunningInstances { get; }

    /// <summary>
    ///     尝试添加一个服务器实例
    /// </summary>
    /// <param name="setting"></param>
    /// <returns></returns>
    Task<Result<InstanceConfiguration, DaemonError>> TryAddInstance(
        InstanceFactoryConfiguration setting,
        CancellationToken ct = default);

    /// <summary>
    ///     尝试移除一个服务器实例即实例文件夹，服务器必须是停止状态。
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    /// ƒ
    Task<bool> TryRemoveInstance(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    ///     尝试启动一个服务器实例，如果服务器正在运行，返回false
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    public Task<IInstance?> TryStartInstance(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    ///     尝试停止一个服务器实例, 如果服务器不在运行，返回false
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    Task<bool> TryStopInstance(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    ///     向服务器进程的stdin发送消息
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <param name="message">消息</param>
    bool SendToInstance(Guid instanceId, string message);

    bool TryWriteConsole(Guid instanceId, ReadOnlyMemory<byte> data);

    bool TryResizeConsole(Guid instanceId, ushort columns, ushort rows);

    Guid? AttachConsole(Guid instanceId, Func<ReadOnlyMemory<byte>, long, CancellationToken, Task> handler);

    void DetachConsole(Guid instanceId, Guid subscriberId);

    /// <summary>
    ///     杀死服务器进程
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    void KillInstance(Guid instanceId);

    /// <summary>
    ///     获取服务器实例状态
    /// </summary>
    /// <param name="instanceId">实例Uuid</param>
    /// <returns></returns>
    Task<InstanceReport?> GetInstanceReport(Guid instanceId, CancellationToken ct = default);


    /// <summary>
    ///     获取所有服务器实例状态
    /// </summary>
    /// <returns></returns>
    Task<Dictionary<Guid, InstanceReport>> GetAllReports(CancellationToken ct = default);

    bool TryGetInstanceLog(Guid instanceId, out IReadOnlyList<string> logs);

    Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettings(
        Guid instanceId,
        CancellationToken ct = default);

    Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettings(
        UpdateInstanceSettingsRequest request,
        CancellationToken ct = default);

    IDisposable AcquireInstanceMutation(Guid instanceId);

    ValueTask<IDisposable> AcquireInstanceMutationAsync(Guid instanceId, CancellationToken ct = default);

    Task StopAllInstances(CancellationToken ct = default);
}
