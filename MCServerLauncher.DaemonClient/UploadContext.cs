using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient;

public enum NetworkLoadContextState
{
    Opening,
    Cancelling,
    Closed
}

public abstract class NetworkLoadContext
{
    private readonly SemaphoreSlim _mutex = new(1);

    protected NetworkLoadContext(
        Guid fileId,
        CancellationTokenSource cancellationTokenSource,
        NetworkLoadSpeed uploadSpeed,
        IDaemon daemon
    )
    {
        FileId = fileId;
        CancellationTokenSource = cancellationTokenSource;
        UploadSpeed = uploadSpeed;
        Daemon = daemon;

        State = NetworkLoadContextState.Opening;
    }

    /// <summary>
    ///     服务端分配的文件ID, 用于标识一个上传任务
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    ///     各分段上传任务共同持有的CancellationTokenSource
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>
    ///     上传速度,用于查看速度和ETA,同时提供了速度更新时的event
    /// </summary>
    public NetworkLoadSpeed UploadSpeed { get; }

    /// <summary>
    ///     开启该上传上下文的Daemon
    /// </summary>
    public IDaemon Daemon { get; }

    public Task? NetworkLoadTask { get; set; }

    /// <summary>
    ///     服务端已接收的字节数
    /// </summary>
    public long LoadedBytes { get; set; } = 0;

    /// <summary>
    ///     是否完成上传
    /// </summary>
    public bool Done { get; set; } = false;

    /// <summary>
    ///     上传任务是否已经关闭
    /// </summary>
    public NetworkLoadContextState State { get; private set; }

    public void OnDone()
    {
        State = NetworkLoadContextState.Closed;
    }

    public async Task CancelAsync(int timeout = -1, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(timeout, ct);
        if (State == NetworkLoadContextState.Closed)
        {
            _mutex.Release();
            return;
        }

        if (State != NetworkLoadContextState.Opening)
            throw new InvalidOperationException("Cannot cancel an already closed upload context");

        State = NetworkLoadContextState.Cancelling;

        CancellationTokenSource.Cancel(); // 发送取消信号
        if (NetworkLoadTask != null) await NetworkLoadTask; // 等待各分区上传任务完成

        // 向服务端发出取消信号
        await SendCancelRequestAsync(ct);

        State = NetworkLoadContextState.Closed;

        _mutex.Release();
    }

    protected abstract Task SendCancelRequestAsync(CancellationToken ct = default);
}

public class UploadContext : NetworkLoadContext
{
    private readonly SemaphoreSlim _mutex = new(1);

    public UploadContext(Guid fileId, CancellationTokenSource cancellationTokenSource, NetworkLoadSpeed uploadSpeed,
        IDaemon daemon) : base(fileId, cancellationTokenSource, uploadSpeed, daemon)
    {
    }

    protected override Task SendCancelRequestAsync(CancellationToken ct = default)
    {
        return Daemon.RequestAsync(ActionType.FileUploadCancel,
            new FileUploadCancelParameter
            {
                FileId = FileId
            }, cancellationToken: ct);
    }
}

public class DownloadContext : NetworkLoadContext
{
    private readonly SemaphoreSlim _mutex = new(1);

    public DownloadContext(Guid fileId, CancellationTokenSource cancellationTokenSource, NetworkLoadSpeed uploadSpeed,
        IDaemon daemon) : base(fileId, cancellationTokenSource, uploadSpeed, daemon)
    {
    }


    protected override Task SendCancelRequestAsync(CancellationToken ct = default)
    {
        return Daemon.RequestAsync(ActionType.FileDownloadClose,
            new FileDownloadCloseParameter
            {
                FileId = FileId
            }, cancellationToken: ct);
    }
}