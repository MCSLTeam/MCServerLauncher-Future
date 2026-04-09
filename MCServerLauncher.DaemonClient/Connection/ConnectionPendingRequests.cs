using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.ProtoType.Action;

namespace MCServerLauncher.DaemonClient.Connection;

internal class ConnectionPendingRequests
{
    private readonly SemaphoreSlim _full;
    private readonly ConcurrentDictionary<Guid, byte> _pendings = new();
    public readonly int Size;
    private bool _closed;

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
    /// <exception cref="InvalidOperationException"></exception>
    /// <returns></returns>
    public async Task<bool> AddPendingAsync(Guid id, TaskCompletionSource<ActionResponse> tcs, int timeout,
        CancellationToken cancellationToken = default)
    {
        if (_closed) throw new InvalidOperationException("ConnectionPendingRequest already closed");

        if (!await _full.WaitAsync(timeout, cancellationToken)) return false;

        if (_pendings.TryAdd(id, 0)) return true;

        _full.Release();
        return false;
    }

    public bool TryRemovePending(Guid id, out TaskCompletionSource<ActionResponse> tcs)
    {
        tcs = null!;
        var rv = _pendings.TryRemove(id, out _);
        if (rv)
            _full.Release();
        return rv;
    }

    public bool TryGetPending(Guid id, out TaskCompletionSource<ActionResponse> tcs)
    {
        tcs = null!;
        return _pendings.ContainsKey(id);
    }

    public void Close()
    {
        _closed = true;
    }
}
