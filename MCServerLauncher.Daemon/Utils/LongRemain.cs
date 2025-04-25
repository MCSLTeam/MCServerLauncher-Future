namespace MCServerLauncher.Daemon.Utils;

/// <summary>
///     -2^63 ~ 2^63 - 1内的整数区间(只支持减去某一子区间)，用于上传文件数据的记录
/// </summary>
public class LongRemain
{
    private LongRemainNode? _head;

    /// <summary>
    ///     整数区间: [Begin, End)
    /// </summary>
    /// <param name="begin"></param>
    /// <param name="end"></param>
    public LongRemain(long begin, long end)
    {
        Begin = begin;
        End = end;
        _head = new LongRemainNode(begin, end);
    }

    public long Begin { get; private set; }
    public long End { get; private set; }

    /// <summary>
    ///     减去[from, to)
    /// </summary>
    /// <param name="from">闭区间</param>
    /// <param name="to">开区间</param>
    /// <returns>自身,用于链式操作</returns>
    public LongRemain Reduce(long from, long to)
    {
        LongRemainNode? lastNode = null;
        var node = _head;
        while (node != null)
        {
            if (from <= node.Begin && to >= node.End)
            {
                if (lastNode == null)
                    _head = _head?.Next;
                else lastNode.Next = node.Next;

                break;
            }

            if (from > node.Begin && to < node.End)
            {
                var next = new LongRemainNode(to, node.End);
                node.End = from;
                next.Next = node.Next;
                node.Next = next;
                break;
            }

            if (node.Begin < from && from < node.End)
            {
                // to >= node.Begin
                node.End = from;
                break;
            }


            if (node.Begin < to && to < node.End)
            {
                // from <= node.End
                node.Begin = to;
                break;
            }

            if (to < node.Begin) break; // break

            lastNode = node;
            node = node.Next;
        }

        return this;
    }

    /// <summary>
    ///     获取剩余区间
    /// </summary>
    /// <returns></returns>
    public IEnumerable<(long Begin, long End)> GetRemains()
    {
        var node = _head;
        while (node != null)
        {
            yield return (node.Begin, node.End);
            node = node.Next;
        }
    }


    /// <summary>
    ///     获取剩余区间的总长度
    /// </summary>
    /// <returns></returns>
    public long GetRemain()
    {
        long remain = 0;
        foreach (var (begin, end) in GetRemains()) remain += end - begin;
        return remain;
    }

    /// <summary>
    ///     判断是否完成
    /// </summary>
    /// <returns></returns>
    public bool Done()
    {
        return _head == null;
    }


    /// <summary>
    ///     节点
    /// </summary>
    private class LongRemainNode
    {
        public LongRemainNode(long begin, long end)
        {
            Begin = begin;
            End = end;
        }

        public long Begin { get; set; }
        public long End { get; set; }
        public LongRemainNode? Next { get; set; }
    }
}