using System.Buffers;
using System.Collections;

namespace MCServerLauncher.Common.Concurrent;

/// <summary>
///     一个 D-ary 堆实现
///     特性：
///     1. 支持异步等待出队 (DequeueAsync)
///     2. 支持同步立即出队 (Dequeue)
///     3. 内存池优化
///     4. 读写锁粒度控制
///     5. 优先级快速查询
/// </summary>
public sealed class DaryHeap<TElement, TPriority> : IDisposable
    where TPriority : IComparable<TPriority>
{
    private const int CF_DEFAULT_ARITY = 4;
    private const int CF_INITIAL_CAPACITY = 64;

    private readonly int _arity;
    private readonly Dictionary<TPriority, int> _indexMap = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly SemaphoreSlim _semaphore = new(0);
    private int _count;
    private bool _disposed;
    private (TElement Element, TPriority Priority)[] _heap;

    public DaryHeap(int arity = CF_DEFAULT_ARITY)
    {
        _arity = Math.Max(2, arity);
        _heap = ArrayPool<(TElement, TPriority)>.Shared.Rent(CF_INITIAL_CAPACITY);
    }

    public int Count
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                return _count;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rwLock.Dispose();
        _semaphore.Dispose();
        ArrayPool<(TElement, TPriority)>.Shared.Return(_heap);
        _heap = null!;
    }

    public void Enqueue(TElement element, TPriority priority)
    {
        _rwLock.EnterWriteLock();
        try
        {
            EnsureCapacity();

            // 添加到堆尾
            var index = _count++;
            _heap[index] = (element, priority);
            _indexMap[priority] = index;
            BubbleUp(index);

            _semaphore.Release();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public (TElement Element, TPriority Priority) Dequeue()
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (_count == 0)
                throw new InvalidOperationException("Heap is empty");

            return DequeueCore();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public async Task<(TElement Element, TPriority Priority)> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        _rwLock.EnterWriteLock();
        try
        {
            return DequeueCore();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public bool TryRemove(TPriority priority, out TElement? element)
    {
        element = default;
        var removed = false;

        _rwLock.EnterWriteLock();
        try
        {
            // 检查优先级是否存在
            if (!_indexMap.TryGetValue(priority, out var index)) return false;

            // 获取要移除的元素
            element = _heap[index].Element;
            removed = true;

            // 判断是否是最后一个元素
            var isLast = index == _count - 1;

            // 将要删除的元素与最后一个元素交换
            if (!isLast) Swap(index, _count - 1);

            // 移除最后一个元素（即目标元素）
            _count--;
            _indexMap.Remove(priority);

            // 调整堆结构（非最后一个元素时需要修复堆）
            if (!isLast)
            {
                // 需要同时检查向上和向下调整
                var parentIndex = (index - 1) / _arity;
                if (index > 0 && _heap[index].Priority.CompareTo(_heap[parentIndex].Priority) < 0)
                    BubbleUp(index);
                else
                    BubbleDown(index);
            }

            // 调整信号量计数
            if (_semaphore.CurrentCount > 0)
                try
                {
                    // 非阻塞减少信号量
                    _semaphore.Wait(0);
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放的异常
                }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return removed;
    }

    public bool ContainsPriority(TPriority priority)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _indexMap.ContainsKey(priority);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public TElement GetElement(TPriority priority)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _heap[_indexMap[priority]].Element;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public TElement? GetElementOrDefault(TPriority priority, TElement? defaultValue = default)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _indexMap.TryGetValue(priority, out var index) ? _heap[index].Element : defaultValue;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public UnorderedItemCollection<TElement, TPriority> GetUnorderedItems()
    {
        _rwLock.EnterReadLock();
        try
        {
            var buffer = ArrayPool<(TElement, TPriority)>.Shared.Rent(_count);
            _heap.AsSpan(0, _count).CopyTo(buffer);
            return new UnorderedItemCollection<TElement, TPriority>(
                buffer,
                _count,
                ArrayPool<(TElement, TPriority)>.Shared);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private (TElement Element, TPriority Priority) DequeueCore()
    {
        var min = _heap[0];
        _indexMap.Remove(min.Priority);

        if (--_count > 0)
        {
            _heap[0] = _heap[_count];
            _indexMap[_heap[0].Priority] = 0;
            BubbleDown(0);
        }

        return min;
    }

    private void BubbleUp(int index)
    {
        while (index > 0)
        {
            var parentIndex = (index - 1) / _arity;
            if (_heap[index].Priority.CompareTo(_heap[parentIndex].Priority) >= 0)
                break;

            Swap(index, parentIndex);
            index = parentIndex;
        }
    }

    private void BubbleDown(int index)
    {
        while (true)
        {
            var firstChild = index * _arity + 1;
            if (firstChild >= _count) break;

            var minChild = firstChild;
            var lastChild = Math.Min(firstChild + _arity, _count);

            for (var i = firstChild + 1; i < lastChild; i++)
                if (_heap[i].Priority.CompareTo(_heap[minChild].Priority) < 0)
                    minChild = i;

            if (_heap[minChild].Priority.CompareTo(_heap[index].Priority) < 0)
            {
                Swap(index, minChild);
                index = minChild;
            }
            else
            {
                break;
            }
        }
    }

    private void Swap(int i, int j)
    {
        (_heap[i], _heap[j]) = (_heap[j], _heap[i]);
        _indexMap[_heap[i].Priority] = i;
        _indexMap[_heap[j].Priority] = j;
    }

    private void EnsureCapacity()
    {
        if (_count < _heap.Length) return;

        var newHeap = ArrayPool<(TElement, TPriority)>.Shared.Rent(_heap.Length * 2);
        Array.Copy(_heap, newHeap, _count);
        ArrayPool<(TElement, TPriority)>.Shared.Return(_heap);
        _heap = newHeap;
    }

    public sealed class UnorderedItemCollection<THeapElement, THeapPriority>
        : IReadOnlyList<(THeapElement Element, THeapPriority Priority)>, IDisposable
    {
        private readonly (THeapElement, THeapPriority)[] _buffer;
        private readonly int _count;
        private readonly ArrayPool<(THeapElement, THeapPriority)> _pool;
        private bool _disposed;

        internal UnorderedItemCollection(
            (THeapElement, THeapPriority)[] buffer,
            int count,
            ArrayPool<(THeapElement, THeapPriority)> pool)
        {
            _buffer = buffer;
            _count = count;
            _pool = pool;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Return(_buffer);
                _disposed = true;
            }
        }

        public int Count => _disposed
            ? throw new ObjectDisposedException(nameof(UnorderedItemCollection<THeapElement, THeapPriority>))
            : _count;

        public (THeapElement Element, THeapPriority Priority) this[int index]
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(UnorderedItemCollection<THeapElement, THeapPriority>));
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return _buffer[index];
            }
        }

        public IEnumerator<(THeapElement Element, THeapPriority Priority)> GetEnumerator()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnorderedItemCollection<THeapElement, THeapPriority>));

            for (var i = 0; i < _count; i++) yield return _buffer[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}