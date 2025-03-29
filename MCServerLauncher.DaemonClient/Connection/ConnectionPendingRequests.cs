using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient.Connection;

internal class ConnectionPendingRequests
{
    private readonly SemaphoreSlim _full;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ActionResponse>> _pendings = new();
    public readonly int Size;

    public ConnectionPendingRequests(int size)
    {
        Size = size;
        _full = new SemaphoreSlim(size);
    }

    /// <summary>
    ///     添加一个pending请求
    /// </summary>
    /// <param name="id"></param>
    /// <param name="tcs"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<bool> AddPendingAsync(Guid id, TaskCompletionSource<ActionResponse> tcs, int timeout,
        CancellationToken cancellationToken = default)
    {
        if (!await _full.WaitAsync(timeout, cancellationToken)) return false;


        return _pendings.TryAdd(id, tcs); // 确保echo在size范围内不会重复
    }

    public bool TryRemovePending(Guid id, out TaskCompletionSource<ActionResponse> tcs)
    {
        var rv = _pendings.TryRemove(id, out tcs);
        _full.Release();
        return rv;
    }

    public bool TryGetPending(Guid id, out TaskCompletionSource<ActionResponse> tcs)
    {
        return _pendings.TryGetValue(id, out tcs);
    }
}
