using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.Daemon.Utils.LazyCell;

namespace MCServerLauncher.Benchmarks.Benchmarks;

/// <summary>
/// Compares the production ValueTask hit path with Task-only cache shapes while
/// keeping the refresh operation single-flight and reusable by all waiters.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class AsyncTimedLazyCellBenchmarks
{
    private static readonly object SharedValue = new();

    private AsyncTimedLazyCell<object> _hybridHot = null!;
    private TaskOnlyTimedCell<object> _taskPerHitHot = null!;
    private TaskCachingTimedCell<object> _taskCachedHot = null!;
    private AsyncTimedLazyCell<object> _hybridStale = null!;
    private TaskOnlyTimedCell<object> _taskPerHitStale = null!;
    private TaskCachingTimedCell<object> _taskCachedStale = null!;

    [Params(64, 1024)]
    public int Waiters { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _hybridHot = new AsyncTimedLazyCell<object>(
            static () => Task.FromResult(SharedValue),
            TimeSpan.FromHours(1));
        _taskPerHitHot = new TaskOnlyTimedCell<object>(
            static () => Task.FromResult(SharedValue),
            TimeSpan.FromHours(1));
        _taskCachedHot = new TaskCachingTimedCell<object>(
            static () => Task.FromResult(SharedValue),
            TimeSpan.FromHours(1));

        _hybridStale = CreateHybridStaleCell();
        _taskPerHitStale = CreateTaskPerHitStaleCell();
        _taskCachedStale = CreateTaskCachedStaleCell();
    }

    [Benchmark]
    public object HybridHotHit() => _hybridHot.Value.GetAwaiter().GetResult();

    [Benchmark]
    public object TaskPerHitHotHit() => _taskPerHitHot.Value.GetAwaiter().GetResult();

    [Benchmark]
    public object TaskCachedHotHit() => _taskCachedHot.Value.GetAwaiter().GetResult();

    [Benchmark]
    public Task<int> HybridFirstLoadSingleFlight() =>
        RunFirstLoadWaveAsync(static factory => new HybridTimedCell<object>(factory, TimeSpan.FromHours(1)));

    [Benchmark]
    public Task<int> TaskPerHitFirstLoadSingleFlight() =>
        RunFirstLoadWaveAsync(static factory => new TaskOnlyTimedCell<object>(factory, TimeSpan.FromHours(1)));

    [Benchmark]
    public Task<int> TaskCachedFirstLoadSingleFlight() =>
        RunFirstLoadWaveAsync(static factory => new TaskCachingTimedCell<object>(factory, TimeSpan.FromHours(1)));

    [Benchmark]
    public object HybridExpiredStaleRead() => _hybridStale.Value.GetAwaiter().GetResult();

    [Benchmark]
    public object TaskPerHitExpiredStaleRead() => _taskPerHitStale.Value.GetAwaiter().GetResult();

    [Benchmark]
    public object TaskCachedExpiredStaleRead() => _taskCachedStale.Value.GetAwaiter().GetResult();

    private Task<int> RunFirstLoadWaveAsync<TCell>(Func<Func<Task<object>>, TCell> createCell)
        where TCell : ITaskValueCell<object>
    {
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cell = createCell(() => release.Task);
        var calls = new Task<object>[Waiters];
        for (var index = 0; index < calls.Length; index++)
        {
            calls[index] = cell.GetValueAsTask();
        }

        release.SetResult(SharedValue);
        return SumAsync(calls);
    }

    private static async Task<int> SumAsync(Task<object>[] calls)
    {
        var values = await Task.WhenAll(calls).ConfigureAwait(false);
        var count = 0;
        foreach (var value in values)
        {
            if (ReferenceEquals(value, SharedValue))
            {
                count++;
            }
        }

        return count;
    }

    private sealed class HybridTimedCell<T> : ITaskValueCell<T>
    {
        private readonly AsyncTimedLazyCell<T> _cell;

        public HybridTimedCell(Func<Task<T>> factory, TimeSpan duration)
        {
            _cell = new AsyncTimedLazyCell<T>(factory, duration);
        }

        public Task<T> GetValueAsTask() => _cell.Value.AsTask();
    }

    private static AsyncTimedLazyCell<object> CreateHybridStaleCell()
    {
        var call = 0;
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cell = new AsyncTimedLazyCell<object>(
            () => Interlocked.Increment(ref call) == 1 ? Task.FromResult(SharedValue) : release.Task,
            TimeSpan.Zero);
        cell.Update().GetAwaiter().GetResult();
        _ = cell.Value;
        return cell;
    }

    private static TaskOnlyTimedCell<object> CreateTaskPerHitStaleCell()
    {
        var call = 0;
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cell = new TaskOnlyTimedCell<object>(
            () => Interlocked.Increment(ref call) == 1 ? Task.FromResult(SharedValue) : release.Task,
            TimeSpan.Zero);
        cell.Update().GetAwaiter().GetResult();
        _ = cell.Value;
        return cell;
    }

    private static TaskCachingTimedCell<object> CreateTaskCachedStaleCell()
    {
        var call = 0;
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cell = new TaskCachingTimedCell<object>(
            () => Interlocked.Increment(ref call) == 1 ? Task.FromResult(SharedValue) : release.Task,
            TimeSpan.Zero);
        cell.Update().GetAwaiter().GetResult();
        _ = cell.Value;
        return cell;
    }

    private interface ITaskValueCell<T>
    {
        Task<T> GetValueAsTask();
    }

    private sealed class TaskOnlyTimedCell<T> : ITaskValueCell<T>
    {
        private readonly Func<Task<T>> _factory;
        private readonly object _gate = new();
        private readonly TimeSpan _duration;
        private Task? _refreshTask;
        private Exception? _lastFailure;
        private T? _value;
        private long _lastUpdatedUtcTicks;

        public TaskOnlyTimedCell(Func<Task<T>> factory, TimeSpan duration)
        {
            _factory = factory;
            _duration = duration;
        }

        public Task<T> Value => GetValueAsync(false);

        public Task Update() => GetValueAsync(true);

        public Task<T> GetValueAsTask() => Value;

        private Task<T> GetValueAsync(bool forceUpdate)
        {
            var now = DateTime.UtcNow;
            var ticks = Volatile.Read(ref _lastUpdatedUtcTicks);
            if (!forceUpdate && ticks != 0 && now - new DateTime(ticks, DateTimeKind.Utc) <= _duration)
            {
                return Task.FromResult(_value!);
            }

            return GetValueSlowAsync(forceUpdate, now);
        }

        private async Task<T> GetValueSlowAsync(bool forceUpdate, DateTime now)
        {
            Task refresh;
            var stale = false;
            T? staleValue = default;
            lock (_gate)
            {
                var ticks = _lastUpdatedUtcTicks;
                var hasValue = ticks != 0;
                var expired = !hasValue || forceUpdate ||
                              now - new DateTime(ticks, DateTimeKind.Utc) > _duration;
                if (!expired)
                {
                    return _value!;
                }

                if (_refreshTask is { IsCompleted: false })
                {
                    refresh = _refreshTask;
                }
                else
                {
                    refresh = RefreshCoreAsync();
                    _refreshTask = refresh;
                }

                if (hasValue && !forceUpdate)
                {
                    stale = true;
                    staleValue = _value;
                }
            }

            if (stale)
            {
                return staleValue!;
            }

            try
            {
                await refresh.ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    if (_lastUpdatedUtcTicks != 0 && !forceUpdate)
                    {
                        return _value!;
                    }
                }

                throw;
            }

            lock (_gate)
            {
                return _lastUpdatedUtcTicks != 0
                    ? _value!
                    : throw (_lastFailure ?? new InvalidOperationException("Refresh produced no value."));
            }
        }

        private async Task RefreshCoreAsync()
        {
            try
            {
                var value = await _factory().ConfigureAwait(false);
                lock (_gate)
                {
                    _value = value;
                    _lastUpdatedUtcTicks = DateTime.UtcNow.Ticks;
                    _lastFailure = null;
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _lastFailure = exception;
                }

                throw;
            }
        }
    }

    private sealed class TaskCachingTimedCell<T> : ITaskValueCell<T>
    {
        private readonly Func<Task<T>> _factory;
        private readonly object _gate = new();
        private readonly TimeSpan _duration;
        private Task<T>? _valueTask;
        private Task? _refreshTask;
        private Exception? _lastFailure;
        private T? _value;
        private long _lastUpdatedUtcTicks;

        public TaskCachingTimedCell(Func<Task<T>> factory, TimeSpan duration)
        {
            _factory = factory;
            _duration = duration;
        }

        public Task<T> Value => GetValueAsync(false);

        public Task Update() => GetValueAsync(true);

        public Task<T> GetValueAsTask() => Value;

        private Task<T> GetValueAsync(bool forceUpdate)
        {
            var now = DateTime.UtcNow;
            var ticks = Volatile.Read(ref _lastUpdatedUtcTicks);
            if (!forceUpdate && ticks != 0 && now - new DateTime(ticks, DateTimeKind.Utc) <= _duration)
            {
                return Volatile.Read(ref _valueTask) ?? Task.FromResult(_value!);
            }

            return GetValueSlowAsync(forceUpdate, now);
        }

        private async Task<T> GetValueSlowAsync(bool forceUpdate, DateTime now)
        {
            Task refresh;
            var stale = false;
            T? staleValue = default;
            lock (_gate)
            {
                var ticks = _lastUpdatedUtcTicks;
                var hasValue = ticks != 0;
                var expired = !hasValue || forceUpdate ||
                              now - new DateTime(ticks, DateTimeKind.Utc) > _duration;
                if (!expired)
                {
                    return _value!;
                }

                if (_refreshTask is { IsCompleted: false })
                {
                    refresh = _refreshTask;
                }
                else
                {
                    refresh = RefreshCoreAsync();
                    _refreshTask = refresh;
                }

                if (hasValue && !forceUpdate)
                {
                    stale = true;
                    staleValue = _value;
                }
            }

            if (stale)
            {
                return staleValue!;
            }

            try
            {
                await refresh.ConfigureAwait(false);
            }
            catch
            {
                lock (_gate)
                {
                    if (_lastUpdatedUtcTicks != 0 && !forceUpdate)
                    {
                        return _value!;
                    }
                }

                throw;
            }

            lock (_gate)
            {
                return _lastUpdatedUtcTicks != 0
                    ? _value!
                    : throw (_lastFailure ?? new InvalidOperationException("Refresh produced no value."));
            }
        }

        private async Task RefreshCoreAsync()
        {
            try
            {
                var value = await _factory().ConfigureAwait(false);
                lock (_gate)
                {
                    _value = value;
                    _valueTask = Task.FromResult(value);
                    _lastUpdatedUtcTicks = DateTime.UtcNow.Ticks;
                    _lastFailure = null;
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _lastFailure = exception;
                }

                throw;
            }
        }
    }
}
